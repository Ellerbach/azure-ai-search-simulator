namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Manages HNSW (Hierarchical Navigable Small World) indexes for vector search.
/// Provides fast approximate nearest neighbor search capabilities.
/// </summary>
public interface IHnswIndexManager : IDisposable
{
    /// <summary>
    /// Creates or opens an HNSW index for the specified search index and vector field.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="dimensions">Number of dimensions for the vectors.</param>
    /// <param name="maxElements">Maximum number of elements the index can hold.</param>
    void CreateOrOpenIndex(string indexName, string fieldName, int dimensions, int maxElements);

    /// <summary>
    /// Checks if an HNSW index exists for the specified search index and field.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <returns>True if the index exists.</returns>
    bool IndexExists(string indexName, string fieldName);

    /// <summary>
    /// Adds or updates a vector in the HNSW index.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentId">Unique document identifier.</param>
    /// <param name="vector">The vector to add.</param>
    void AddVector(string indexName, string fieldName, string documentId, float[] vector);

    /// <summary>
    /// Adds or updates multiple vectors in the HNSW index (batch operation).
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documents">Collection of (documentId, vector) tuples.</param>
    void AddVectors(string indexName, string fieldName, IEnumerable<(string DocumentId, float[] Vector)> documents);

    /// <summary>
    /// Removes a vector from the HNSW index.
    /// Note: HNSW does not support true deletion, this marks the vector as deleted.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentId">Document identifier to remove.</param>
    void RemoveVector(string indexName, string fieldName, string documentId);

    /// <summary>
    /// Searches for the k nearest neighbors of the query vector.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <returns>List of (documentId, distance) tuples ordered by distance (ascending).</returns>
    IReadOnlyList<(string DocumentId, float Distance)> Search(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k);

    /// <summary>
    /// Searches for the k nearest neighbors with oversampling for filtered queries.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Final number of results needed.</param>
    /// <param name="oversampleMultiplier">Multiplier for oversampling (e.g., 5 means retrieve k*5 candidates).</param>
    /// <returns>List of (documentId, distance) tuples ordered by distance (ascending).</returns>
    IReadOnlyList<(string DocumentId, float Distance)> SearchWithOversampling(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        int oversampleMultiplier);

    /// <summary>
    /// Gets the document ID for an HNSW internal label.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="label">HNSW internal label.</param>
    /// <returns>The document ID, or null if not found.</returns>
    string? GetDocumentId(string indexName, string fieldName, long label);

    /// <summary>
    /// Gets the HNSW internal label for a document ID.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentId">Document identifier.</param>
    /// <returns>The internal label, or null if not found.</returns>
    long? GetLabel(string indexName, string fieldName, string documentId);

    /// <summary>
    /// Gets the number of vectors in the HNSW index.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <returns>Number of vectors in the index.</returns>
    int GetVectorCount(string indexName, string fieldName);

    /// <summary>
    /// Persists the HNSW index to disk.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    void SaveIndex(string indexName, string fieldName);

    /// <summary>
    /// Persists all HNSW indexes to disk.
    /// </summary>
    void SaveAllIndexes();

    /// <summary>
    /// Deletes an HNSW index completely.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field (optional, if null deletes all fields).</param>
    void DeleteIndex(string indexName, string? fieldName = null);

    /// <summary>
    /// Rebuilds an HNSW index from scratch with the provided vectors.
    /// Useful for recovering from corruption or optimizing the index.
    /// </summary>
    /// <param name="indexName">Name of the search index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documents">Collection of (documentId, vector) tuples.</param>
    void RebuildIndex(string indexName, string fieldName, IEnumerable<(string DocumentId, float[] Vector)> documents);
}
