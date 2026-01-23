using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.DataSources;

/// <summary>
/// Represents a document retrieved from a data source.
/// </summary>
public class DataSourceDocument
{
    /// <summary>
    /// Unique key for the document.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Raw content bytes.
    /// </summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Content type (MIME type).
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// File name or path.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Metadata properties.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Last modified timestamp.
    /// </summary>
    public DateTimeOffset? LastModified { get; set; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long Size { get; set; }
}

/// <summary>
/// Connector interface for reading documents from a data source.
/// </summary>
public interface IDataSourceConnector
{
    /// <summary>
    /// Data source type this connector handles.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Tests the connection to the data source.
    /// </summary>
    Task<bool> TestConnectionAsync(DataSource dataSource);

    /// <summary>
    /// Lists all documents in the data source.
    /// </summary>
    Task<IEnumerable<DataSourceDocument>> ListDocumentsAsync(DataSource dataSource, string? trackingState = null);

    /// <summary>
    /// Gets a single document by key.
    /// </summary>
    Task<DataSourceDocument?> GetDocumentAsync(DataSource dataSource, string key);
}
