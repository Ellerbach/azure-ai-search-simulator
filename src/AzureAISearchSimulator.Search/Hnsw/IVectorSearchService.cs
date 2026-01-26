namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Result of a vector search operation.
/// </summary>
public class VectorSearchResult
{
    /// <summary>
    /// Document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Distance from the query vector (lower is closer).
    /// </summary>
    public float Distance { get; init; }

    /// <summary>
    /// Similarity score (higher is more similar), computed from distance.
    /// </summary>
    public double Score { get; init; }
}

/// <summary>
/// Service for performing vector similarity searches.
/// Abstracts the underlying vector search implementation (HNSW or brute-force).
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Initializes or opens a vector index for the specified search index and field.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="dimensions">Number of dimensions for the vectors.</param>
    void InitializeIndex(string indexName, string fieldName, int dimensions);

    /// <summary>
    /// Checks if a vector index exists for the specified search index and field.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <returns>True if the index exists.</returns>
    bool IndexExists(string indexName, string fieldName);

    /// <summary>
    /// Adds or updates a vector for a document.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <param name="vector">The vector to store.</param>
    void AddVector(string indexName, string fieldName, string documentId, float[] vector);

    /// <summary>
    /// Removes a vector for a document.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentId">Document identifier.</param>
    void RemoveVector(string indexName, string fieldName, string documentId);

    /// <summary>
    /// Removes all vectors for a document across all fields.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="documentId">Document identifier.</param>
    void RemoveDocument(string indexName, string documentId);

    /// <summary>
    /// Performs a K-nearest neighbors search.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <returns>List of search results ordered by similarity (descending).</returns>
    IReadOnlyList<VectorSearchResult> Search(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k);

    /// <summary>
    /// Performs a filtered vector search using post-filtering with oversampling.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Final number of results to return.</param>
    /// <param name="candidateDocumentIds">Set of document IDs that pass the filter.</param>
    /// <returns>List of search results ordered by similarity (descending).</returns>
    IReadOnlyList<VectorSearchResult> SearchWithFilter(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        ISet<string> candidateDocumentIds);

    /// <summary>
    /// Gets the number of vectors stored for an index/field.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <returns>Number of vectors.</returns>
    int GetVectorCount(string indexName, string fieldName);

    /// <summary>
    /// Deletes a vector index.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field (optional, if null deletes all fields).</param>
    void DeleteIndex(string indexName, string? fieldName = null);

    /// <summary>
    /// Persists all vector indexes to disk.
    /// </summary>
    void SaveAll();
}
