using System.Collections.Concurrent;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// In-memory storage for vector embeddings with cosine similarity search.
/// </summary>
public class VectorStore
{
    private readonly ConcurrentDictionary<string, IndexVectorStore> _indexStores = new();

    /// <summary>
    /// Adds or updates a vector for a document.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="documentKey">Document key.</param>
    /// <param name="vector">The vector embeddings.</param>
    public void AddVector(string indexName, string fieldName, string documentKey, float[] vector)
    {
        var store = _indexStores.GetOrAdd(indexName, _ => new IndexVectorStore());
        store.AddVector(fieldName, documentKey, vector);
    }

    /// <summary>
    /// Gets a vector for a document.
    /// </summary>
    public float[]? GetVector(string indexName, string fieldName, string documentKey)
    {
        if (_indexStores.TryGetValue(indexName, out var store))
        {
            return store.GetVector(fieldName, documentKey);
        }
        return null;
    }

    /// <summary>
    /// Removes a vector for a document.
    /// </summary>
    public void RemoveVector(string indexName, string fieldName, string documentKey)
    {
        if (_indexStores.TryGetValue(indexName, out var store))
        {
            store.RemoveVector(fieldName, documentKey);
        }
    }

    /// <summary>
    /// Removes all vectors for a document.
    /// </summary>
    public void RemoveDocument(string indexName, string documentKey)
    {
        if (_indexStores.TryGetValue(indexName, out var store))
        {
            store.RemoveDocument(documentKey);
        }
    }

    /// <summary>
    /// Searches for the nearest vectors using cosine similarity.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="fieldName">Name of the vector field.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="k">Number of nearest neighbors to return.</param>
    /// <param name="candidateDocuments">Optional set of document keys to search within.</param>
    /// <returns>Ordered list of (documentKey, similarity) tuples.</returns>
    public List<(string DocumentKey, double Similarity)> Search(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        ISet<string>? candidateDocuments = null)
    {
        if (!_indexStores.TryGetValue(indexName, out var store))
        {
            return new List<(string, double)>();
        }

        return store.Search(fieldName, queryVector, k, candidateDocuments);
    }

    /// <summary>
    /// Clears all vectors for an index.
    /// </summary>
    public void ClearIndex(string indexName)
    {
        _indexStores.TryRemove(indexName, out _);
    }

    /// <summary>
    /// Gets the count of vectors stored for an index/field.
    /// </summary>
    public int GetVectorCount(string indexName, string fieldName)
    {
        if (_indexStores.TryGetValue(indexName, out var store))
        {
            return store.GetVectorCount(fieldName);
        }
        return 0;
    }

    /// <summary>
    /// Vector storage for a single index.
    /// </summary>
    private class IndexVectorStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, float[]>> _fieldVectors = new();

        public void AddVector(string fieldName, string documentKey, float[] vector)
        {
            var fieldStore = _fieldVectors.GetOrAdd(fieldName, _ => new ConcurrentDictionary<string, float[]>());
            fieldStore[documentKey] = vector;
        }

        public float[]? GetVector(string fieldName, string documentKey)
        {
            if (_fieldVectors.TryGetValue(fieldName, out var fieldStore) &&
                fieldStore.TryGetValue(documentKey, out var vector))
            {
                return vector;
            }
            return null;
        }

        public void RemoveVector(string fieldName, string documentKey)
        {
            if (_fieldVectors.TryGetValue(fieldName, out var fieldStore))
            {
                fieldStore.TryRemove(documentKey, out _);
            }
        }

        public void RemoveDocument(string documentKey)
        {
            foreach (var fieldStore in _fieldVectors.Values)
            {
                fieldStore.TryRemove(documentKey, out _);
            }
        }

        public int GetVectorCount(string fieldName)
        {
            if (_fieldVectors.TryGetValue(fieldName, out var fieldStore))
            {
                return fieldStore.Count;
            }
            return 0;
        }

        public List<(string DocumentKey, double Similarity)> Search(
            string fieldName,
            float[] queryVector,
            int k,
            ISet<string>? candidateDocuments)
        {
            if (!_fieldVectors.TryGetValue(fieldName, out var fieldStore))
            {
                return new List<(string, double)>();
            }

            var results = new List<(string DocumentKey, double Similarity)>();

            foreach (var kvp in fieldStore)
            {
                if (candidateDocuments != null && !candidateDocuments.Contains(kvp.Key))
                {
                    continue;
                }

                var similarity = CosineSimilarity(queryVector, kvp.Value);
                results.Add((kvp.Key, similarity));
            }

            return results
                .OrderByDescending(r => r.Similarity)
                .Take(k)
                .ToList();
        }

        /// <summary>
        /// Computes cosine similarity between two vectors.
        /// </summary>
        private static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
            {
                throw new ArgumentException($"Vector dimensions do not match: {a.Length} vs {b.Length}");
            }

            double dotProduct = 0;
            double normA = 0;
            double normB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            normA = Math.Sqrt(normA);
            normB = Math.Sqrt(normB);

            if (normA == 0 || normB == 0)
            {
                return 0;
            }

            return dotProduct / (normA * normB);
        }
    }
}
