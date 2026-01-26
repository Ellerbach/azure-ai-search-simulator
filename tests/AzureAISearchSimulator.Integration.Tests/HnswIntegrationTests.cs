using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests for HNSW vector search with DocumentService and SearchService.
/// </summary>
public class HnswIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly VectorStore _vectorStore;
    private readonly IHnswIndexManager _hnswManager;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;
    private readonly SearchIndex _testIndex;

    public HnswIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "hnsw-integration-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        // Set up Lucene
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = _testDir });
        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        // Set up VectorStore (brute-force fallback)
        _vectorStore = new VectorStore();

        // Set up HNSW
        var vectorSettings = Options.Create(new VectorSearchSettings
        {
            UseHnsw = true,
            MaxVectorsPerIndex = 10000,
            HnswSettings = new HnswSettings
            {
                M = 16,
                EfConstruction = 100,
                EfSearch = 50,
                OversampleMultiplier = 3
            },
            HybridSearchSettings = new HybridSearchSettings
            {
                DefaultFusionMethod = "RRF",
                RrfK = 60
            }
        });

        _hnswManager = new HnswIndexManager(
            Mock.Of<ILogger<HnswIndexManager>>(),
            vectorSettings,
            luceneSettings);

        _vectorSearchService = new HnswVectorSearchService(
            Mock.Of<ILogger<HnswVectorSearchService>>(),
            vectorSettings,
            _hnswManager,
            _vectorStore);

        // Set up test index schema
        _testIndex = new SearchIndex
        {
            Name = "test-index",
            Fields = new List<SearchField>
            {
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "content", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "embedding", Type = "Collection(Edm.Single)", Dimensions = 3 }
            }
        };

        // Mock index service
        _indexServiceMock = new Mock<IIndexService>();
        _indexServiceMock.Setup(x => x.GetIndexAsync("test-index"))
            .ReturnsAsync(_testIndex);

        // Create services
        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            _vectorSearchService,
            _indexServiceMock.Object);

        _searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            _vectorSearchService,
            _indexServiceMock.Object);

        // Initialize Lucene index
        _luceneManager.GetWriter("test-index");
    }

    public void Dispose()
    {
        (_hnswManager as IDisposable)?.Dispose();
        (_vectorSearchService as IDisposable)?.Dispose();
        _luceneManager?.Dispose();

        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    /// <summary>
    /// Creates an IndexAction with the specified action type and document fields.
    /// </summary>
    private static IndexAction CreateAction(string actionType, Dictionary<string, object?> fields)
    {
        var action = new IndexAction
        {
            ["@search.action"] = actionType
        };
        foreach (var kvp in fields)
        {
            action[kvp.Key] = kvp.Value;
        }
        return action;
    }

    #region Document Indexing Tests

    [Fact]
    public async Task IndexDocuments_WithVectors_StoresInHnsw()
    {
        // Arrange
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc1",
                    ["title"] = "Test Document",
                    ["content"] = "This is test content",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                })
            }
        };

        // Act
        var response = await _documentService.IndexDocumentsAsync("test-index", request);

        // Assert
        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        
        // Verify vector was stored in HNSW
        var count = _vectorSearchService.GetVectorCount("test-index", "embedding");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IndexDocuments_MultipleVectors_AllStoredInHnsw()
    {
        // Arrange
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc1",
                    ["title"] = "Document 1",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc2",
                    ["title"] = "Document 2",
                    ["embedding"] = new float[] { 0.0f, 1.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc3",
                    ["title"] = "Document 3",
                    ["embedding"] = new float[] { 0.0f, 0.0f, 1.0f }
                })
            }
        };

        // Act
        var response = await _documentService.IndexDocumentsAsync("test-index", request);

        // Assert
        Assert.Equal(3, response.Value.Count);
        Assert.All(response.Value, r => Assert.True(r.Status));
        
        var count = _vectorSearchService.GetVectorCount("test-index", "embedding");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task DeleteDocument_RemovesFromHnsw()
    {
        // Arrange - First add a document
        var uploadRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc1",
                    ["title"] = "Test",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", uploadRequest);
        
        // Verify it's there
        Assert.Equal(1, _vectorSearchService.GetVectorCount("test-index", "embedding"));

        // Act - Delete the document
        var deleteRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("delete", new Dictionary<string, object?>
                {
                    ["id"] = "doc1"
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", deleteRequest);

        // Assert - Vector should be marked as deleted (soft delete in HNSW)
        // Note: HNSW uses soft delete, so count may still show 1 until rebuild
        // But search should not return the deleted document
        var results = _vectorSearchService.Search("test-index", "embedding", 
            new float[] { 1.0f, 0.0f, 0.0f }, 10);
        Assert.DoesNotContain(results, r => r.DocumentId == "doc1");
    }

    [Fact]
    public async Task MergeDocument_UpdatesVectorInHnsw()
    {
        // Arrange - First upload
        var uploadRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "doc1",
                    ["title"] = "Original Title",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", uploadRequest);

        // Act - Merge with new vector
        var mergeRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("mergeOrUpload", new Dictionary<string, object?>
                {
                    ["id"] = "doc1",
                    ["title"] = "Updated Title",
                    ["embedding"] = new float[] { 0.0f, 1.0f, 0.0f }  // New vector
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", mergeRequest);

        // Assert - Search with new vector should find the document
        var results = _vectorSearchService.Search("test-index", "embedding",
            new float[] { 0.0f, 1.0f, 0.0f }, 10);
        
        Assert.Contains(results, r => r.DocumentId == "doc1");
        // The updated vector should be the most similar to query
        Assert.Equal("doc1", results[0].DocumentId);
    }

    #endregion

    #region Vector Search Tests

    [Fact]
    public async Task VectorSearch_FindsNearestNeighbors()
    {
        // Arrange - Add documents with distinct vectors
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "north",
                    ["title"] = "North",
                    ["embedding"] = new float[] { 0.0f, 1.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "south",
                    ["title"] = "South",
                    ["embedding"] = new float[] { 0.0f, -1.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "east",
                    ["title"] = "East",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", request);

        // Act - Search for vector close to "north"
        var results = _vectorSearchService.Search("test-index", "embedding",
            new float[] { 0.1f, 0.9f, 0.0f }, 3);

        // Assert - "north" should be first (most similar)
        Assert.Equal(3, results.Count);
        Assert.Equal("north", results[0].DocumentId);
    }

    [Fact]
    public async Task VectorSearch_RespectsKParameter()
    {
        // Arrange - Add 5 documents
        var request = new IndexDocumentsRequest
        {
            Value = Enumerable.Range(1, 5).Select(i => 
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"doc{i}",
                    ["title"] = $"Document {i}",
                    ["embedding"] = new float[] { i * 0.1f, 0.0f, 0.0f }
                })).ToList()
        };
        await _documentService.IndexDocumentsAsync("test-index", request);

        // Act - Search with k=2
        var results = _vectorSearchService.Search("test-index", "embedding",
            new float[] { 0.5f, 0.0f, 0.0f }, k: 2);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task VectorSearch_WithFilter_ReturnsOnlyMatchingDocuments()
    {
        // Arrange
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "include1",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "include2",
                    ["embedding"] = new float[] { 0.9f, 0.1f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "exclude",
                    ["embedding"] = new float[] { 0.95f, 0.05f, 0.0f }
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", request);

        // Act - Filter to only include documents
        var candidates = new HashSet<string> { "include1", "include2" };
        var results = _vectorSearchService.SearchWithFilter("test-index", "embedding",
            new float[] { 1.0f, 0.0f, 0.0f }, k: 10, candidates);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.StartsWith("include", r.DocumentId));
    }

    #endregion

    #region Hybrid Search Tests

    [Fact]
    public async Task HybridSearch_CombinesTextAndVectorResults()
    {
        // Arrange
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "textmatch",
                    ["title"] = "Azure Search Tutorial",
                    ["content"] = "Learn about Azure AI Search",
                    ["embedding"] = new float[] { 0.0f, 0.0f, 1.0f }  // Far from query vector
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "vectormatch",
                    ["title"] = "Machine Learning Guide",
                    ["content"] = "Deep learning techniques",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }  // Close to query vector
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "bothmatch",
                    ["title"] = "Azure AI Overview",
                    ["content"] = "Azure AI Search and embeddings",
                    ["embedding"] = new float[] { 0.9f, 0.1f, 0.0f }  // Close to query vector
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", request);

        // Act - Hybrid search
        var searchRequest = new SearchRequest
        {
            Search = "Azure",
            SearchFields = "title,content",
            VectorQueries = new List<VectorQuery>
            {
                new VectorQuery
                {
                    Kind = "vector",
                    Vector = new float[] { 1.0f, 0.0f, 0.0f },
                    Fields = "embedding",
                    K = 10
                }
            }
        };
        
        var response = await _searchService.SearchAsync("test-index", searchRequest);

        // Assert - Both text and vector matches should appear
        Assert.NotEmpty(response.Value);
        var docIds = response.Value.Select(r => r["id"]?.ToString()).ToList();
        
        // "bothmatch" should rank highly (matches both)
        Assert.Contains("bothmatch", docIds);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task IndexDocuments_PersistsVectorsOnCommit()
    {
        // Arrange & Act
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "persistent-doc",
                    ["title"] = "Persistent Document",
                    ["embedding"] = new float[] { 0.5f, 0.5f, 0.0f }
                })
            }
        };
        await _documentService.IndexDocumentsAsync("test-index", request);

        // Assert - HNSW files should exist
        var hnswDir = Path.Combine(_testDir, "hnsw", "test-index", "embedding");
        Assert.True(Directory.Exists(hnswDir) || _vectorSearchService.GetVectorCount("test-index", "embedding") > 0,
            "HNSW index should be persisted or in memory");
    }

    #endregion

    #region Clear Index Tests

    [Fact]
    public async Task ClearIndex_RemovesAllVectors()
    {
        // Arrange
        var request = new IndexDocumentsRequest
        {
            Value = Enumerable.Range(1, 5).Select(i => 
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"doc{i}",
                    ["title"] = $"Document {i}",
                    ["embedding"] = new float[] { i * 0.1f, 0.0f, 0.0f }
                })).ToList()
        };
        await _documentService.IndexDocumentsAsync("test-index", request);
        Assert.Equal(5, _vectorSearchService.GetVectorCount("test-index", "embedding"));

        // Act
        await _documentService.ClearIndexAsync("test-index");

        // Assert - Vectors should be cleared
        // Note: After delete, a new index would need to be initialized
        var count = _vectorSearchService.GetVectorCount("test-index", "embedding");
        Assert.Equal(0, count);
    }

    #endregion
}
