using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a data source connection for indexers.
/// </summary>
public class DataSource
{
    /// <summary>
    /// Name of the data source.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the data source.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Type of data source (e.g., "azureblob", "azuresql", "filesystem").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Connection credentials (simplified for simulator).
    /// </summary>
    [JsonPropertyName("credentials")]
    public DataSourceCredentials? Credentials { get; set; }

    /// <summary>
    /// Container information (blob container, table name, etc.).
    /// </summary>
    [JsonPropertyName("container")]
    public DataSourceContainer Container { get; set; } = new();

    /// <summary>
    /// Data change detection policy.
    /// </summary>
    [JsonPropertyName("dataChangeDetectionPolicy")]
    public DataChangeDetectionPolicy? DataChangeDetectionPolicy { get; set; }

    /// <summary>
    /// Data deletion detection policy.
    /// </summary>
    [JsonPropertyName("dataDeletionDetectionPolicy")]
    public DataDeletionDetectionPolicy? DataDeletionDetectionPolicy { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ODataETag { get; set; }
}

/// <summary>
/// Credentials for connecting to a data source.
/// </summary>
public class DataSourceCredentials
{
    /// <summary>
    /// Connection string (for simulator, this is the local path).
    /// </summary>
    [JsonPropertyName("connectionString")]
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Container configuration for a data source.
/// </summary>
public class DataSourceContainer
{
    /// <summary>
    /// Name of the container (folder name for file system).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Query to filter documents (subfolder path for file system).
    /// </summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}

/// <summary>
/// Policy for detecting data changes.
/// </summary>
public class DataChangeDetectionPolicy
{
    /// <summary>
    /// Type of detection policy.
    /// </summary>
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.HighWaterMarkChangeDetectionPolicy";

    /// <summary>
    /// Column name for high water mark (for simulator: "lastModified").
    /// </summary>
    [JsonPropertyName("highWaterMarkColumnName")]
    public string? HighWaterMarkColumnName { get; set; }
}

/// <summary>
/// Policy for detecting deleted data.
/// </summary>
public class DataDeletionDetectionPolicy
{
    /// <summary>
    /// Type of deletion policy.
    /// </summary>
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.SoftDeleteColumnDeletionDetectionPolicy";

    /// <summary>
    /// Column name that indicates soft delete.
    /// </summary>
    [JsonPropertyName("softDeleteColumnName")]
    public string? SoftDeleteColumnName { get; set; }

    /// <summary>
    /// Value that indicates the document is deleted.
    /// </summary>
    [JsonPropertyName("softDeleteMarkerValue")]
    public string? SoftDeleteMarkerValue { get; set; }
}

/// <summary>
/// Supported data source types.
/// </summary>
public static class DataSourceType
{
    public const string AzureBlob = "azureblob";
    public const string AdlsGen2 = "adlsgen2";
    public const string AzureSql = "azuresql";
    public const string AzureTable = "azuretable";
    public const string CosmosDb = "cosmosdb";
    public const string FileSystem = "filesystem"; // Simulator-specific
}
