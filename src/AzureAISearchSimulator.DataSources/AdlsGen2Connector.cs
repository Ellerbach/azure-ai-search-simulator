using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services.Credentials;
using AzureAISearchSimulator.Search.DataSources;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.DataSources;

/// <summary>
/// Connector for reading documents from Azure Data Lake Storage Gen2 (ADLS Gen2).
/// Supports connection string, SAS tokens, and managed identity authentication.
/// ADLS Gen2 provides hierarchical namespace support with better performance for big data scenarios.
/// </summary>
public class AdlsGen2Connector : IDataSourceConnector
{
    private readonly ILogger<AdlsGen2Connector> _logger;
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
        { ".parquet", "application/vnd.apache.parquet" },
        { ".avro", "application/avro" },
        { ".orc", "application/orc" },
    };

    public AdlsGen2Connector(
        ILogger<AdlsGen2Connector> logger,
        ICredentialFactory credentialFactory)
    {
        _logger = logger;
        _credentialFactory = credentialFactory;
    }

    /// <summary>
    /// Returns the data source type. Uses "adlsgen2" for explicit ADLS Gen2 or "azureblob" 
    /// as ADLS Gen2 is built on top of Blob Storage.
    /// </summary>
    public string Type => "adlsgen2";

    public async Task<bool> TestConnectionAsync(DataSource dataSource)
    {
        try
        {
            var fileSystemClient = GetFileSystemClient(dataSource);
            return await fileSystemClient.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection to ADLS Gen2");
            return false;
        }
    }

    public async Task<IEnumerable<DataSourceDocument>> ListDocumentsAsync(DataSource dataSource, string? trackingState = null)
    {
        var documents = new List<DataSourceDocument>();

        try
        {
            var fileSystemClient = GetFileSystemClient(dataSource);

            if (!await fileSystemClient.ExistsAsync())
            {
                _logger.LogWarning("File system (container) does not exist: {Container}", dataSource.Container?.Name);
                return documents;
            }

            // Parse tracking state (last modified time)
            DateTimeOffset? lastTrackingTime = null;
            if (!string.IsNullOrEmpty(trackingState) && DateTimeOffset.TryParse(trackingState, out var parsedTime))
            {
                lastTrackingTime = parsedTime;
            }

            // Get directory path from query if specified (supports hierarchical navigation)
            var directoryPath = dataSource.Container?.Query ?? "";

            await ListFilesRecursivelyAsync(fileSystemClient, directoryPath, documents, lastTrackingTime);

            _logger.LogInformation("Found {Count} documents in ADLS Gen2 file system {FileSystem}", 
                documents.Count, dataSource.Container?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list files from ADLS Gen2");
        }

        return documents;
    }

    private async Task ListFilesRecursivelyAsync(
        DataLakeFileSystemClient fileSystemClient, 
        string directoryPath, 
        List<DataSourceDocument> documents,
        DateTimeOffset? lastTrackingTime)
    {
        DataLakeDirectoryClient directoryClient;
        
        if (string.IsNullOrEmpty(directoryPath))
        {
            directoryClient = fileSystemClient.GetDirectoryClient("/");
        }
        else
        {
            directoryClient = fileSystemClient.GetDirectoryClient(directoryPath);
        }

        try
        {
            await foreach (var pathItem in fileSystemClient.GetPathsAsync(directoryPath, recursive: true))
            {
                // Skip directories
                if (pathItem.IsDirectory == true)
                {
                    continue;
                }

                try
                {
                    // Skip if file hasn't changed since last tracking
                    if (lastTrackingTime.HasValue && pathItem.LastModified <= lastTrackingTime)
                    {
                        continue;
                    }

                    var fileClient = fileSystemClient.GetFileClient(pathItem.Name);
                    var content = await DownloadFileContentAsync(fileClient);

                    var key = GenerateKey(pathItem.Name);

                    documents.Add(new DataSourceDocument
                    {
                        Key = key,
                        Name = pathItem.Name,
                        Content = content,
                        ContentType = GetMimeType(pathItem.Name),
                        LastModified = pathItem.LastModified,
                        Size = pathItem.ContentLength ?? 0,
                        Metadata = BuildMetadata(pathItem, fileSystemClient.Uri.ToString())
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read file: {FilePath}", pathItem.Name);
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Directory not found: {Path}", directoryPath);
        }
    }

    public async Task<DataSourceDocument?> GetDocumentAsync(DataSource dataSource, string key)
    {
        try
        {
            var fileSystemClient = GetFileSystemClient(dataSource);

            if (!await fileSystemClient.ExistsAsync())
            {
                return null;
            }

            // Decode key to get file path
            var filePath = DecodeKey(key);
            var fileClient = fileSystemClient.GetFileClient(filePath);

            if (!await fileClient.ExistsAsync())
            {
                // Try to find by iterating (in case key encoding differs)
                await foreach (var pathItem in fileSystemClient.GetPathsAsync(recursive: true))
                {
                    if (pathItem.IsDirectory == true) continue;
                    
                    if (GenerateKey(pathItem.Name) == key)
                    {
                        fileClient = fileSystemClient.GetFileClient(pathItem.Name);
                        break;
                    }
                }
            }

            if (!await fileClient.ExistsAsync())
            {
                return null;
            }

            var properties = await fileClient.GetPropertiesAsync();
            var content = await DownloadFileContentAsync(fileClient);

            return new DataSourceDocument
            {
                Key = key,
                Name = fileClient.Path,
                Content = content,
                ContentType = properties.Value.ContentType ?? GetMimeType(fileClient.Path),
                LastModified = properties.Value.LastModified,
                Size = properties.Value.ContentLength,
                Metadata = new Dictionary<string, object>
                {
                    ["metadata_storage_path"] = fileClient.Uri.ToString(),
                    ["metadata_storage_name"] = Path.GetFileName(fileClient.Path),
                    ["metadata_storage_size"] = properties.Value.ContentLength,
                    ["metadata_storage_last_modified"] = properties.Value.LastModified.ToString("O"),
                    ["metadata_storage_content_type"] = properties.Value.ContentType ?? GetMimeType(fileClient.Path),
                    ["metadata_storage_file_extension"] = Path.GetExtension(fileClient.Path).ToLowerInvariant()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file with key: {Key}", key);
            return null;
        }
    }

    private DataLakeFileSystemClient GetFileSystemClient(DataSource dataSource)
    {
        var connectionString = dataSource.Credentials?.ConnectionString;
        var fileSystemName = dataSource.Container?.Name ?? throw new ArgumentException("Container (file system) name is required");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Connection string or account URL is required for ADLS Gen2");
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
            var serviceClient = new DataLakeServiceClient(connectionString);
            return serviceClient.GetFileSystemClient(fileSystemName);
        }

        // Check if it's a DFS endpoint URL (https://account.dfs.core.windows.net)
        if (connectionString.Contains(".dfs.core.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            // Use credential factory for managed identity
            var credential = GetCredentialForDataSource(identity);
            _logger.LogDebug("Using managed identity for ADLS Gen2 with DFS endpoint");
            var serviceClient = new DataLakeServiceClient(new Uri(connectionString), credential);
            return serviceClient.GetFileSystemClient(fileSystemName);
        }
        else if (connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                 connectionString.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            // Generic HTTPS URL - convert to DFS endpoint if needed
            var credential = GetCredentialForDataSource(identity);
            _logger.LogDebug("Using managed identity for ADLS Gen2");
            var dfsUri = ConvertToDfsEndpoint(connectionString);
            var serviceClient = new DataLakeServiceClient(new Uri(dfsUri), credential);
            return serviceClient.GetFileSystemClient(fileSystemName);
        }
        else if (connectionString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
                 connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase))
        {
            // Use connection string with key or SAS
            _logger.LogDebug("Using connection string authentication for ADLS Gen2");
            var serviceClient = new DataLakeServiceClient(connectionString);
            return serviceClient.GetFileSystemClient(fileSystemName);
        }
        else
        {
            // Assume it's an account name, use managed identity
            var accountUrl = $"https://{connectionString}.dfs.core.windows.net";
            var credential = GetCredentialForDataSource(identity);
            _logger.LogDebug("Using managed identity with account name for ADLS Gen2: {Account}", connectionString);
            var serviceClient = new DataLakeServiceClient(new Uri(accountUrl), credential);
            return serviceClient.GetFileSystemClient(fileSystemName);
        }
    }

    private TokenCredential GetCredentialForDataSource(SearchIdentity? identity)
    {
        return _credentialFactory.GetCredential(identity: identity);
    }

    private static SearchIdentity? ParseIdentity(DataSource dataSource)
    {
        // Check if data source has identity configuration
        // This would typically come from the data source's Identity property
        // For now, we return null to use default credential
        return null;
    }

    private static string ConvertToDfsEndpoint(string url)
    {
        // Convert blob endpoint to DFS endpoint if necessary
        // https://account.blob.core.windows.net -> https://account.dfs.core.windows.net
        return url.Replace(".blob.core.windows.net", ".dfs.core.windows.net", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> DownloadFileContentAsync(DataLakeFileClient fileClient)
    {
        using var stream = new MemoryStream();
        await fileClient.ReadToAsync(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, object> BuildMetadata(PathItem pathItem, string fileSystemUri)
    {
        var metadata = new Dictionary<string, object>
        {
            ["metadata_storage_path"] = $"{fileSystemUri.TrimEnd('/')}/{pathItem.Name}",
            ["metadata_storage_name"] = Path.GetFileName(pathItem.Name),
            ["metadata_storage_size"] = pathItem.ContentLength ?? 0,
            ["metadata_storage_last_modified"] = pathItem.LastModified.ToString("O"),
            ["metadata_storage_content_type"] = GetMimeType(pathItem.Name),
            ["metadata_storage_file_extension"] = Path.GetExtension(pathItem.Name).ToLowerInvariant()
        };

        // Add owner and group if available (ADLS Gen2 specific)
        if (!string.IsNullOrEmpty(pathItem.Owner))
        {
            metadata["metadata_owner"] = pathItem.Owner;
        }
        if (!string.IsNullOrEmpty(pathItem.Group))
        {
            metadata["metadata_group"] = pathItem.Group;
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
