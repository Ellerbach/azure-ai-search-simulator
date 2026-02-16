using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services.Credentials;
using AzureAISearchSimulator.Search.DataSources;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.DataSources;

/// <summary>
/// Connector for reading documents from Azure Blob Storage.
/// Supports connection string, SAS tokens, and managed identity authentication.
/// </summary>
public class AzureBlobStorageConnector : IDataSourceConnector
{
    private readonly ILogger<AzureBlobStorageConnector> _logger;
    private readonly ICredentialFactory _credentialFactory;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".txt", "text/plain" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".pdf", "application/pdf" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls", "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".csv", "text/csv" },
        { ".md", "text/markdown" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
    };

    public AzureBlobStorageConnector(
        ILogger<AzureBlobStorageConnector> logger,
        ICredentialFactory credentialFactory)
    {
        _logger = logger;
        _credentialFactory = credentialFactory;
    }

    public string Type => DataSourceType.AzureBlob;

    public async Task<bool> TestConnectionAsync(DataSource dataSource)
    {
        try
        {
            var containerClient = GetContainerClient(dataSource);
            return await containerClient.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection to Azure Blob Storage");
            return false;
        }
    }

    public async Task<IEnumerable<DataSourceDocument>> ListDocumentsAsync(DataSource dataSource, string? trackingState = null)
    {
        var documents = new List<DataSourceDocument>();

        try
        {
            var containerClient = GetContainerClient(dataSource);

            if (!await containerClient.ExistsAsync())
            {
                _logger.LogWarning("Container does not exist: {Container}", dataSource.Container?.Name);
                return documents;
            }

            // Parse tracking state (last modified time)
            DateTimeOffset? lastTrackingTime = null;
            if (!string.IsNullOrEmpty(trackingState) && DateTimeOffset.TryParse(trackingState, out var parsedTime))
            {
                lastTrackingTime = parsedTime;
            }

            // Get prefix from query if specified
            var prefix = dataSource.Container?.Query;

            await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix, default))
            {
                try
                {
                    // Skip if blob hasn't changed since last tracking
                    if (lastTrackingTime.HasValue && blobItem.Properties.LastModified <= lastTrackingTime)
                    {
                        continue;
                    }

                    var key = GenerateKey(blobItem.Name);

                    // Metadata-only: content is NOT downloaded here for performance.
                    // Use DownloadContentAsync to fetch content when needed.
                    documents.Add(new DataSourceDocument
                    {
                        Key = key,
                        Name = blobItem.Name,
                        Content = Array.Empty<byte>(),
                        ContentType = blobItem.Properties.ContentType ?? GetMimeType(blobItem.Name),
                        LastModified = blobItem.Properties.LastModified,
                        Size = blobItem.Properties.ContentLength ?? 0,
                        Metadata = BuildMetadata(blobItem, containerClient.Uri.ToString())
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read blob: {BlobName}", blobItem.Name);
                }
            }

            _logger.LogInformation("Found {Count} documents in container {Container}", 
                documents.Count, dataSource.Container?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list blobs from Azure Blob Storage");
        }

        return documents;
    }

    public async Task DownloadContentAsync(DataSource dataSource, DataSourceDocument document)
    {
        try
        {
            var containerClient = GetContainerClient(dataSource);
            var blobClient = containerClient.GetBlobClient(document.Name);
            document.Content = await DownloadBlobContentAsync(blobClient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download content for blob: {BlobName}", document.Name);
            throw;
        }
    }

    public async Task<DataSourceDocument?> GetDocumentAsync(DataSource dataSource, string key)
    {
        try
        {
            var containerClient = GetContainerClient(dataSource);

            if (!await containerClient.ExistsAsync())
            {
                return null;
            }

            // Decode key to get blob name
            var blobName = DecodeKey(key);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                // Try to find by iterating (in case key encoding differs)
                await foreach (var blobItem in containerClient.GetBlobsAsync())
                {
                    if (GenerateKey(blobItem.Name) == key)
                    {
                        blobClient = containerClient.GetBlobClient(blobItem.Name);
                        break;
                    }
                }
            }

            if (!await blobClient.ExistsAsync())
            {
                return null;
            }

            var properties = await blobClient.GetPropertiesAsync();
            var content = await DownloadBlobContentAsync(blobClient);

            return new DataSourceDocument
            {
                Key = key,
                Name = blobClient.Name,
                Content = content,
                ContentType = properties.Value.ContentType ?? GetMimeType(blobClient.Name),
                LastModified = properties.Value.LastModified,
                Size = properties.Value.ContentLength,
                Metadata = new Dictionary<string, object>
                {
                    ["metadata_storage_path"] = blobClient.Uri.ToString(),
                    ["metadata_storage_name"] = Path.GetFileName(blobClient.Name),
                    ["metadata_storage_size"] = properties.Value.ContentLength,
                    ["metadata_storage_last_modified"] = properties.Value.LastModified.ToString("O"),
                    ["metadata_storage_content_type"] = properties.Value.ContentType ?? GetMimeType(blobClient.Name),
                    ["metadata_storage_file_extension"] = Path.GetExtension(blobClient.Name).ToLowerInvariant()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blob with key: {Key}", key);
            return null;
        }
    }

    private BlobContainerClient GetContainerClient(DataSource dataSource)
    {
        var connectionString = dataSource.Credentials?.ConnectionString;
        var containerName = dataSource.Container?.Name ?? throw new ArgumentException("Container name is required");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Connection string is required for Azure Blob Storage");
        }

        // Parse identity from data source if specified
        var identity = ParseIdentity(dataSource);

        // If identity is explicitly set to None, must use connection string with key
        if (identity?.IsNone == true)
        {
            if (!connectionString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) &&
                !connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Identity is set to None but connection string doesn't contain AccountKey or SharedAccessSignature");
            }

            _logger.LogDebug("Using connection string authentication (identity=none)");
            var serviceClient = new BlobServiceClient(connectionString);
            return serviceClient.GetBlobContainerClient(containerName);
        }

        // Check if it's a connection string or account URL
        if (connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            // Use credential factory for managed identity
            var credential = GetCredentialForDataSource(identity);
            _logger.LogDebug("Using managed identity for Azure Blob Storage");
            var serviceClient = new BlobServiceClient(new Uri(connectionString), credential);
            return serviceClient.GetBlobContainerClient(containerName);
        }
        else if (connectionString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
                 connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase))
        {
            // Use connection string with key or SAS
            _logger.LogDebug("Using connection string authentication for Azure Blob Storage");
            var serviceClient = new BlobServiceClient(connectionString);
            return serviceClient.GetBlobContainerClient(containerName);
        }
        else
        {
            // Assume it's an account name, use managed identity
            var accountUrl = $"https://{connectionString}.blob.core.windows.net";
            var credential = GetCredentialForDataSource(identity);
            _logger.LogDebug("Using managed identity with account name for Azure Blob Storage: {Account}", connectionString);
            var serviceClient = new BlobServiceClient(new Uri(accountUrl), credential);
            return serviceClient.GetBlobContainerClient(containerName);
        }
    }

    private TokenCredential GetCredentialForDataSource(SearchIdentity? identity)
    {
        return _credentialFactory.GetCredential(identity: identity);
    }

    private static SearchIdentity? ParseIdentity(DataSource dataSource)
    {
        // Convert ResourceIdentity from the model to SearchIdentity for the credential factory
        var resourceIdentity = dataSource.Identity;
        if (resourceIdentity == null)
        {
            return null; // Use default credential
        }

        return new SearchIdentity
        {
            ODataType = resourceIdentity.ODataType,
            UserAssignedIdentity = resourceIdentity.UserAssignedIdentity
        };
    }

    private static async Task<byte[]> DownloadBlobContentAsync(BlobClient blobClient)
    {
        using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, object> BuildMetadata(BlobItem blobItem, string containerUri)
    {
        var metadata = new Dictionary<string, object>
        {
            ["metadata_storage_path"] = $"{containerUri}/{blobItem.Name}",
            ["metadata_storage_name"] = Path.GetFileName(blobItem.Name),
            ["metadata_storage_size"] = blobItem.Properties.ContentLength ?? 0,
            ["metadata_storage_last_modified"] = blobItem.Properties.LastModified?.ToString("O") ?? "",
            ["metadata_storage_content_type"] = blobItem.Properties.ContentType ?? GetMimeType(blobItem.Name),
            ["metadata_storage_file_extension"] = Path.GetExtension(blobItem.Name).ToLowerInvariant()
        };

        // Add blob metadata if present
        if (blobItem.Metadata != null)
        {
            foreach (var kvp in blobItem.Metadata)
            {
                metadata[$"metadata_{kvp.Key}"] = kvp.Value;
            }
        }

        return metadata;
    }

    private static string GenerateKey(string path)
    {
        // Create a URL-safe base64 key from the path
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string DecodeKey(string key)
    {
        // Restore base64 padding and decode
        var base64 = key.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        base64 += new string('=', padding);
        
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return key;
        }
    }

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return MimeTypes.TryGetValue(extension, out var mimeType) 
            ? mimeType 
            : "application/octet-stream";
    }
}
