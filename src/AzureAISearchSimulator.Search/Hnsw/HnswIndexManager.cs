using System.Collections.Concurrent;
using HNSW.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Manages HNSW indexes for fast approximate nearest neighbor search.
/// Uses HNSW.Net (SmallWorld) for efficient vector search with O(log n) query time.
/// </summary>
public class HnswIndexManager : IHnswIndexManager
{
    private readonly ILogger<HnswIndexManager> _logger;
    private readonly VectorSearchSettings _settings;
    private readonly string _dataDirectory;
    private readonly ConcurrentDictionary<string, HnswIndexHolder> _indexes = new();
    private readonly object _lock = new();
    private bool _disposed;

    public HnswIndexManager(
        ILogger<HnswIndexManager> logger,
        IOptions<VectorSearchSettings> settings,
        IOptions<LuceneSettings> luceneSettings)
    {
        _logger = logger;
        _settings = settings.Value;
        _dataDirectory = Path.Combine(luceneSettings.Value.IndexPath, "hnsw");
        
        // Ensure HNSW data directory exists
        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }
    }

    /// <inheritdoc />
    public void CreateOrOpenIndex(string indexName, string fieldName, int dimensions, int maxElements)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        lock (_lock)
        {
            if (_indexes.ContainsKey(key))
            {
                _logger.LogDebug("HNSW index already exists for {IndexName}/{FieldName}", indexName, fieldName);
                return;
            }

            var indexPath = GetIndexPath(indexName, fieldName);
            var mappingPath = GetMappingPath(indexName, fieldName);
            
            HnswIndexHolder holder;
            
            if (File.Exists(indexPath) && File.Exists(mappingPath))
            {
                // Load existing index
                _logger.LogInformation("Loading existing HNSW index from {Path}", indexPath);
                holder = LoadIndex(indexPath, mappingPath, dimensions);
            }
            else
            {
                // Create new index
                _logger.LogInformation("Creating new HNSW index for {IndexName}/{FieldName} with {Dimensions} dimensions", 
                    indexName, fieldName, dimensions);
                holder = CreateNewIndex(dimensions);
            }

            _indexes[key] = holder;
        }
    }

    /// <inheritdoc />
    public bool IndexExists(string indexName, string fieldName)
    {
        var key = GetIndexKey(indexName, fieldName);
        return _indexes.ContainsKey(key);
    }

    /// <inheritdoc />
    public void AddVector(string indexName, string fieldName, string documentId, float[] vector)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            throw new InvalidOperationException($"HNSW index not found for {indexName}/{fieldName}. Call CreateOrOpenIndex first.");
        }

        lock (holder.Lock)
        {
            // Check if document already exists
            if (holder.DocumentIdToIndex.TryGetValue(documentId, out var existingIndex))
            {
                // Update: Mark old index as deleted and add new one
                holder.DeletedIndices.Add(existingIndex);
                holder.IndexToDocumentId.Remove(existingIndex);
                holder.DocumentIdToIndex.Remove(documentId);
            }

            // Add the vector to our list
            var index = holder.Vectors.Count;
            holder.Vectors.Add(vector);
            
            // Update mappings
            holder.IndexToDocumentId[index] = documentId;
            holder.DocumentIdToIndex[documentId] = index;
            holder.IsDirty = true;
            
            _logger.LogDebug("Added vector for document {DocumentId} at index {Index}", documentId, index);
        }
    }

    /// <inheritdoc />
    public void AddVectors(string indexName, string fieldName, IEnumerable<(string DocumentId, float[] Vector)> documents)
    {
        foreach (var (documentId, vector) in documents)
        {
            AddVector(indexName, fieldName, documentId, vector);
        }
    }

    /// <inheritdoc />
    public void RemoveVector(string indexName, string fieldName, string documentId)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            return;
        }

        lock (holder.Lock)
        {
            if (holder.DocumentIdToIndex.TryGetValue(documentId, out var index))
            {
                // Mark as deleted
                holder.DeletedIndices.Add(index);
                holder.IndexToDocumentId.Remove(index);
                holder.DocumentIdToIndex.Remove(documentId);
                holder.IsDirty = true;
                
                _logger.LogDebug("Marked vector as deleted for document {DocumentId}", documentId);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DocumentId, float Distance)> Search(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k)
    {
        return SearchWithOversampling(indexName, fieldName, queryVector, k, 1);
    }

    /// <inheritdoc />
    public IReadOnlyList<(string DocumentId, float Distance)> SearchWithOversampling(
        string indexName,
        string fieldName,
        float[] queryVector,
        int k,
        int oversampleMultiplier)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            _logger.LogWarning("HNSW index not found for {IndexName}/{FieldName}", indexName, fieldName);
            return Array.Empty<(string, float)>();
        }

        lock (holder.Lock)
        {
            // Rebuild graph if dirty
            if (holder.IsDirty || holder.Graph == null)
            {
                RebuildGraphInternal(holder);
            }

            if (holder.Graph == null || holder.ActiveVectors.Count == 0)
            {
                return Array.Empty<(string, float)>();
            }

            var actualK = Math.Min(k * oversampleMultiplier, holder.ActiveVectors.Count);
            if (actualK == 0)
            {
                return Array.Empty<(string, float)>();
            }

            // Perform KNN search
            var searchResults = holder.Graph.KNNSearch(queryVector, actualK);

            var results = new List<(string DocumentId, float Distance)>();
            
            foreach (var result in searchResults)
            {
                var originalIndex = holder.ActiveIndexMapping[result.Id];
                if (holder.IndexToDocumentId.TryGetValue(originalIndex, out var documentId))
                {
                    results.Add((documentId, result.Distance));
                }
            }

            return results.OrderBy(r => r.Distance).ToList();
        }
    }

    /// <inheritdoc />
    public string? GetDocumentId(string indexName, string fieldName, long label)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            return null;
        }

        lock (holder.Lock)
        {
            return holder.IndexToDocumentId.TryGetValue((int)label, out var documentId) ? documentId : null;
        }
    }

    /// <inheritdoc />
    public long? GetLabel(string indexName, string fieldName, string documentId)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            return null;
        }

        lock (holder.Lock)
        {
            return holder.DocumentIdToIndex.TryGetValue(documentId, out var index) ? index : null;
        }
    }

    /// <inheritdoc />
    public int GetVectorCount(string indexName, string fieldName)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            return 0;
        }

        lock (holder.Lock)
        {
            return holder.DocumentIdToIndex.Count;
        }
    }

    /// <inheritdoc />
    public void SaveIndex(string indexName, string fieldName)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        if (!_indexes.TryGetValue(key, out var holder))
        {
            return;
        }

        var indexPath = GetIndexPath(indexName, fieldName);
        var mappingPath = GetMappingPath(indexName, fieldName);
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (holder.Lock)
        {
            // Save vectors and mappings
            SaveData(holder, indexPath, mappingPath);
            
            _logger.LogInformation("Saved HNSW index to {Path}", indexPath);
        }
    }

    /// <inheritdoc />
    public void SaveAllIndexes()
    {
        foreach (var key in _indexes.Keys)
        {
            var parts = key.Split('/');
            if (parts.Length == 2)
            {
                SaveIndex(parts[0], parts[1]);
            }
        }
    }

    /// <inheritdoc />
    public void DeleteIndex(string indexName, string? fieldName = null)
    {
        if (fieldName != null)
        {
            // Delete specific field index
            var key = GetIndexKey(indexName, fieldName);
            _indexes.TryRemove(key, out _);

            // Delete files
            var indexPath = GetIndexPath(indexName, fieldName);
            var mappingPath = GetMappingPath(indexName, fieldName);
            
            if (File.Exists(indexPath)) File.Delete(indexPath);
            if (File.Exists(mappingPath)) File.Delete(mappingPath);
            
            _logger.LogInformation("Deleted HNSW index for {IndexName}/{FieldName}", indexName, fieldName);
        }
        else
        {
            // Delete all field indexes for this search index
            var keysToRemove = _indexes.Keys.Where(k => k.StartsWith($"{indexName}/")).ToList();
            
            foreach (var key in keysToRemove)
            {
                _indexes.TryRemove(key, out _);
            }

            // Delete directory
            var indexDir = Path.Combine(_dataDirectory, indexName);
            if (Directory.Exists(indexDir))
            {
                Directory.Delete(indexDir, recursive: true);
            }
            
            _logger.LogInformation("Deleted all HNSW indexes for {IndexName}", indexName);
        }
    }

    /// <inheritdoc />
    public void RebuildIndex(string indexName, string fieldName, IEnumerable<(string DocumentId, float[] Vector)> documents)
    {
        var key = GetIndexKey(indexName, fieldName);
        
        var documentList = documents.ToList();
        if (documentList.Count == 0)
        {
            return;
        }

        var dimensions = documentList[0].Vector.Length;

        lock (_lock)
        {
            // Create new holder
            var newHolder = CreateNewIndex(dimensions);
            
            // Add all documents
            foreach (var (documentId, vector) in documentList)
            {
                var index = newHolder.Vectors.Count;
                newHolder.Vectors.Add(vector);
                newHolder.IndexToDocumentId[index] = documentId;
                newHolder.DocumentIdToIndex[documentId] = index;
            }
            
            newHolder.IsDirty = true;
            _indexes[key] = newHolder;
            
            _logger.LogInformation("Rebuilt HNSW index for {IndexName}/{FieldName} with {Count} vectors", 
                indexName, fieldName, documentList.Count);
        }
    }

    private void RebuildGraphInternal(HnswIndexHolder holder)
    {
        // Get active vectors (excluding deleted)
        holder.ActiveVectors.Clear();
        holder.ActiveIndexMapping.Clear();
        
        for (int i = 0; i < holder.Vectors.Count; i++)
        {
            if (!holder.DeletedIndices.Contains(i))
            {
                holder.ActiveIndexMapping[holder.ActiveVectors.Count] = i;
                holder.ActiveVectors.Add(holder.Vectors[i]);
            }
        }

        if (holder.ActiveVectors.Count == 0)
        {
            holder.Graph = null;
            holder.IsDirty = false;
            return;
        }

        // Create parameters
        var parameters = new SmallWorldParameters
        {
            M = _settings.HnswSettings.M,
            LevelLambda = 1.0 / Math.Log(_settings.HnswSettings.M),
            EfSearch = _settings.HnswSettings.EfSearch,
            ConstructionPruning = _settings.HnswSettings.EfConstruction,
            NeighbourHeuristic = NeighbourSelectionHeuristic.SelectSimple,
            EnableDistanceCacheForConstruction = true
        };

        // Get distance function
        var distanceFunc = GetDistanceFunction(_settings.SimilarityMetric);

        // Use the default random generator from the library
        var generator = DefaultRandomGenerator.Instance;

        // Build new graph
        holder.Graph = new SmallWorld<float[], float>(distanceFunc, generator, parameters);
        holder.Graph.AddItems(holder.ActiveVectors);
        
        holder.IsDirty = false;
        _logger.LogDebug("Rebuilt HNSW graph with {Count} vectors", holder.ActiveVectors.Count);
    }

    private HnswIndexHolder CreateNewIndex(int dimensions)
    {
        return new HnswIndexHolder
        {
            Dimensions = dimensions
        };
    }

    private HnswIndexHolder LoadIndex(string indexPath, string mappingPath, int dimensions)
    {
        var holder = new HnswIndexHolder
        {
            Dimensions = dimensions
        };

        // Load data
        LoadData(holder, indexPath, mappingPath);
        holder.IsDirty = true; // Graph needs to be rebuilt

        return holder;
    }

    private static Func<float[], float[], float> GetDistanceFunction(string similarityMetric)
    {
        return similarityMetric.ToLowerInvariant() switch
        {
            "cosine" => CosineDistance.NonOptimized,
            "euclidean" or "l2" => EuclideanDistance,
            "dotproduct" or "innerproduct" or "ip" => DotProductDistance,
            _ => CosineDistance.NonOptimized
        };
    }

    private static float EuclideanDistance(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return (float)Math.Sqrt(sum);
    }

    private static float DotProductDistance(float[] a, float[] b)
    {
        float dot = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }
        // Convert to distance (higher dot product = more similar = lower distance)
        return 1f - dot;
    }

    private void SaveData(HnswIndexHolder holder, string indexPath, string mappingPath)
    {
        using var writer = new BinaryWriter(File.Create(indexPath));
        
        // Write version
        writer.Write(1);
        
        // Write dimensions
        writer.Write(holder.Dimensions);
        
        // Write vectors
        writer.Write(holder.Vectors.Count);
        foreach (var vector in holder.Vectors)
        {
            writer.Write(vector.Length);
            foreach (var value in vector)
            {
                writer.Write(value);
            }
        }

        using var mappingWriter = new BinaryWriter(File.Create(mappingPath));
        
        // Write version
        mappingWriter.Write(1);
        
        // Write index to document ID mappings
        mappingWriter.Write(holder.IndexToDocumentId.Count);
        foreach (var (index, documentId) in holder.IndexToDocumentId)
        {
            mappingWriter.Write(index);
            mappingWriter.Write(documentId);
        }
        
        // Write deleted indices
        mappingWriter.Write(holder.DeletedIndices.Count);
        foreach (var index in holder.DeletedIndices)
        {
            mappingWriter.Write(index);
        }
    }

    private void LoadData(HnswIndexHolder holder, string indexPath, string mappingPath)
    {
        using var reader = new BinaryReader(File.OpenRead(indexPath));
        
        // Read version
        var version = reader.ReadInt32();
        if (version != 1)
        {
            throw new InvalidOperationException($"Unsupported index file version: {version}");
        }
        
        // Read dimensions
        holder.Dimensions = reader.ReadInt32();
        
        // Read vectors
        var vectorCount = reader.ReadInt32();
        for (int i = 0; i < vectorCount; i++)
        {
            var length = reader.ReadInt32();
            var vector = new float[length];
            for (int j = 0; j < length; j++)
            {
                vector[j] = reader.ReadSingle();
            }
            holder.Vectors.Add(vector);
        }

        using var mappingReader = new BinaryReader(File.OpenRead(mappingPath));
        
        // Read version
        var mappingVersion = mappingReader.ReadInt32();
        if (mappingVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported mapping file version: {mappingVersion}");
        }
        
        // Read index to document ID mappings
        var mappingCount = mappingReader.ReadInt32();
        for (int i = 0; i < mappingCount; i++)
        {
            var index = mappingReader.ReadInt32();
            var documentId = mappingReader.ReadString();
            holder.IndexToDocumentId[index] = documentId;
            holder.DocumentIdToIndex[documentId] = index;
        }
        
        // Read deleted indices
        var deletedCount = mappingReader.ReadInt32();
        for (int i = 0; i < deletedCount; i++)
        {
            var index = mappingReader.ReadInt32();
            holder.DeletedIndices.Add(index);
        }
    }

    private static string GetIndexKey(string indexName, string fieldName) => $"{indexName}/{fieldName}";

    private string GetIndexPath(string indexName, string fieldName) => 
        Path.Combine(_dataDirectory, indexName, $"{fieldName}.hnsw");

    private string GetMappingPath(string indexName, string fieldName) => 
        Path.Combine(_dataDirectory, indexName, $"{fieldName}.mapping");

    /// <inheritdoc />
    public long GetVectorIndexSize(string indexName)
    {
        var indexDir = Path.Combine(_dataDirectory, indexName);
        if (!Directory.Exists(indexDir))
        {
            return 0;
        }

        return Directory.GetFiles(indexDir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            // Save all indexes before disposing
            SaveAllIndexes();
            _indexes.Clear();
        }
    }

    /// <summary>
    /// Holds an HNSW index and its associated mappings.
    /// </summary>
    private class HnswIndexHolder
    {
        public SmallWorld<float[], float>? Graph { get; set; }
        public int Dimensions { get; set; }
        public List<float[]> Vectors { get; } = new();
        public List<float[]> ActiveVectors { get; } = new();
        public Dictionary<int, int> ActiveIndexMapping { get; } = new(); // Active index -> Original index
        public Dictionary<int, string> IndexToDocumentId { get; } = new();
        public Dictionary<string, int> DocumentIdToIndex { get; } = new();
        public HashSet<int> DeletedIndices { get; } = new();
        public bool IsDirty { get; set; } = true;
        public object Lock { get; } = new();
    }
}

