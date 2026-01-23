using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service for searching documents within a search index.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Search documents in an index.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="request">The search request.</param>
    /// <returns>The search response with matching documents.</returns>
    Task<SearchResponse> SearchAsync(string indexName, SearchRequest request);

    /// <summary>
    /// Get suggestions for search queries.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="request">The suggest request.</param>
    /// <returns>The suggest response.</returns>
    Task<SuggestResponse> SuggestAsync(string indexName, SuggestRequest request);

    /// <summary>
    /// Get autocomplete suggestions.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="request">The autocomplete request.</param>
    /// <returns>The autocomplete response.</returns>
    Task<AutocompleteResponse> AutocompleteAsync(string indexName, AutocompleteRequest request);
}
