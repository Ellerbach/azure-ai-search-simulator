using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Search.Hnsw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for the HNSW index manager.
/// </summary>
public class HnswIndexManagerTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly HnswIndexManager _manager;
    private readonly Mock<ILogger<HnswIndexManager>> _loggerMock;

    public HnswIndexManagerTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"hnsw-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);

        _loggerMock = new Mock<ILogger<HnswIndexManager>>();

        var vectorSettings = Options.Create(new VectorSearchSettings
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
            }
        });

        var luceneSettings = Options.Create(new LuceneSettings
        {
            IndexPath = _testDataPath
        });

        _manager = new HnswIndexManager(_loggerMock.Object, vectorSettings, luceneSettings);
    }

    public void Dispose()
    {
        _manager.Dispose();
        
        // Clean up test directory
        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public void CreateOrOpenIndex_ShouldCreateNewIndex()
    {
        // Act
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);

        // Assert
        Assert.True(_manager.IndexExists("test-index", "embedding"));
    }

    [Fact]
    public void CreateOrOpenIndex_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);

        // Act - Should not throw when called again
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);

        // Assert
        Assert.True(_manager.IndexExists("test-index", "embedding"));
    }

    [Fact]
    public void IndexExists_WhenNotCreated_ShouldReturnFalse()
    {
        // Act & Assert
        Assert.False(_manager.IndexExists("nonexistent", "field"));
    }

    [Fact]
    public void AddVector_ShouldAddVectorToIndex()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        _manager.AddVector("test-index", "embedding", "doc1", vector);

        // Assert
        Assert.Equal(1, _manager.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void AddVector_WithoutIndex_ShouldThrow()
    {
        // Arrange
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _manager.AddVector("nonexistent", "embedding", "doc1", vector));
    }

    [Fact]
    public void AddVectors_ShouldAddMultipleVectors()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        var documents = new List<(string DocumentId, float[] Vector)>
        {
            ("doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f }),
            ("doc2", new[] { 0.5f, 0.6f, 0.7f, 0.8f }),
            ("doc3", new[] { 0.9f, 0.8f, 0.7f, 0.6f })
        };

        // Act
        _manager.AddVectors("test-index", "embedding", documents);

        // Assert
        Assert.Equal(3, _manager.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void AddVector_UpdateExisting_ShouldReplaceVector()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        var vector1 = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var vector2 = new[] { 0.9f, 0.8f, 0.7f, 0.6f };

        // Act
        _manager.AddVector("test-index", "embedding", "doc1", vector1);
        _manager.AddVector("test-index", "embedding", "doc1", vector2); // Update

        // Assert - Count should still be 1
        Assert.Equal(1, _manager.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void RemoveVector_ShouldMarkVectorAsDeleted()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("test-index", "embedding", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _manager.AddVector("test-index", "embedding", "doc2", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act
        _manager.RemoveVector("test-index", "embedding", "doc1");

        // Assert
        Assert.Equal(1, _manager.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void RemoveVector_NonexistentDocument_ShouldNotThrow()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);

        // Act - Should not throw
        _manager.RemoveVector("test-index", "embedding", "nonexistent");

        // Assert
        Assert.Equal(0, _manager.GetVectorCount("test-index", "embedding"));
    }

    [Fact]
    public void Search_ShouldReturnNearestNeighbors()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("test-index", "embedding", "doc1", new[] { 1.0f, 0.0f, 0.0f, 0.0f });
        _manager.AddVector("test-index", "embedding", "doc2", new[] { 0.9f, 0.1f, 0.0f, 0.0f });
        _manager.AddVector("test-index", "embedding", "doc3", new[] { 0.0f, 1.0f, 0.0f, 0.0f });

        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _manager.Search("test-index", "embedding", queryVector, k: 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("doc1", results[0].DocumentId); // Exact match should be first
    }

    [Fact]
    public void Search_EmptyIndex_ShouldReturnEmptyList()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _manager.Search("test-index", "embedding", queryVector, k: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_NonexistentIndex_ShouldReturnEmptyList()
    {
        // Arrange
        var queryVector = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = _manager.Search("nonexistent", "embedding", queryVector, k: 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void SearchWithOversampling_ShouldReturnMoreCandidates()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        for (int i = 0; i < 20; i++)
        {
            var vec = new[] { (float)i / 20f, 1f - (float)i / 20f, 0f, 0f };
            _manager.AddVector("test-index", "embedding", $"doc{i}", vec);
        }

        var queryVector = new[] { 0.5f, 0.5f, 0.0f, 0.0f };

        // Act
        var results = _manager.SearchWithOversampling("test-index", "embedding", queryVector, k: 3, oversampleMultiplier: 3);

        // Assert
        Assert.True(results.Count <= 9); // At most k * oversampleMultiplier
        Assert.True(results.Count >= 3); // At least k results
    }

    [Fact]
    public void GetLabel_ShouldReturnLabelForDocument()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("test-index", "embedding", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });

        // Act
        var label = _manager.GetLabel("test-index", "embedding", "doc1");

        // Assert
        Assert.NotNull(label);
    }

    [Fact]
    public void GetLabel_NonexistentDocument_ShouldReturnNull()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);

        // Act
        var label = _manager.GetLabel("test-index", "embedding", "nonexistent");

        // Assert
        Assert.Null(label);
    }

    [Fact]
    public void GetDocumentId_ShouldReturnDocumentForLabel()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("test-index", "embedding", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        var label = _manager.GetLabel("test-index", "embedding", "doc1");

        // Act
        var documentId = _manager.GetDocumentId("test-index", "embedding", label!.Value);

        // Assert
        Assert.Equal("doc1", documentId);
    }

    [Fact]
    public void DeleteIndex_SpecificField_ShouldRemoveOnlyThatField()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding1", dimensions: 4, maxElements: 100);
        _manager.CreateOrOpenIndex("test-index", "embedding2", dimensions: 4, maxElements: 100);

        // Act
        _manager.DeleteIndex("test-index", "embedding1");

        // Assert
        Assert.False(_manager.IndexExists("test-index", "embedding1"));
        Assert.True(_manager.IndexExists("test-index", "embedding2"));
    }

    [Fact]
    public void DeleteIndex_AllFields_ShouldRemoveAllFieldsForIndex()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding1", dimensions: 4, maxElements: 100);
        _manager.CreateOrOpenIndex("test-index", "embedding2", dimensions: 4, maxElements: 100);

        // Act
        _manager.DeleteIndex("test-index");

        // Assert
        Assert.False(_manager.IndexExists("test-index", "embedding1"));
        Assert.False(_manager.IndexExists("test-index", "embedding2"));
    }

    [Fact]
    public void RebuildIndex_ShouldRecreateWithNewVectors()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("test-index", "embedding", "old-doc", new[] { 0.1f, 0.2f, 0.3f, 0.4f });

        var newDocuments = new List<(string DocumentId, float[] Vector)>
        {
            ("new-doc1", new[] { 0.5f, 0.5f, 0.0f, 0.0f }),
            ("new-doc2", new[] { 0.0f, 0.5f, 0.5f, 0.0f })
        };

        // Act
        _manager.RebuildIndex("test-index", "embedding", newDocuments);

        // Assert
        Assert.Equal(2, _manager.GetVectorCount("test-index", "embedding"));
        Assert.Null(_manager.GetLabel("test-index", "embedding", "old-doc"));
        Assert.NotNull(_manager.GetLabel("test-index", "embedding", "new-doc1"));
    }

    [Fact]
    public void MultipleVectorFields_ShouldBeIndependent()
    {
        // Arrange
        _manager.CreateOrOpenIndex("test-index", "titleVector", dimensions: 4, maxElements: 100);
        _manager.CreateOrOpenIndex("test-index", "contentVector", dimensions: 4, maxElements: 100);

        // Act
        _manager.AddVector("test-index", "titleVector", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _manager.AddVector("test-index", "contentVector", "doc1", new[] { 0.5f, 0.6f, 0.7f, 0.8f });
        _manager.AddVector("test-index", "contentVector", "doc2", new[] { 0.9f, 0.8f, 0.7f, 0.6f });

        // Assert
        Assert.Equal(1, _manager.GetVectorCount("test-index", "titleVector"));
        Assert.Equal(2, _manager.GetVectorCount("test-index", "contentVector"));
    }

    [Fact]
    public void SaveAndLoad_ShouldPersistIndexToDisk()
    {
        // Arrange
        _manager.CreateOrOpenIndex("persist-test", "embedding", dimensions: 4, maxElements: 100);
        _manager.AddVector("persist-test", "embedding", "doc1", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        _manager.AddVector("persist-test", "embedding", "doc2", new[] { 0.5f, 0.6f, 0.7f, 0.8f });

        // Act - Save
        _manager.SaveIndex("persist-test", "embedding");

        // Verify files were created
        var indexPath = Path.Combine(_testDataPath, "hnsw", "persist-test", "embedding.hnsw");
        var mappingPath = Path.Combine(_testDataPath, "hnsw", "persist-test", "embedding.mapping");

        // Assert
        Assert.True(File.Exists(indexPath), $"Index file should exist at {indexPath}");
        Assert.True(File.Exists(mappingPath), $"Mapping file should exist at {mappingPath}");
    }
}
