using System.Text.Json.Serialization;
using LiteDB;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents an indexer that pulls data from a data source into an index.
/// </summary>
public class Indexer
{
    /// <summary>
    /// Internal ID for LiteDB storage (not serialized to JSON).
    /// </summary>
    [BsonId]
    [JsonIgnore]
    public int InternalId { get; set; }

    /// <summary>
    /// Name of the indexer.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the indexer.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Name of the data source to pull from.
    /// </summary>
    [JsonPropertyName("dataSourceName")]
    public string DataSourceName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the target index.
    /// </summary>
    [JsonPropertyName("targetIndexName")]
    public string TargetIndexName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the skillset to apply (optional).
    /// </summary>
    [JsonPropertyName("skillsetName")]
    public string? SkillsetName { get; set; }

    /// <summary>
    /// Schedule for automatic execution.
    /// </summary>
    [JsonPropertyName("schedule")]
    public IndexerSchedule? Schedule { get; set; }

    /// <summary>
    /// Field mappings from source to index.
    /// </summary>
    [JsonPropertyName("fieldMappings")]
    public List<FieldMapping>? FieldMappings { get; set; }

    /// <summary>
    /// Output field mappings from skillset to index.
    /// </summary>
    [JsonPropertyName("outputFieldMappings")]
    public List<FieldMapping>? OutputFieldMappings { get; set; }

    /// <summary>
    /// Indexer parameters.
    /// </summary>
    [JsonPropertyName("parameters")]
    public IndexerParameters? Parameters { get; set; }

    /// <summary>
    /// Whether the indexer is disabled.
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ODataETag { get; set; }
}

/// <summary>
/// Schedule for indexer execution.
/// </summary>
public class IndexerSchedule
{
    /// <summary>
    /// Interval between executions (ISO 8601 duration).
    /// </summary>
    [JsonPropertyName("interval")]
    public string Interval { get; set; } = "PT5M"; // Default 5 minutes

    /// <summary>
    /// Start time for scheduled execution.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; set; }
}

/// <summary>
/// Mapping between source and target fields.
/// </summary>
public class FieldMapping
{
    /// <summary>
    /// Source field name.
    /// </summary>
    [JsonPropertyName("sourceFieldName")]
    public string SourceFieldName { get; set; } = string.Empty;

    /// <summary>
    /// Target field name in the index.
    /// </summary>
    [JsonPropertyName("targetFieldName")]
    public string? TargetFieldName { get; set; }

    /// <summary>
    /// Mapping function to apply.
    /// </summary>
    [JsonPropertyName("mappingFunction")]
    public FieldMappingFunction? MappingFunction { get; set; }
}

/// <summary>
/// Function to transform field values during mapping.
/// </summary>
public class FieldMappingFunction
{
    /// <summary>
    /// Name of the function (e.g., "base64Encode", "extractTokenAtPosition").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parameters for the function.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Parameters controlling indexer behavior.
/// </summary>
public class IndexerParameters
{
    /// <summary>
    /// Batch size for indexing.
    /// </summary>
    [JsonPropertyName("batchSize")]
    public int? BatchSize { get; set; }

    /// <summary>
    /// Maximum number of items to fail before stopping.
    /// </summary>
    [JsonPropertyName("maxFailedItems")]
    public int? MaxFailedItems { get; set; }

    /// <summary>
    /// Maximum number of items to fail per batch.
    /// </summary>
    [JsonPropertyName("maxFailedItemsPerBatch")]
    public int? MaxFailedItemsPerBatch { get; set; }

    /// <summary>
    /// Configuration settings.
    /// </summary>
    [JsonPropertyName("configuration")]
    public IndexerConfiguration? Configuration { get; set; }
}

/// <summary>
/// Configuration for indexer execution.
/// </summary>
public class IndexerConfiguration
{
    /// <summary>
    /// Parsing mode for blobs (e.g., "default", "json", "jsonLines", "jsonArray", "delimitedText").
    /// </summary>
    [JsonPropertyName("parsingMode")]
    public string ParsingMode { get; set; } = "default";

    /// <summary>
    /// For delimited text: the delimiter character.
    /// </summary>
    [JsonPropertyName("delimitedTextDelimiter")]
    public string? DelimitedTextDelimiter { get; set; }

    /// <summary>
    /// For delimited text: headers.
    /// </summary>
    [JsonPropertyName("delimitedTextHeaders")]
    public string? DelimitedTextHeaders { get; set; }

    /// <summary>
    /// Whether to index storage metadata.
    /// </summary>
    [JsonPropertyName("indexStorageMetadataOnlyForOversizedDocuments")]
    public bool? IndexStorageMetadataOnlyForOversizedDocuments { get; set; }

    /// <summary>
    /// Document root for JSON parsing.
    /// </summary>
    [JsonPropertyName("documentRoot")]
    public string? DocumentRoot { get; set; }

    /// <summary>
    /// Data to extract from blobs.
    /// </summary>
    [JsonPropertyName("dataToExtract")]
    public string DataToExtract { get; set; } = "contentAndMetadata";

    /// <summary>
    /// Image action (none, generateNormalizedImages, etc.).
    /// </summary>
    [JsonPropertyName("imageAction")]
    public string ImageAction { get; set; } = "none";

    /// <summary>
    /// File extensions to index (comma-separated).
    /// </summary>
    [JsonPropertyName("indexedFileNameExtensions")]
    public string? IndexedFileNameExtensions { get; set; }

    /// <summary>
    /// File extensions to exclude (comma-separated).
    /// </summary>
    [JsonPropertyName("excludedFileNameExtensions")]
    public string? ExcludedFileNameExtensions { get; set; }

    /// <summary>
    /// Whether to fail on unsupported content type.
    /// </summary>
    [JsonPropertyName("failOnUnsupportedContentType")]
    public bool? FailOnUnsupportedContentType { get; set; }

    /// <summary>
    /// Whether to fail on unprocessable documents.
    /// </summary>
    [JsonPropertyName("failOnUnprocessableDocument")]
    public bool? FailOnUnprocessableDocument { get; set; }
}
