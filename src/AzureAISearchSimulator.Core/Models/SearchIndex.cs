using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a search index definition compatible with Azure AI Search.
/// </summary>
public class SearchIndex
{
    /// <summary>
    /// OData context URL for the entity.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    /// <summary>
    /// Name of the index.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the index. Helpful for prompt engineering in RAG workloads.
    /// Added in API version 2025-09-01.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Fields defined in the index.
    /// </summary>
    [JsonPropertyName("fields")]
    public List<SearchField> Fields { get; set; } = new();

    /// <summary>
    /// Scoring profiles for relevance tuning.
    /// </summary>
    [JsonPropertyName("scoringProfiles")]
    public List<ScoringProfile>? ScoringProfiles { get; set; }

    /// <summary>
    /// Default scoring profile name.
    /// </summary>
    [JsonPropertyName("defaultScoringProfile")]
    public string? DefaultScoringProfile { get; set; }

    /// <summary>
    /// Suggesters for autocomplete and suggestions.
    /// </summary>
    [JsonPropertyName("suggesters")]
    public List<Suggester>? Suggesters { get; set; }

    /// <summary>
    /// Analyzers defined for the index.
    /// </summary>
    [JsonPropertyName("analyzers")]
    public List<CustomAnalyzer>? Analyzers { get; set; }

    /// <summary>
    /// Tokenizers defined for the index.
    /// </summary>
    [JsonPropertyName("tokenizers")]
    public List<CustomTokenizer>? Tokenizers { get; set; }

    /// <summary>
    /// Token filters defined for the index.
    /// </summary>
    [JsonPropertyName("tokenFilters")]
    public List<CustomTokenFilter>? TokenFilters { get; set; }

    /// <summary>
    /// Character filters defined for the index.
    /// </summary>
    [JsonPropertyName("charFilters")]
    public List<CustomCharFilter>? CharFilters { get; set; }

    /// <summary>
    /// Normalizers defined for the index. Used for case-insensitive filtering, faceting, and sorting.
    /// Added in API version 2025-09-01.
    /// </summary>
    [JsonPropertyName("normalizers")]
    public List<CustomNormalizer>? Normalizers { get; set; }

    /// <summary>
    /// Vector search configuration.
    /// </summary>
    [JsonPropertyName("vectorSearch")]
    public VectorSearchConfiguration? VectorSearch { get; set; }

    /// <summary>
    /// CORS options for the index.
    /// </summary>
    [JsonPropertyName("corsOptions")]
    public CorsOptions? CorsOptions { get; set; }

    /// <summary>
    /// Encryption key for customer-managed encryption.
    /// </summary>
    [JsonPropertyName("encryptionKey")]
    public object? EncryptionKey { get; set; }

    /// <summary>
    /// Similarity algorithm configuration (e.g., BM25).
    /// </summary>
    [JsonPropertyName("similarity")]
    public SimilarityAlgorithm? Similarity { get; set; }

    /// <summary>
    /// Semantic search configuration.
    /// </summary>
    [JsonPropertyName("semantic")]
    public object? Semantic { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ETag { get; set; }

    /// <summary>
    /// Timestamp when the index was created.
    /// </summary>
    [JsonIgnore]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the index was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Scoring profile for relevance tuning.
/// </summary>
public class ScoringProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public TextWeights? Text { get; set; }

    [JsonPropertyName("functions")]
    public List<ScoringFunction>? Functions { get; set; }

    [JsonPropertyName("functionAggregation")]
    public string? FunctionAggregation { get; set; }
}

public class TextWeights
{
    [JsonPropertyName("weights")]
    public Dictionary<string, double>? Weights { get; set; }
}

public class ScoringFunction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("boost")]
    public double Boost { get; set; }

    [JsonPropertyName("interpolation")]
    public string? Interpolation { get; set; }

    [JsonPropertyName("freshness")]
    public FreshnessFunction? Freshness { get; set; }

    [JsonPropertyName("magnitude")]
    public MagnitudeFunction? Magnitude { get; set; }

    [JsonPropertyName("distance")]
    public DistanceFunction? Distance { get; set; }

    [JsonPropertyName("tag")]
    public TagFunction? Tag { get; set; }
}

public class FreshnessFunction
{
    [JsonPropertyName("boostingDuration")]
    public string BoostingDuration { get; set; } = string.Empty;
}

public class MagnitudeFunction
{
    [JsonPropertyName("boostingRangeStart")]
    public double BoostingRangeStart { get; set; }

    [JsonPropertyName("boostingRangeEnd")]
    public double BoostingRangeEnd { get; set; }

    [JsonPropertyName("constantBoostBeyondRange")]
    public bool? ConstantBoostBeyondRange { get; set; }
}

public class DistanceFunction
{
    [JsonPropertyName("referencePointParameter")]
    public string ReferencePointParameter { get; set; } = string.Empty;

    [JsonPropertyName("boostingDistance")]
    public double BoostingDistance { get; set; }
}

