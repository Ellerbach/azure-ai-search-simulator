using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Search request compatible with Azure AI Search.
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// Search text. Use "*" to match all documents.
    /// </summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>
    /// Search mode: "any" (OR) or "all" (AND).
    /// </summary>
    [JsonPropertyName("searchMode")]
    public string SearchMode { get; set; } = "any";

    /// <summary>
    /// Query type: "simple" or "full" (Lucene syntax).
    /// </summary>
    [JsonPropertyName("queryType")]
    public string QueryType { get; set; } = "simple";

    /// <summary>
    /// Comma-separated list of fields to search.
    /// </summary>
    [JsonPropertyName("searchFields")]
    public string? SearchFields { get; set; }

    /// <summary>
    /// Comma-separated list of fields to return.
    /// </summary>
    [JsonPropertyName("select")]
    public string? Select { get; set; }

    /// <summary>
    /// OData filter expression.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// Comma-separated list of orderby expressions.
    /// </summary>
    [JsonPropertyName("orderby")]
    public string? OrderBy { get; set; }

    /// <summary>
    /// Number of results to return.
    /// </summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }

    /// <summary>
    /// Number of results to skip.
    /// </summary>
    [JsonPropertyName("skip")]
    public int? Skip { get; set; }

    /// <summary>
    /// Whether to include total count.
    /// </summary>
    [JsonPropertyName("count")]
    public bool? Count { get; set; }

    /// <summary>
    /// Facet specifications.
    /// </summary>
    [JsonPropertyName("facets")]
    public List<string>? Facets { get; set; }

    /// <summary>
    /// Comma-separated list of fields to highlight.
    /// </summary>
    [JsonPropertyName("highlight")]
    public string? Highlight { get; set; }

    /// <summary>
    /// HTML tag to insert before highlighted terms.
    /// </summary>
    [JsonPropertyName("highlightPreTag")]
    public string HighlightPreTag { get; set; } = "<em>";

    /// <summary>
    /// HTML tag to insert after highlighted terms.
    /// </summary>
    [JsonPropertyName("highlightPostTag")]
    public string HighlightPostTag { get; set; } = "</em>";

    /// <summary>
    /// Scoring profile to use.
    /// </summary>
    [JsonPropertyName("scoringProfile")]
    public string? ScoringProfile { get; set; }

    /// <summary>
    /// Scoring parameters.
    /// </summary>
    [JsonPropertyName("scoringParameters")]
    public List<string>? ScoringParameters { get; set; }

    /// <summary>
    /// Minimum coverage required (0-100).
    /// </summary>
    [JsonPropertyName("minimumCoverage")]
    public double? MinimumCoverage { get; set; }

    /// <summary>
    /// Vector queries for vector/hybrid search.
    /// </summary>
    [JsonPropertyName("vectorQueries")]
    public List<VectorQuery>? VectorQueries { get; set; }
}

/// <summary>
/// Vector query for vector search.
/// </summary>
public class VectorQuery
{
    /// <summary>
    /// Kind of vector query: "vector" for raw vectors.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "vector";

    /// <summary>
    /// The vector embedding to search with.
    /// </summary>
    [JsonPropertyName("vector")]
    public float[]? Vector { get; set; }

    /// <summary>
    /// Comma-separated list of vector fields to search.
    /// </summary>
    [JsonPropertyName("fields")]
    public string? Fields { get; set; }

    /// <summary>
    /// Number of nearest neighbors to return.
    /// </summary>
    [JsonPropertyName("k")]
    public int K { get; set; } = 10;

    /// <summary>
    /// Weight for this vector query in hybrid search.
    /// </summary>
    [JsonPropertyName("weight")]
    public float? Weight { get; set; }
}
