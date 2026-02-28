using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Manages Lucene index lifecycle for each search index.
/// </summary>
public class LuceneIndexManager : IDisposable
{
    private readonly ILogger<LuceneIndexManager> _logger;
    private readonly LuceneSettings _settings;
    private readonly ConcurrentDictionary<string, IndexHolder> _indexes = new();
    private readonly ConcurrentDictionary<string, SimilarityAlgorithm> _similarityConfigs = new();
    private readonly object _lock = new();
    private bool _disposed;

    public LuceneIndexManager(
        ILogger<LuceneIndexManager> logger,
        IOptions<LuceneSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Configures the similarity algorithm for the specified index.
    /// If the similarity has changed, the index holder is rebuilt to apply the new settings.
    /// This should be called before GetWriter/GetSearcher when the index definition is available.
    /// </summary>
    public void ConfigureSimilarity(string indexName, SimilarityAlgorithm? similarity)
    {
        ThrowIfDisposed();
        var effectiveSimilarity = similarity ?? new SimilarityAlgorithm();
        
        var previousSimilarity = _similarityConfigs.GetValueOrDefault(indexName);
        _similarityConfigs[indexName] = effectiveSimilarity;

        // If the index holder already exists and the similarity has changed, rebuild it.
        // When previousSimilarity is null (first call), the holder may have been created
        // with default similarity by another code path (e.g., GetDocumentCountAsync).
        // In that case, compare against the default SimilarityAlgorithm.
        if (_indexes.ContainsKey(indexName))
        {
            var compareWith = previousSimilarity ?? new SimilarityAlgorithm();
            if (SimilarityChanged(compareWith, effectiveSimilarity))
            {
                lock (_lock)
                {
                    if (_indexes.TryRemove(indexName, out var oldHolder))
                    {
                        _logger.LogInformation(
                            "Similarity configuration changed for {IndexName}, rebuilding index holder",
                            indexName);
                        oldHolder.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets or creates an IndexWriter for the specified index.
    /// </summary>
    public IndexWriter GetWriter(string indexName)
    {
        ThrowIfDisposed();
        var holder = GetOrCreateHolder(indexName);
        return holder.Writer;
    }

    /// <summary>
    /// Gets a fresh IndexSearcher for the specified index.
    /// </summary>
    public IndexSearcher GetSearcher(string indexName)
    {
        ThrowIfDisposed();
        var holder = GetOrCreateHolder(indexName);
        holder.RefreshReader();
        return holder.Searcher;
    }

    /// <summary>
    /// Gets the directory for the specified index.
    /// </summary>
    public Lucene.Net.Store.Directory GetDirectory(string indexName)
    {
        var holder = GetOrCreateHolder(indexName);
        return holder.Directory;
    }

    /// <summary>
    /// Gets the analyzer for the specified index.
    /// </summary>
    public Analyzer GetAnalyzer(string indexName)
    {
        var holder = GetOrCreateHolder(indexName);
        return holder.Analyzer;
    }

    /// <summary>
    /// Commits pending changes for the specified index.
    /// </summary>
    public void Commit(string indexName)
    {
        ThrowIfDisposed();
        if (_indexes.TryGetValue(indexName, out var holder))
        {
            holder.Writer.Commit();
            holder.RefreshReader();
            _logger.LogDebug("Committed changes to index {IndexName}", indexName);
        }
    }

    /// <summary>
    /// Deletes all documents from the specified index.
    /// </summary>
    public void ClearIndex(string indexName)
    {
        ThrowIfDisposed();
        if (_indexes.TryGetValue(indexName, out var holder))
        {
            holder.Writer.DeleteAll();
            holder.Writer.Commit();
            holder.RefreshReader();
            _logger.LogInformation("Cleared all documents from index {IndexName}", indexName);
        }
    }

    /// <summary>
    /// Removes and closes the index holder for the specified index.
    /// </summary>
    public void CloseIndex(string indexName)
    {
        if (_indexes.TryRemove(indexName, out var holder))
        {
            holder.Dispose();
            _logger.LogInformation("Closed index {IndexName}", indexName);
        }
    }

    /// <summary>
    /// Deletes the index directory from disk.
    /// </summary>
    public void DeleteIndex(string indexName)
    {
        CloseIndex(indexName);

        var indexPath = GetIndexPath(indexName);
        if (System.IO.Directory.Exists(indexPath))
        {
            System.IO.Directory.Delete(indexPath, recursive: true);
            _logger.LogInformation("Deleted index directory {Path}", indexPath);
        }
    }

    /// <summary>
    /// Checks if an index exists (has documents or directory).
    /// </summary>
    public bool IndexExists(string indexName)
    {
        var indexPath = GetIndexPath(indexName);
        return System.IO.Directory.Exists(indexPath) && 
               System.IO.Directory.GetFiles(indexPath).Length > 0;
    }

    /// <summary>
    /// Gets the storage size in bytes for the specified index.
    /// </summary>
    public long GetStorageSize(string indexName)
    {
        var indexPath = GetIndexPath(indexName);
        if (!System.IO.Directory.Exists(indexPath))
        {
            return 0;
        }

        return System.IO.Directory.GetFiles(indexPath, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool SimilarityChanged(SimilarityAlgorithm previous, SimilarityAlgorithm current)
    {
        if (previous.ODataType != current.ODataType)
            return true;

        // For BM25, check if k1/b changed
        if (current.ODataType == "#Microsoft.Azure.Search.BM25Similarity")
        {
            var prevK1 = previous.K1 ?? 1.2;
            var prevB = previous.B ?? 0.75;
            var curK1 = current.K1 ?? 1.2;
            var curB = current.B ?? 0.75;
            return Math.Abs(prevK1 - curK1) > 0.0001 || Math.Abs(prevB - curB) > 0.0001;
        }

        return false;
    }

    /// <summary>
    /// Creates the appropriate Lucene Similarity from a SimilarityAlgorithm configuration.
    /// </summary>
    public static Similarity CreateLuceneSimilarity(SimilarityAlgorithm? similarity)
    {
        if (similarity?.ODataType == "#Microsoft.Azure.Search.ClassicSimilarity")
        {
            return new DefaultSimilarity();
        }

        // Default to BM25
        var k1 = (float)(similarity?.K1 ?? 1.2);
        var b = (float)(similarity?.B ?? 0.75);
        return new BM25Similarity(k1, b);
    }

    private IndexHolder GetOrCreateHolder(string indexName)
    {
        return _indexes.GetOrAdd(indexName, name =>
        {
            _logger.LogInformation("Creating Lucene index holder for {IndexName}", name);
            var similarity = _similarityConfigs.GetValueOrDefault(name);
            return new IndexHolder(name, GetIndexPath(name), similarity, _logger);
        });
    }

    private string GetIndexPath(string indexName)
    {
        var basePath = _settings.IndexPath;
        
        // Use AppData if not specified
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AzureAISearchSimulator",
                "Indexes");
        }

        return Path.Combine(basePath, indexName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var holder in _indexes.Values)
        {
            holder.Dispose();
        }
        _indexes.Clear();
        _disposed = true;
    }

    /// <summary>
    /// Holds Lucene resources for a single index.
    /// </summary>
    private class IndexHolder : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _indexName;
        private DirectoryReader? _reader;
        private IndexSearcher? _searcher;
        private bool _disposed;

        public Lucene.Net.Store.Directory Directory { get; }
        public Analyzer Analyzer { get; }
        public IndexWriter Writer { get; }
        public Similarity LuceneSimilarity { get; }

        public IndexSearcher Searcher
        {
            get
            {
                if (_searcher == null)
                {
                    RefreshReader();
                }
                return _searcher!;
            }
        }

        public IndexHolder(string indexName, string indexPath, SimilarityAlgorithm? similarity, ILogger logger)
        {
            _logger = logger;
            _indexName = indexName;

            // Ensure directory exists
            System.IO.Directory.CreateDirectory(indexPath);

            // Use FSDirectory for persistence
            Directory = FSDirectory.Open(indexPath);
            
            // Standard analyzer for text processing
            Analyzer = new StandardAnalyzer(LuceneDocumentMapper.AppLuceneVersion);

            // Create the Lucene similarity from the index definition
            LuceneSimilarity = CreateLuceneSimilarity(similarity);

            var config = new IndexWriterConfig(LuceneDocumentMapper.AppLuceneVersion, Analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                Similarity = LuceneSimilarity
            };

            Writer = new IndexWriter(Directory, config);
            
            _logger.LogDebug(
                "Created IndexWriter for {IndexName} at {Path} with similarity {SimilarityType}",
                indexName, indexPath, LuceneSimilarity.GetType().Name);
        }

        public void RefreshReader()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException($"IndexHolder[{_indexName}]",
                    $"Cannot refresh reader for index '{_indexName}' because it has been disposed.");
            }

            var newReader = _reader == null
                ? DirectoryReader.Open(Writer, applyAllDeletes: true)
                : DirectoryReader.OpenIfChanged(_reader, Writer, applyAllDeletes: true);

            if (newReader != null && newReader != _reader)
            {
                _reader?.Dispose();
                _reader = newReader;
                _searcher = new IndexSearcher(_reader)
                {
                    Similarity = LuceneSimilarity
                };
            }
            else if (_reader == null)
            {
                // No documents yet, create empty reader
                _reader = DirectoryReader.Open(Writer, applyAllDeletes: true);
                _searcher = new IndexSearcher(_reader)
                {
                    Similarity = LuceneSimilarity
                };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _reader?.Dispose();
                Writer.Dispose();
                Analyzer.Dispose();
                Directory.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing index holder for {IndexName}", _indexName);
            }

            _disposed = true;
        }
    }
}
