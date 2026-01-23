using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service for managing documents within a search index.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Index, merge, or delete documents in a search index.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="request">The indexing request containing document actions.</param>
    /// <returns>The indexing response with results for each document.</returns>
    Task<IndexDocumentsResponse> IndexDocumentsAsync(string indexName, IndexDocumentsRequest request);

    /// <summary>
    /// Get a document by its key.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="key">The document key.</param>
    /// <param name="selectedFields">Fields to return (null for all).</param>
    /// <returns>The document or null if not found.</returns>
    Task<Dictionary<string, object?>?> GetDocumentAsync(string indexName, string key, IEnumerable<string>? selectedFields = null);

    /// <summary>
    /// Get document count in an index.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <returns>The document count.</returns>
    Task<long> GetDocumentCountAsync(string indexName);

    /// <summary>
    /// Delete all documents from an index.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    Task ClearIndexAsync(string indexName);
}