public class TagFunction
{
    [JsonPropertyName("tagsParameter")]
    public string TagsParameter { get; set; } = string.Empty;
}

/// <summary>
/// Suggester configuration for autocomplete and suggestions.
/// </summary>
public class Suggester
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("searchMode")]
    public string SearchMode { get; set; } = "analyzingInfixMatching";

    [JsonPropertyName("sourceFields")]
    public List<string> SourceFields { get; set; } = new();
}

/// <summary>
/// Custom analyzer definition.
/// </summary>
public class CustomAnalyzer
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.CustomAnalyzer";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tokenizer")]
    public string Tokenizer { get; set; } = string.Empty;

    [JsonPropertyName("tokenFilters")]
    public List<string>? TokenFilters { get; set; }

    [JsonPropertyName("charFilters")]
    public List<string>? CharFilters { get; set; }
}

/// <summary>
/// Custom tokenizer definition.
/// </summary>
public class CustomTokenizer
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Custom token filter definition.
/// </summary>
public class CustomTokenFilter
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Custom character filter definition.
/// Supports mapping and pattern_replace types.
/// </summary>
public class CustomCharFilter
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mappings for mapping char filter. Format: "a=>b" or "abc=>xyz".
    /// Used when @odata.type is "#Microsoft.Azure.Search.MappingCharFilter".
    /// </summary>
    [JsonPropertyName("mappings")]
    public List<string>? Mappings { get; set; }

    /// <summary>
    /// Pattern to match for pattern_replace char filter (regex).
    /// Used when @odata.type is "#Microsoft.Azure.Search.PatternReplaceCharFilter".
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    /// Replacement string for pattern_replace char filter.
    /// Used when @odata.type is "#Microsoft.Azure.Search.PatternReplaceCharFilter".
    /// </summary>
    [JsonPropertyName("replacement")]
    public string? Replacement { get; set; }
}

/// <summary>
/// Custom normalizer definition for case-insensitive filtering, faceting, and sorting.
/// Added in API version 2025-09-01.
/// </summary>
public class CustomNormalizer
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.CustomNormalizer";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Token filters to apply (e.g., "lowercase", "asciifolding").
    /// </summary>
    [JsonPropertyName("tokenFilters")]
    public List<string>? TokenFilters { get; set; }

    /// <summary>
    /// Character filters to apply.
    /// </summary>
    [JsonPropertyName("charFilters")]
    public List<string>? CharFilters { get; set; }
}

/// <summary>
/// Built-in normalizer names supported by Azure AI Search.
/// </summary>
public static class NormalizerName
{
    /// <summary>
    /// Converts text to lowercase.
    /// </summary>
    public const string Lowercase = "lowercase";

    /// <summary>
    /// Converts text to uppercase.
    /// </summary>
    public const string Uppercase = "uppercase";

    /// <summary>
    /// Standard normalizer that lowercases and removes accents.
    /// </summary>
    public const string Standard = "standard";

    /// <summary>
    /// Removes diacritics and converts to ASCII equivalents.
    /// </summary>
    public const string AsciiFolding = "asciifolding";

    /// <summary>
    /// Removes elisions (articles like l', d', etc.) from text.
    /// Commonly used for French and other Romance languages.
    /// </summary>
    public const string Elision = "elision";

    /// <summary>
    /// Checks if the normalizer name is a built-in normalizer.
    /// </summary>
    public static bool IsBuiltIn(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "lowercase" or "uppercase" or "standard" or "asciifolding" or "elision" => true,
            _ => false
        };
    }
}

/// <summary>
/// Vector search configuration for the index.
/// </summary>
public class VectorSearchConfiguration
{
    [JsonPropertyName("algorithms")]
    public List<VectorSearchAlgorithm>? Algorithms { get; set; }

    [JsonPropertyName("profiles")]
    public List<VectorSearchProfile>? Profiles { get; set; }
}

public class VectorSearchAlgorithm
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "hnsw";

    [JsonPropertyName("hnswParameters")]
    public HnswParameters? HnswParameters { get; set; }
}

public class HnswParameters
{
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "cosine";

    [JsonPropertyName("m")]
    public int? M { get; set; }

    [JsonPropertyName("efConstruction")]
    public int? EfConstruction { get; set; }

    [JsonPropertyName("efSearch")]
    public int? EfSearch { get; set; }
}

public class VectorSearchProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;
}

/// <summary>
/// CORS options for the index.
/// </summary>
public class CorsOptions
{
    [JsonPropertyName("allowedOrigins")]
    public List<string> AllowedOrigins { get; set; } = new();

    [JsonPropertyName("maxAgeInSeconds")]
    public int? MaxAgeInSeconds { get; set; }
}

/// <summary>
/// Similarity algorithm configuration.
/// Azure AI Search defaults to BM25.
/// </summary>
public class SimilarityAlgorithm
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.BM25Similarity";

    [JsonPropertyName("k1")]
    public double? K1 { get; set; }

    [JsonPropertyName("b")]
    public double? B { get; set; }
}
