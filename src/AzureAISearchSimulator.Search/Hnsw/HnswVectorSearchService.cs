using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Vector search service implementation using HNSW algorithm for fast approximate nearest neighbor search.
/// Falls back to brute-force cosine similarity when HNSW is disabled.
/// </summary>
public class HnswVectorSearchService : IVectorSearchService, IDisposable
{
    private readonly ILogger<HnswVectorSearchService> _logger;
    private readonly VectorSearchSettings _settings;
    private readonly IHnswIndexManager _hnswManager;
    private readonly VectorStore _bruteForceStore;
    private readonly HashSet<string> _knownVectorFields = new();
    private readonly object _lock = new();
    private bool _disposed;

    public HnswVectorSearchService(
        ILogger<HnswVectorSearchService> logger,
        IOptions<VectorSearchSettings> settings,
        IHnswIndexManager hnswManager,
        VectorStore bruteForceStore)
    {
        _logger = logger;
        _settings = settings.Value;
        _hnswManager = hnswManager;
        _bruteForceStore = bruteForceStore;
    }

    /// <inheritdoc />
    public void InitializeIndex(string indexName, string fieldName, int dimensions)
    {
        if (_settings.UseHnsw)
        {
            _hnswManager.CreateOrOpenIndex(indexName, fieldName, dimensions, _settings.MaxVectorsPerIndex);
        }

        lock (_lock)
        {
            _knownVectorFields.Add($"{indexName}/{fieldName}");
        }

        _logger.LogDebug("Initialized vector index for {IndexName}/{FieldName} with {Dimensions} dimensions (HNSW: {UseHnsw})",
            indexName, fieldName, dimensions, _settings.UseHnsw);
    }

    /// <inheritdoc />
    public bool IndexExists(string indexName, string fieldName)
    {
        if (_settings.UseHnsw)
        {
            return _hnswManager.IndexExists(indexName, fieldName);
        }

        lock (_lock)
        {
            return _knownVectorFields.Contains($"{indexName}/{fieldName}");
        }
    }

    /// <inheritdoc />
    public void AddVector(string indexName, string fieldName, string documentId, float[] vector)
    {
        // Always add to brute-force store (used for retrieval and fallback)
        _bruteForceStore.AddVector(indexName, fieldName, documentId, vector);

        // Add to HNSW if enabled
        if (_settings.UseHnsw)
        {
            // Auto-initialize if not exists
            if (!_hnswManager.IndexExists(indexName, fieldName))
            {
                _hnswManager.CreateOrOpenIndex(indexName, fieldName, vector.Length, _settings.MaxVectorsPerIndex);
            }
            _hnswManager.AddVector(indexName, fieldName, documentId, vector);
        }

        lock (_lock)
        {
            _knownVectorFields.Add($"{indexName}/{fieldName}");
        }
    }

    /// <inheritdoc />
    public void RemoveVector(string indexName, string fieldName, string documentId)
    {
        _bruteForceStore.RemoveVector(indexName, fieldName, documentId);

        if (_settings.UseHnsw && _hnswManager.IndexExists(indexName, fieldName))
        {
            _hnswManager.RemoveVector(indexName, fieldName, documentId);
        }
    }

