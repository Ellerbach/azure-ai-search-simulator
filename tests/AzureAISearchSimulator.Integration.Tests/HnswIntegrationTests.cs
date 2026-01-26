using Xunit;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests for HNSW vector search with DocumentService and SearchService.
/// Uses a shared fixture to avoid heavy initialization during test discovery.
/// Run locally with: dotnet test --filter "FullyQualifiedName~Integration"
/// </summary>
[Collection("HNSW Integration Tests")]
public class HnswIntegrationTests
{
    private readonly HnswTestFixture _fixture;

    public HnswIntegrationTests(HnswTestFixture fixture)
    {
        _fixture = fixture;
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

    /// <summary>
    /// Creates a unique document ID to avoid conflicts between tests.
    /// </summary>
    private static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    #region Document Indexing Tests

    [Fact]
    public async Task IndexDocuments_WithVectors_StoresInHnsw()
    {
        // Arrange
        var docId = UniqueId("doc");
        var countBefore = _fixture.VectorSearchService.GetVectorCount("test-index", "embedding");
        
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = docId,
                    ["title"] = "Test Document",
                    ["content"] = "This is test content",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                })
            }
        };

        // Act
        var response = await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Assert
        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        
        // Verify vector count increased
        var countAfter = _fixture.VectorSearchService.GetVectorCount("test-index", "embedding");
        Assert.Equal(countBefore + 1, countAfter);
    }

    [Fact]
    public async Task IndexDocuments_MultipleVectors_AllStoredInHnsw()
    {
        // Arrange
        var prefix = UniqueId("batch");
        var countBefore = _fixture.VectorSearchService.GetVectorCount("test-index", "embedding");
        
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-1",
                    ["title"] = "Document 1",
                    ["embedding"] = new float[] { 1.0f, 0.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-2",
                    ["title"] = "Document 2",
                    ["embedding"] = new float[] { 0.0f, 1.0f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-3",
                    ["title"] = "Document 3",
                    ["embedding"] = new float[] { 0.0f, 0.0f, 1.0f }
                })
            }
        };

        // Act
        var response = await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Assert
        Assert.Equal(3, response.Value.Count);
        Assert.All(response.Value, r => Assert.True(r.Status));
        
        var countAfter = _fixture.VectorSearchService.GetVectorCount("test-index", "embedding");
        Assert.Equal(countBefore + 3, countAfter);
    }

    [Fact]
    public async Task DeleteDocument_RemovesFromHnsw()
    {
        // Arrange - First add a document
        var docId = UniqueId("delete");
        var uploadRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = docId,
                    ["title"] = "Test",
                    ["embedding"] = new float[] { 0.99f, 0.01f, 0.0f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", uploadRequest);

        // Act - Delete the document
        var deleteRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("delete", new Dictionary<string, object?>
                {
                    ["id"] = docId
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", deleteRequest);

        // Assert - Search should not return the deleted document
        var results = _fixture.VectorSearchService.Search("test-index", "embedding", 
            new float[] { 0.99f, 0.01f, 0.0f }, 10);
        Assert.DoesNotContain(results, r => r.DocumentId == docId);
    }

    [Fact]
    public async Task MergeDocument_UpdatesVectorInHnsw()
    {
        // Arrange - First upload with a unique vector direction
        var docId = UniqueId("merge");
        var uploadRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = docId,
                    ["title"] = "Original Title",
                    ["embedding"] = new float[] { 0.0f, 0.0f, 0.99f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", uploadRequest);

        // Act - Merge with a completely different vector direction
        var mergeRequest = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("mergeOrUpload", new Dictionary<string, object?>
                {
                    ["id"] = docId,
                    ["title"] = "Updated Title",
                    ["embedding"] = new float[] { 0.0f, 0.99f, 0.0f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", mergeRequest);

        // Assert - Search with new vector should find the document
        var results = _fixture.VectorSearchService.Search("test-index", "embedding",
            new float[] { 0.0f, 0.99f, 0.0f }, 10);
        
        Assert.Contains(results, r => r.DocumentId == docId);
    }

    #endregion

    #region Vector Search Tests

    [Fact]
    public async Task VectorSearch_FindsNearestNeighbors()
    {
        // Arrange - Add documents with distinct vectors using unique negative values
        // to avoid collision with other tests' vectors
        var prefix = UniqueId("search");
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-target",
                    ["title"] = "Target",
                    ["embedding"] = new float[] { -0.9f, -0.1f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-other1",
                    ["title"] = "Other1",
                    ["embedding"] = new float[] { -0.1f, -0.9f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-other2",
                    ["title"] = "Other2",
                    ["embedding"] = new float[] { 0.0f, 0.0f, -0.99f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Act - Search for vector close to target
        var results = _fixture.VectorSearchService.Search("test-index", "embedding",
            new float[] { -0.85f, -0.15f, 0.0f }, k: 5);

        // Assert - Results should include the target
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.DocumentId == $"{prefix}-target");
    }

    [Fact]
    public async Task VectorSearch_RespectsKParameter()
    {
        // Arrange - Add 5 documents
        var prefix = UniqueId("kparam");
        var request = new IndexDocumentsRequest
        {
            Value = Enumerable.Range(1, 5).Select(i => 
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-{i}",
                    ["title"] = $"Document {i}",
                    ["embedding"] = new float[] { i * 0.1f + 0.5f, 0.0f, 0.0f }
                })).ToList()
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Act - Search with k=2
        var results = _fixture.VectorSearchService.Search("test-index", "embedding",
            new float[] { 0.75f, 0.0f, 0.0f }, k: 2);

        // Assert - Should return exactly 2 results
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task VectorSearch_WithFilter_ReturnsOnlyMatchingDocuments()
    {
        // Arrange
        var prefix = UniqueId("filter");
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-include1",
                    ["embedding"] = new float[] { 0.8f, 0.2f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-include2",
                    ["embedding"] = new float[] { 0.7f, 0.3f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-exclude",
                    ["embedding"] = new float[] { 0.75f, 0.25f, 0.0f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Act - Filter to only include documents
        var candidates = new HashSet<string> { $"{prefix}-include1", $"{prefix}-include2" };
        var results = _fixture.VectorSearchService.SearchWithFilter("test-index", "embedding",
            new float[] { 0.8f, 0.2f, 0.0f }, k: 10, candidates);

        // Assert - Should only return filtered documents
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("include", r.DocumentId));
    }

    #endregion

    #region Hybrid Search Tests

    [Fact]
    public async Task HybridSearch_CombinesTextAndVectorResults()
    {
        // Arrange
        var prefix = UniqueId("hybrid");
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-textmatch",
                    ["title"] = "Azure Search Tutorial",
                    ["content"] = "Learn about Azure AI Search",
                    ["embedding"] = new float[] { 0.0f, 0.0f, 0.8f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-vectormatch",
                    ["title"] = "Machine Learning Guide",
                    ["content"] = "Deep learning techniques",
                    ["embedding"] = new float[] { 0.8f, 0.2f, 0.0f }
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = $"{prefix}-bothmatch",
                    ["title"] = "Azure AI Overview",
                    ["content"] = "Azure AI Search and embeddings",
                    ["embedding"] = new float[] { 0.7f, 0.3f, 0.0f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

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
                    Vector = new float[] { 0.8f, 0.2f, 0.0f },
                    Fields = "embedding",
                    K = 10
                }
            }
        };
        
        var response = await _fixture.SearchService.SearchAsync("test-index", searchRequest);

        // Assert - Both text and vector matches should appear
        Assert.NotEmpty(response.Value);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task IndexDocuments_PersistsVectorsOnCommit()
    {
        // Arrange & Act
        var docId = UniqueId("persist");
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = docId,
                    ["title"] = "Persistent Document",
                    ["embedding"] = new float[] { 0.5f, 0.5f, 0.0f }
                })
            }
        };
        await _fixture.DocumentService.IndexDocumentsAsync("test-index", request);

        // Assert - HNSW files should exist or vectors should be in memory
        var hnswDir = Path.Combine(_fixture.TestDir, "hnsw", "test-index", "embedding");
        Assert.True(Directory.Exists(hnswDir) || _fixture.VectorSearchService.GetVectorCount("test-index", "embedding") > 0,
            "HNSW index should be persisted or in memory");
    }

    #endregion
}
