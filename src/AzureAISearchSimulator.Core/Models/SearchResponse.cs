using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Search response compatible with Azure AI Search.
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    /// <summary>
    /// Total count of matching documents.
    /// </summary>
    [JsonPropertyName("@odata.count")]
    public long? ODataCount { get; set; }

    /// <summary>
    /// Search coverage percentage.
    /// </summary>
    [JsonPropertyName("@search.coverage")]
    public double? SearchCoverage { get; set; }

    /// <summary>
    /// Facet results.
    /// </summary>
    [JsonPropertyName("@search.facets")]
    public Dictionary<string, List<FacetResult>>? SearchFacets { get; set; }

    /// <summary>
    /// The search results.
    /// </summary>
    [JsonPropertyName("value")]
    public List<SearchResult> Value { get; set; } = new();

    /// <summary>
    /// Continuation token for paging.
    /// </summary>
    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }
}

/// <summary>
/// A single search result.
/// </summary>
public class SearchResult : Dictionary<string, object?>
{
    /// <summary>
    /// Search relevance score.
    /// </summary>
    [JsonIgnore]
    public double? Score
    {
        get => TryGetValue("@search.score", out var score) ? Convert.ToDouble(score) : null;
        set => this["@search.score"] = value;
    }

    /// <summary>
    /// Highlighted field values.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, List<string>>? Highlights
    {
        get => TryGetValue("@search.highlights", out var h) ? h as Dictionary<string, List<string>> : null;
        set
        {
            if (value != null)
                this["@search.highlights"] = value;
        }
    }
}

/// <summary>
/// Facet result for a single value or range.
/// </summary>
public class FacetResult
{
    /// <summary>
    /// The facet value (for value facets).
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// Count of documents with this value.
    /// </summary>
    [JsonPropertyName("count")]
    public long Count { get; set; }

    /// <summary>
    /// Range start (for range facets).
    /// </summary>
    [JsonPropertyName("from")]
    public object? From { get; set; }

    /// <summary>
    /// Range end (for range facets).
    /// </summary>
    [JsonPropertyName("to")]
    public object? To { get; set; }
}

/// <summary>
/// Suggestion request.
/// </summary>
public class SuggestRequest
{
    /// <summary>
    /// Search text.
    /// </summary>
    [JsonPropertyName("search")]
    public string Search { get; set; } = string.Empty;

    /// <summary>
    /// Name of the suggester to use.
    /// </summary>
    [JsonPropertyName("suggesterName")]
    public string SuggesterName { get; set; } = string.Empty;

    /// <summary>
    /// OData filter expression.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// Comma-separated list of fields to return.
    /// </summary>
    [JsonPropertyName("select")]
    public string? Select { get; set; }

    /// <summary>
    /// Comma-separated list of orderby expressions.
    /// </summary>
    [JsonPropertyName("orderby")]
    public string? OrderBy { get; set; }

    /// <summary>
    /// Number of suggestions to return.
    /// </summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }

    /// <summary>
    /// Enable fuzzy matching.
    /// </summary>
    [JsonPropertyName("fuzzy")]
    public bool? Fuzzy { get; set; }

    /// <summary>
    /// Comma-separated list of fields to highlight.
    /// </summary>
    [JsonPropertyName("highlightPreTag")]
    public string? HighlightPreTag { get; set; }

    /// <summary>
    /// HTML tag to insert after highlighted terms.
    /// </summary>
    [JsonPropertyName("highlightPostTag")]
    public string? HighlightPostTag { get; set; }
}

/// <summary>
/// Suggestion response.
/// </summary>
public class SuggestResponse
{
    /// <summary>
    /// OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    /// <summary>
    /// The suggestion results.
    /// </summary>
    [JsonPropertyName("value")]
    public List<SuggestResult> Value { get; set; } = new();
}

/// <summary>
/// A single suggestion result.
/// </summary>
public class SuggestResult : Dictionary<string, object?>
{
    /// <summary>
    /// The suggested text.
    /// </summary>
    [JsonIgnore]
    public string? Text
    {
        get => TryGetValue("@search.text", out var text) ? text?.ToString() : null;
        set => this["@search.text"] = value;
    }
}

/// <summary>
/// Autocomplete request.
/// </summary>
public class AutocompleteRequest
{
    /// <summary>
    /// Search text.
    /// </summary>
    [JsonPropertyName("search")]
    public string Search { get; set; } = string.Empty;

    /// <summary>
    /// Name of the suggester to use.
    /// </summary>
    [JsonPropertyName("suggesterName")]
    public string SuggesterName { get; set; } = string.Empty;

    /// <summary>
    /// Autocomplete mode: "oneTerm", "twoTerms", "oneTermWithContext".
    /// </summary>
    [JsonPropertyName("autocompleteMode")]
    public string AutocompleteMode { get; set; } = "oneTerm";

    /// <summary>
    /// OData filter expression.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// Enable fuzzy matching.
    /// </summary>
    [JsonPropertyName("fuzzy")]
    public bool? Fuzzy { get; set; }

    /// <summary>
    /// Number of completions to return.
    /// </summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }
}

/// <summary>
/// Autocomplete response.
/// </summary>
public class AutocompleteResponse
{
    /// <summary>
    /// OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    /// <summary>
    /// The autocomplete results.
    /// </summary>
    [JsonPropertyName("value")]
    public List<AutocompleteItem> Value { get; set; } = new();
}

/// <summary>
/// A single autocomplete result.
/// </summary>
public class AutocompleteItem
{
    /// <summary>
    /// The completed text.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The query text to use.
    /// </summary>
    [JsonPropertyName("queryPlusText")]
    public string QueryPlusText { get; set; } = string.Empty;
}
