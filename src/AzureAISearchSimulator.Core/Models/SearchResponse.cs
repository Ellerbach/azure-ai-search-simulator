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
    /// Debug information about the search execution.
    /// Only populated when debug is set in the request.
    /// Official: Contains debugging information that can be used to further explore your search results.
    /// </summary>
    [JsonPropertyName("@search.debug")]
    public DebugInfo? SearchDebug { get; set; }

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

    /// <summary>
    /// Per-document debug information. Only populated when debug mode includes "vector" or "all".
    /// Official: Contains debugging information that can be used to further explore your search results.
    /// </summary>
    [JsonIgnore]
    public DocumentDebugInfo? DocumentDebugInfo
    {
        get => TryGetValue("@search.documentDebugInfo", out var d) ? d as DocumentDebugInfo : null;
        set
        {
            if (value != null)
                this["@search.documentDebugInfo"] = value;
        }
    }

    /// <summary>
    /// Per-field BM25 scoring features. Only populated when featuresMode is "enabled".
    /// Contains per-field breakdown of uniqueTokenMatches, similarityScore, and termFrequency.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, FieldFeatures>? Features
    {
        get => TryGetValue("@search.features", out var f) ? f as Dictionary<string, FieldFeatures> : null;
        set
        {
            if (value != null)
                this["@search.features"] = value;
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

/// <summary>
/// Contains debugging information that can be used to further explore your search results.
/// Official Azure AI Search DebugInfo structure.
/// </summary>
public class DebugInfo
{
    /// <summary>
    /// Contains debugging information specific to query rewrites.
    /// </summary>
    [JsonPropertyName("queryRewrites")]
    public QueryRewritesDebugInfo? QueryRewrites { get; set; }

    // --- Simulator-specific additions ---

    /// <summary>
    /// [Simulator] The parsed Lucene query string.
    /// </summary>
    [JsonPropertyName("simulator.parsedQuery")]
    public string? ParsedQuery { get; set; }

    /// <summary>
    /// [Simulator] The parsed filter query string.
    /// </summary>
    [JsonPropertyName("simulator.parsedFilter")]
    public string? ParsedFilter { get; set; }

    /// <summary>
    /// [Simulator] Whether hybrid search was used.
    /// </summary>
    [JsonPropertyName("simulator.isHybridSearch")]
    public bool? IsHybridSearch { get; set; }

    /// <summary>
    /// [Simulator] Time taken to execute the text search in milliseconds.
    /// </summary>
    [JsonPropertyName("simulator.textSearchTimeMs")]
    public double? TextSearchTimeMs { get; set; }

    /// <summary>
    /// [Simulator] Time taken to execute the vector search in milliseconds.
    /// </summary>
    [JsonPropertyName("simulator.vectorSearchTimeMs")]
    public double? VectorSearchTimeMs { get; set; }

    /// <summary>
    /// [Simulator] Total search execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("simulator.totalTimeMs")]
    public double? TotalTimeMs { get; set; }

    /// <summary>
    /// [Simulator] Number of documents matched by text search.
    /// </summary>
    [JsonPropertyName("simulator.textMatchCount")]
    public int? TextMatchCount { get; set; }

    /// <summary>
    /// [Simulator] Number of documents matched by vector search.
    /// </summary>
    [JsonPropertyName("simulator.vectorMatchCount")]
    public int? VectorMatchCount { get; set; }

    /// <summary>
    /// [Simulator] Score fusion method used for hybrid search.
    /// </summary>
    [JsonPropertyName("simulator.scoreFusionMethod")]
    public string? ScoreFusionMethod { get; set; }

    /// <summary>
    /// [Simulator] Searchable fields used in the query.
    /// </summary>
    [JsonPropertyName("simulator.searchableFields")]
    public List<string>? SearchableFields { get; set; }
}

/// <summary>
/// Contains debugging information specific to query rewrites.
/// </summary>
public class QueryRewritesDebugInfo
{
    /// <summary>
    /// List of query rewrites generated for the text query.
    /// </summary>
    [JsonPropertyName("text")]
    public QueryRewritesValuesDebugInfo? Text { get; set; }

    /// <summary>
    /// List of query rewrites generated for the vectorizable text queries.
    /// </summary>
    [JsonPropertyName("vectors")]
    public List<QueryRewritesValuesDebugInfo>? Vectors { get; set; }
}

/// <summary>
/// Contains debugging information specific to query rewrites values.
/// </summary>
public class QueryRewritesValuesDebugInfo
{
    /// <summary>
    /// The input text to the generative query rewriting model.
    /// </summary>
    [JsonPropertyName("inputQuery")]
    public string? InputQuery { get; set; }

    /// <summary>
    /// List of query rewrites.
    /// </summary>
    [JsonPropertyName("rewrites")]
    public List<string>? Rewrites { get; set; }
}

/// <summary>
/// Per-document debugging information.
/// Official Azure AI Search DocumentDebugInfo structure.
/// </summary>
public class DocumentDebugInfo
{
    /// <summary>
    /// Contains debugging information specific to semantic ranking requests.
    /// </summary>
    [JsonPropertyName("semantic")]
    public SemanticDebugInfo? Semantic { get; set; }

    /// <summary>
    /// Contains debugging information specific to vector and hybrid search.
    /// </summary>
    [JsonPropertyName("vectors")]
    public VectorsDebugInfo? Vectors { get; set; }

    /// <summary>
    /// Contains debugging information specific to vectors matched within a collection of complex types.
    /// </summary>
    [JsonPropertyName("innerHits")]
    public Dictionary<string, object>? InnerHits { get; set; }
}

/// <summary>
/// Contains debugging information specific to semantic ranking.
/// </summary>
public class SemanticDebugInfo
{
    /// <summary>
    /// The content fields that were sent to the semantic enrichment process.
    /// </summary>
    [JsonPropertyName("contentFields")]
    public List<QueryResultDocumentSemanticField>? ContentFields { get; set; }

    /// <summary>
    /// The keyword fields that were sent to the semantic enrichment process.
    /// </summary>
    [JsonPropertyName("keywordFields")]
    public List<QueryResultDocumentSemanticField>? KeywordFields { get; set; }

    /// <summary>
    /// The title field that was sent to the semantic enrichment process.
    /// </summary>
    [JsonPropertyName("titleField")]
    public QueryResultDocumentSemanticField? TitleField { get; set; }

    /// <summary>
    /// The raw concatenated strings that were sent to the semantic enrichment process.
    /// </summary>
    [JsonPropertyName("rerankerInput")]
    public QueryResultDocumentRerankerInput? RerankerInput { get; set; }
}

/// <summary>
/// Description of fields that were sent to the semantic enrichment process.
/// </summary>
public class QueryResultDocumentSemanticField
{
    /// <summary>
    /// The name of the field that was sent to the semantic enrichment process.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The way the field was used for the semantic enrichment process (fully used, partially used, or unused).
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }
}

/// <summary>
/// The raw concatenated strings that were sent to the semantic enrichment process.
/// </summary>
public class QueryResultDocumentRerankerInput
{
    /// <summary>
    /// The raw string for the title field.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// The raw concatenated strings for the content fields.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// The raw concatenated strings for the keyword fields.
    /// </summary>
    [JsonPropertyName("keywords")]
    public string? Keywords { get; set; }
}

/// <summary>
/// Contains debugging information specific to vector and hybrid search.
/// </summary>
public class VectorsDebugInfo
{
    /// <summary>
    /// The breakdown of subscores of the document prior to the chosen result set fusion/combination method such as RRF.
    /// </summary>
    [JsonPropertyName("subscores")]
    public QueryResultDocumentSubscores? Subscores { get; set; }
}

/// <summary>
/// The breakdown of subscores between the text and vector query components.
/// Each vector query is shown as a separate object in the same order they were received.
/// </summary>
public class QueryResultDocumentSubscores
{
    /// <summary>
    /// The BM25 or Classic score for the text portion of the query.
    /// </summary>
    [JsonPropertyName("text")]
    public TextResult? Text { get; set; }

    /// <summary>
    /// The document boost value (from scoring profiles).
    /// </summary>
    [JsonPropertyName("documentBoost")]
    public double? DocumentBoost { get; set; }

    /// <summary>
    /// The vector similarity and @search.score values for each vector query.
    /// Keys are vector field names.
    /// </summary>
    [JsonPropertyName("vectors")]
    public Dictionary<string, SingleVectorFieldResult>? Vectors { get; set; }
}

/// <summary>
/// The BM25 or Classic score for the text portion of the query.
/// </summary>
public class TextResult
{
    /// <summary>
    /// The BM25 or Classic score for the text portion of the query.
    /// </summary>
    [JsonPropertyName("searchScore")]
    public double SearchScore { get; set; }
}

/// <summary>
/// A single vector field result with both @search.score and vector similarity values.
/// Vector similarity is related to @search.score by an equation.
/// </summary>
public class SingleVectorFieldResult
{
    /// <summary>
    /// The @search.score value that is calculated from the vector similarity score.
    /// This is the score that's visible in a pure single-field single-vector query.
    /// </summary>
    [JsonPropertyName("searchScore")]
    public double SearchScore { get; set; }

    /// <summary>
    /// The vector similarity score for this document.
    /// Note this is the canonical definition of similarity metric, not the 'distance' version.
    /// For example, cosine similarity instead of cosine distance.
    /// </summary>
    [JsonPropertyName("vectorSimilarity")]
    public double VectorSimilarity { get; set; }
}

/// <summary>
/// Per-field BM25 scoring features returned when featuresMode is "enabled".
/// Contains detailed scoring breakdown for a single searchable field.
/// </summary>
public class FieldFeatures
{
    /// <summary>
    /// Number of unique tokens found in the field that match query terms.
    /// </summary>
    [JsonPropertyName("uniqueTokenMatches")]
    public double UniqueTokenMatches { get; set; }

    /// <summary>
    /// Similarity score: a measure of how similar the content of the field is
    /// relative to the query terms.
    /// </summary>
    [JsonPropertyName("similarityScore")]
    public double SimilarityScore { get; set; }

    /// <summary>
    /// Term frequency: the number of times the query terms were found in the field.
    /// </summary>
    [JsonPropertyName("termFrequency")]
    public double TermFrequency { get; set; }
}
