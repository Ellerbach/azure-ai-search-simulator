using System.Text.Json.Serialization;
using LiteDB;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a synonym map resource in Azure AI Search.
/// A synonym map contains synonym rules in Apache Solr format.
/// </summary>
public class SynonymMap
{
    /// <summary>
    /// Internal ID for LiteDB storage (not serialized to JSON).
    /// </summary>
    [BsonId]
    [JsonIgnore]
    public int InternalId { get; set; }

    /// <summary>
    /// The name of the synonym map.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The format of the synonym map. Currently only "solr" is supported.
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "solr";

    /// <summary>
    /// Synonym rules in Solr synonym format.
    /// Each line contains a rule. Supported formats:
    /// - Equivalent synonyms: "word1, word2, word3" (bidirectional)
    /// - Explicit mapping: "word1, word2 => word3, word4" (unidirectional)
    /// </summary>
    [JsonPropertyName("synonyms")]
    public string Synonyms { get; set; } = string.Empty;

    /// <summary>
    /// Optional encryption key for customer-managed encryption.
    /// </summary>
    [JsonPropertyName("encryptionKey")]
    public EncryptionKey? EncryptionKey { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ETag { get; set; }
}