    /// <inheritdoc />
    public void RemoveDocument(string indexName, string documentId)
    {
        _bruteForceStore.RemoveDocument(indexName, documentId);

        // Remove from all HNSW indexes for this search index
        if (_settings.UseHnsw)
        {
            lock (_lock)
            {
                var fieldsForIndex = _knownVectorFields
                    .Where(k => k.StartsWith($"{indexName}/"))
                    .Select(k => k.Split('/')[1])
                    .ToList();

                foreach (var fieldName in fieldsForIndex)
                {
                    if (_hnswManager.IndexExists(indexName, fieldName))
                    {
                        _hnswManager.RemoveVector(indexName, fieldName, documentId);
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<VectorSearchResult> Search(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k)
    {
        if (_settings.UseHnsw && _hnswManager.IndexExists(indexName, fieldName))
        {
            return SearchWithHnsw(indexName, fieldName, queryVector, k);
        }

        return SearchWithBruteForce(indexName, fieldName, queryVector, k);
    }

    /// <inheritdoc />
    public IReadOnlyList<VectorSearchResult> SearchWithFilter(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        ISet<string> candidateDocumentIds)
    {
        if (candidateDocumentIds.Count == 0)
        {
            return Array.Empty<VectorSearchResult>();
        }

        if (_settings.UseHnsw && _hnswManager.IndexExists(indexName, fieldName))
        {
            return SearchWithHnswFiltered(indexName, fieldName, queryVector, k, candidateDocumentIds);
        }

        return SearchWithBruteForceFiltered(indexName, fieldName, queryVector, k, candidateDocumentIds);
    }

    /// <inheritdoc />
    public int GetVectorCount(string indexName, string fieldName)
    {
        if (_settings.UseHnsw && _hnswManager.IndexExists(indexName, fieldName))
        {
            return _hnswManager.GetVectorCount(indexName, fieldName);
        }

        return _bruteForceStore.GetVectorCount(indexName, fieldName);
    }

    /// <inheritdoc />
    public void DeleteIndex(string indexName, string? fieldName = null)
    {
        if (fieldName != null)
        {
            _bruteForceStore.RemoveVector(indexName, fieldName, "*"); // Note: VectorStore doesn't have field-level clear
            
            if (_settings.UseHnsw)
            {
                _hnswManager.DeleteIndex(indexName, fieldName);
            }

            lock (_lock)
            {
                _knownVectorFields.Remove($"{indexName}/{fieldName}");
            }
        }
        else
        {
            _bruteForceStore.ClearIndex(indexName);
            
            if (_settings.UseHnsw)
            {
                _hnswManager.DeleteIndex(indexName);
            }

            lock (_lock)
            {
                var keysToRemove = _knownVectorFields.Where(k => k.StartsWith($"{indexName}/")).ToList();
                foreach (var key in keysToRemove)
                {
                    _knownVectorFields.Remove(key);
                }
            }
        }

        _logger.LogInformation("Deleted vector index for {IndexName}/{FieldName}", indexName, fieldName ?? "*");
    }

    /// <inheritdoc />
    public void SaveAll()
    {
        if (_settings.UseHnsw)
        {
            _hnswManager.SaveAllIndexes();
        }
    }

    private IReadOnlyList<VectorSearchResult> SearchWithHnsw(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k)
    {
        var results = _hnswManager.Search(indexName, fieldName, queryVector, k);
        
        return results
            .Select(r => new VectorSearchResult
            {
                DocumentId = r.DocumentId,
                Distance = r.Distance,
                Score = DistanceToScore(r.Distance)
            })
            .ToList();
    }

    private IReadOnlyList<VectorSearchResult> SearchWithHnswFiltered(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        ISet<string> candidateDocumentIds)
    {
        // Use oversampling to get more candidates, then filter
        var oversampleMultiplier = _settings.HnswSettings.OversampleMultiplier;
        var candidates = _hnswManager.SearchWithOversampling(
            indexName, fieldName, queryVector, k, oversampleMultiplier);

        // Filter to only candidates that match
        var filtered = candidates
            .Where(r => candidateDocumentIds.Contains(r.DocumentId))
            .Select(r => new VectorSearchResult
            {
                DocumentId = r.DocumentId,
                Distance = r.Distance,
                Score = DistanceToScore(r.Distance)
            })
            .Take(k)
            .ToList();

        // If we didn't get enough results, fall back to brute force on the candidates
        if (filtered.Count < k && candidateDocumentIds.Count > filtered.Count)
        {
            _logger.LogDebug("HNSW filtered search returned {Count}/{K} results, falling back to brute force for remaining",
                filtered.Count, k);
            
            var foundIds = filtered.Select(r => r.DocumentId).ToHashSet();
            var remainingCandidates = candidateDocumentIds.Where(id => !foundIds.Contains(id)).ToHashSet();
            
            if (remainingCandidates.Count > 0)
            {
                var bruteForceResults = SearchWithBruteForceFiltered(
                    indexName, fieldName, queryVector, k - filtered.Count, remainingCandidates);
                
                filtered.AddRange(bruteForceResults);
                filtered = filtered.OrderByDescending(r => r.Score).Take(k).ToList();
            }
        }

        return filtered;
    }

    private IReadOnlyList<VectorSearchResult> SearchWithBruteForce(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k)
    {
        var results = _bruteForceStore.Search(indexName, fieldName, queryVector, k);
        
        return results
            .Select(r => new VectorSearchResult
            {
                DocumentId = r.DocumentKey,
                Distance = (float)(1.0 - r.Similarity), // Convert similarity to distance
                Score = r.Similarity
            })
            .ToList();
    }

    private IReadOnlyList<VectorSearchResult> SearchWithBruteForceFiltered(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        ISet<string> candidateDocumentIds)
    {
        var results = _bruteForceStore.Search(indexName, fieldName, queryVector, k * 10, candidateDocumentIds);
        
        return results
            .Take(k)
            .Select(r => new VectorSearchResult
            {
                DocumentId = r.DocumentKey,
                Distance = (float)(1.0 - r.Similarity),
                Score = r.Similarity
            })
            .ToList();
    }

    /// <summary>
    /// Converts HNSW distance to a similarity score.
    /// </summary>
    private double DistanceToScore(float distance)
    {
        // For cosine distance: score = 1 / (1 + distance)
        // This gives a value between 0 and 1, where 1 is most similar
        return 1.0 / (1.0 + distance);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SaveAll();
    }
}
