using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for HnswVectorSearchService.
/// </summary>
public class HnswVectorSearchServiceTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly HnswVectorSearchService _service;
    private readonly HnswIndexManager _hnswManager;
    private readonly VectorStore _vectorStore;

    public HnswVectorSearchServiceTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"vector-search-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);

        var vectorSettings = new VectorSearchSettings
        {
            DefaultDimensions = 4,
            MaxVectorsPerIndex = 1000,
            SimilarityMetric = "cosine",
            UseHnsw = true,
            HnswSettings = new HnswSettings
            {
                M = 16,
                EfConstruction = 100,
                EfSearch = 50,
                OversampleMultiplier = 5,
                RandomSeed = 42
            },
            HybridSearchSettings = new HybridSearchSettings()
        };

        var luceneSettings = new LuceneSettings { IndexPath = _testDataPath };

        _vectorStore = new VectorStore();

        _hnswManager = new HnswIndexManager(
            Mock.Of<ILogger<HnswIndexManager>>(),
            Options.Create(vectorSettings),
            Options.Create(luceneSettings));

        _service = new HnswVectorSearchService(
            Mock.Of<ILogger<HnswVectorSearchService>>(),
            Options.Create(vectorSettings),
            _hnswManager,
            _vectorStore);
    }

    public void Dispose()
    {
        _service.Dispose();
        _hnswManager.Dispose();

        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void InitializeIndex_ShouldCreateIndex()
    {
        // Act
        _service.InitializeIndex("test-index", "embedding", dimensions: 4);

        // Assert
        Assert.True(_service.IndexExists("test-index", "embedding"));
    }

    [Fact]
    public void AddVector_ShouldAddToBothStores()
    {
        // Arrange
        _service.InitializeIndex("test-index", "embedding", dimensions: 4);
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        _service.AddVector("test-index", "embedding", "doc1", vector);

        // Assert
        Assert.Equal(1, _service.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void AddVector_AutoInitializesIndex()
    {
        // Arrange - Don't call InitializeIndex
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        _service.AddVector("test-index", "embedding", "doc1", vector);

        // Assert
        Assert.True(_service.IndexExists("test-index", "embedding"));
        Assert.Equal(1, _service.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void RemoveVector_ShouldRemoveFromBothStores()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act
        _service.RemoveVector("test-index", "embedding", "doc1");

        // Assert
        Assert.Equal(1, _service.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void RemoveDocument_ShouldRemoveFromAllFields()
    {
        // Arrange
        _service.AddVector("test-index", "titleVector", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _service.AddVector("test-index", "contentVector", "doc1", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act
        _service.RemoveDocument("test-index", "doc1");

        // Assert
        Assert.Equal(0, _service.GetVectorCount("test-index", "titleVector"));
        Assert.Equal(0, _service.GetVectorCount("test-index", "contentVector"));
    }

    [Fact]
    public void Search_ShouldReturnNearestNeighbors()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { 0.9f, 0.1f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc3", new[] { 0.0f, 1.0f, 0.0f, 0.0f });

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _service.Search("test-index", "embedding", queryVector, k: 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("doc1", results[0].DocumentId); // Exact match
        Assert.True(results[0].Score > results[1].Score); // First result has higher score
    }

    [Fact]
    public void Search_ResultsHaveScoreAndDistance()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _service.Search("test-index", "embedding", queryVector, k: 1);

        // Assert
        Assert.Single(results);
        Assert.True(results[0].Score > 0);
        Assert.True(results[0].Distance >= 0);
    }

    [Fact]
    public void Search_EmptyIndex_ShouldReturnEmptyList()
    {
        // Arrange
        _service.InitializeIndex("test-index", "embedding", dimensions: 4);
        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _service.Search("test-index", "embedding", queryVector, k: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void SearchWithFilter_ShouldOnlyReturnMatchingDocuments()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { 0.9f, 0.1f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc3", new[] { 0.8f, 0.2f, 0.0f, 0.0f });

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var candidates = new HashSet<string> { "doc2", "doc3" }; // Exclude doc1

        // Act
        var results = _service.SearchWithFilter("test-index", "embedding", queryVector, k: 2, candidates);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.DocumentId == "doc1");
        Assert.Contains(results, r => r.DocumentId == "doc2");
        Assert.Contains(results, r => r.DocumentId == "doc3");
    }

    [Fact]
    public void SearchWithFilter_EmptyCandidates_ShouldReturnEmpty()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var candidates = new HashSet<string>();

        // Act
        var results = _service.SearchWithFilter("test-index", "embedding", queryVector, k: 10, candidates);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void SearchWithFilter_RespectsKLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var vec = new[] { (float)i / 10f, 1f - (float)i / 10f, 0f, 0f };
            _service.AddVector("test-index", "embedding", $"doc{i}", vec);
        }

        var queryVector = new[] { 0.5f, 0.5f, 0.0f, 0.0f };
        var candidates = Enumerable.Range(0, 10).Select(i => $"doc{i}").ToHashSet();

        // Act
        var results = _service.SearchWithFilter("test-index", "embedding", queryVector, k: 3, candidates);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void DeleteIndex_SpecificField_ShouldOnlyDeleteThatField()
    {
        // Arrange
        _service.AddVector("test-index", "embedding1", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _service.AddVector("test-index", "embedding2", "doc1", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act
        _service.DeleteIndex("test-index", "embedding1");

        // Assert
        Assert.False(_service.IndexExists("test-index", "embedding1"));
        Assert.True(_service.IndexExists("test-index", "embedding2"));
    }

    [Fact]
    public void DeleteIndex_AllFields_ShouldDeleteEntireIndex()
    {
        // Arrange
        _service.AddVector("test-index", "embedding1", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _service.AddVector("test-index", "embedding2", "doc1", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act
        _service.DeleteIndex("test-index");

        // Assert
        Assert.False(_service.IndexExists("test-index", "embedding1"));
        Assert.False(_service.IndexExists("test-index", "embedding2"));
    }

    [Fact]
    public void Search_ScoresAreNormalized()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { -1.0f, 0.0f, 0.0f, 0.0f }); // Opposite direction

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _service.Search("test-index", "embedding", queryVector, k: 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score >= 0 && results[0].Score <= 1);
        Assert.True(results[1].Score >= 0 && results[1].Score <= 1);
    }
}

/// <summary>
/// Tests for HnswVectorSearchService with HNSW disabled (brute-force mode).
/// </summary>
public class BruteForceVectorSearchServiceTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly HnswVectorSearchService _service;
    private readonly HnswIndexManager _hnswManager;
    private readonly VectorStore _vectorStore;

    public BruteForceVectorSearchServiceTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"brute-force-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);

        // Disable HNSW to test brute-force fallback
        var vectorSettings = new VectorSearchSettings
        {
            DefaultDimensions = 4,
            MaxVectorsPerIndex = 1000,
            SimilarityMetric = "cosine",
            UseHnsw = false, // Disabled!
            HnswSettings = new HnswSettings(),
            HybridSearchSettings = new HybridSearchSettings()
        };

        var luceneSettings = new LuceneSettings { IndexPath = _testDataPath };

        _vectorStore = new VectorStore();

        _hnswManager = new HnswIndexManager(
            Mock.Of<ILogger<HnswIndexManager>>(),
            Options.Create(vectorSettings),
            Options.Create(luceneSettings));

        _service = new HnswVectorSearchService(
            Mock.Of<ILogger<HnswVectorSearchService>>(),
            Options.Create(vectorSettings),
            _hnswManager,
            _vectorStore);
    }

    public void Dispose()
    {
        _service.Dispose();
        _hnswManager.Dispose();

        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void Search_WithHnswDisabled_UsessBruteForce()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { 0.9f, 0.1f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc3", new[] { 0.0f, 1.0f, 0.0f, 0.0f });

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _service.Search("test-index", "embedding", queryVector, k: 3);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("doc1", results[0].DocumentId); // Exact match should be first
    }

    [Fact]
    public void SearchWithFilter_WithHnswDisabled_UsesBruteForce()
    {
        // Arrange
        _service.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc2", new[] { 0.9f, 0.1f, 0.0f, 0.0f });
        _service.AddVector("test-index", "embedding", "doc3", new[] { 0.8f, 0.2f, 0.0f, 0.0f });

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var candidates = new HashSet<string> { "doc2", "doc3" };

        // Act
        var results = _service.SearchWithFilter("test-index", "embedding", queryVector, k: 2, candidates);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.DocumentId == "doc1");
    }

    [Fact]
    public void IndexExists_WithHnswDisabled_UsesFieldTracking()
    {
        // Arrange
        _service.InitializeIndex("test-index", "embedding", dimensions: 4);

        // Act & Assert
        Assert.True(_service.IndexExists("test-index", "embedding"));
        Assert.False(_service.IndexExists("test-index", "nonexistent"));
    }
}
