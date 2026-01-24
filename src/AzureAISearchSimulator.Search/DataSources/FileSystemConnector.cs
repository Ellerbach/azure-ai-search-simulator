using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Search.DataSources;

/// <summary>
/// Connector for reading files from the local file system.
/// Simulates Azure Blob Storage by treating folders as containers.
/// </summary>
public class FileSystemConnector : IDataSourceConnector
{
    private readonly ILogger<FileSystemConnector> _logger;

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

    public FileSystemConnector(ILogger<FileSystemConnector> logger)
    {
        _logger = logger;
    }

    public string Type => DataSourceType.FileSystem;

    public Task<bool> TestConnectionAsync(DataSource dataSource)
    {
        var path = GetBasePath(dataSource);
        var exists = Directory.Exists(path);
        
        if (!exists)
        {
            _logger.LogWarning("Directory does not exist: {Path}", path);
        }
        
        return Task.FromResult(exists);
    }

    public Task<IEnumerable<DataSourceDocument>> ListDocumentsAsync(DataSource dataSource, string? trackingState = null)
    {
        var basePath = GetBasePath(dataSource);
        var documents = new List<DataSourceDocument>();

        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Directory does not exist: {Path}", basePath);
            return Task.FromResult<IEnumerable<DataSourceDocument>>(documents);
        }

        // Parse tracking state (last modified time)
        DateTimeOffset? lastTrackingTime = null;
        if (!string.IsNullOrEmpty(trackingState) && DateTimeOffset.TryParse(trackingState, out var parsedTime))
        {
            lastTrackingTime = parsedTime;
        }

        // Get search pattern from query if specified
        var searchPattern = "*.*";
        if (!string.IsNullOrEmpty(dataSource.Container?.Query))
        {
            searchPattern = dataSource.Container.Query;
        }

        var files = Directory.GetFiles(basePath, searchPattern, SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);

                // Skip if file hasn't changed since last tracking
                if (lastTrackingTime.HasValue && lastModified <= lastTrackingTime.Value)
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
                var key = GenerateKey(relativePath);

                documents.Add(new DataSourceDocument
                {
                    Key = key,
                    Name = relativePath,
                    Content = File.ReadAllBytes(filePath),
                    ContentType = GetMimeType(filePath),
                    LastModified = lastModified,
                    Size = fileInfo.Length,
                    Metadata = new Dictionary<string, object>
                    {
                        ["metadata_storage_path"] = relativePath,
                        ["metadata_storage_name"] = fileInfo.Name,
                        ["metadata_storage_size"] = fileInfo.Length,
                        ["metadata_storage_last_modified"] = lastModified.ToString("O"),
                        ["metadata_storage_content_type"] = GetMimeType(filePath),
                        ["metadata_storage_file_extension"] = fileInfo.Extension.ToLowerInvariant()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file: {FilePath}", filePath);
            }
        }

        _logger.LogInformation("Found {Count} documents in {Path}", documents.Count, basePath);
        return Task.FromResult<IEnumerable<DataSourceDocument>>(documents);
    }

    public Task<DataSourceDocument?> GetDocumentAsync(DataSource dataSource, string key)
    {
        var basePath = GetBasePath(dataSource);
        var files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');
            var fileKey = GenerateKey(relativePath);

            if (fileKey == key)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    return Task.FromResult<DataSourceDocument?>(new DataSourceDocument
                    {
                        Key = fileKey,
                        Name = relativePath,
                        Content = File.ReadAllBytes(filePath),
                        ContentType = GetMimeType(filePath),
                        LastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                        Size = fileInfo.Length,
                        Metadata = new Dictionary<string, object>
                        {
                            ["metadata_storage_path"] = relativePath,
                            ["metadata_storage_name"] = fileInfo.Name,
                            ["metadata_storage_size"] = fileInfo.Length,
                            ["metadata_storage_last_modified"] = fileInfo.LastWriteTimeUtc.ToString("O"),
                            ["metadata_storage_content_type"] = GetMimeType(filePath),
                            ["metadata_storage_file_extension"] = fileInfo.Extension.ToLowerInvariant()
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read file: {FilePath}", filePath);
                }
            }
        }

        return Task.FromResult<DataSourceDocument?>(null);
    }

    private string GetBasePath(DataSource dataSource)
    {
        // Connection string can be in format "path=C:\some\path" or just "C:\some\path"
        var connectionString = dataSource.Credentials?.ConnectionString 
            ?? dataSource.ConnectionString  // Also check direct property
            ?? ".";
        
        // Parse "path=" prefix if present
        if (connectionString.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = connectionString.Substring(5);
        }
        
        var containerPath = dataSource.Container?.Name ?? "";
        
        // If container is "." treat it as root (no subdirectory)
        if (containerPath == ".")
        {
            containerPath = "";
        }
        
        var basePath = string.IsNullOrEmpty(containerPath) 
            ? connectionString 
            : Path.Combine(connectionString, containerPath);
            
        _logger.LogInformation("FileSystemConnector.GetBasePath: connectionString={ConnectionString}, container={Container}, result={BasePath}", 
            dataSource.Credentials?.ConnectionString ?? dataSource.ConnectionString, 
            dataSource.Container?.Name, 
            basePath);
        
        return basePath;
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

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return MimeTypes.TryGetValue(extension, out var mimeType) 
            ? mimeType 
            : "application/octet-stream";
    }
}
