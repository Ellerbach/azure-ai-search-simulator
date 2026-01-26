using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Search.Hnsw;

namespace AzureAISearchSimulator.Core.Tests;

public class HybridSearchServiceTests
{
    private readonly Mock<ILogger<HybridSearchService>> _loggerMock;
    private readonly IOptions<VectorSearchSettings> _defaultOptions;

    public HybridSearchServiceTests()
    {
        _loggerMock = new Mock<ILogger<HybridSearchService>>();
        _defaultOptions = Options.Create(new VectorSearchSettings
        {
            HybridSearchSettings = new HybridSearchSettings
            {
                DefaultFusionMethod = "RRF",
                DefaultVectorWeight = 0.7,
                DefaultTextWeight = 0.3,
                RrfK = 60
            }
        });
    }

    private HybridSearchService CreateService()
    {
        return new HybridSearchService(_loggerMock.Object, _defaultOptions);
    }

    #region RRF Tests

    [Fact]
    public void FuseWithRRF_EmptyResults_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Empty<(string DocumentId, double Score)>();
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithRRF_OnlyTextResults_ReturnsRRFScores()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0),
            ("doc2", 8.0),
            ("doc3", 5.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults, k: 60);

        // Assert
        Assert.Equal(3, results.Count);
        
        // First result should have highest RRF score (1/(60+1) = 0.01639...)
        Assert.Equal("doc1", results[0].DocumentId);
        Assert.Equal(1.0 / 61, results[0].Score, 6);
        Assert.Equal(10.0, results[0].TextScore);
        Assert.Equal(0, results[0].VectorScore);
        Assert.Equal(1, results[0].TextRank);
        Assert.Equal(0, results[0].VectorRank);
    }

    [Fact]
    public void FuseWithRRF_OnlyVectorResults_ReturnsRRFScores()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Empty<(string DocumentId, double Score)>();
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.95 },
            new VectorSearchResult { DocumentId = "doc2", Score = 0.85 },
            new VectorSearchResult { DocumentId = "doc3", Score = 0.75 }
        };

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults, k: 60);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("doc1", results[0].DocumentId);
        Assert.Equal(1.0 / 61, results[0].Score, 6);
        Assert.Equal(0, results[0].TextScore);
        Assert.Equal(0.95, results[0].VectorScore);
        Assert.Equal(0, results[0].TextRank);
        Assert.Equal(1, results[0].VectorRank);
    }

    [Fact]
    public void FuseWithRRF_OverlappingResults_CombinesScores()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0), // Rank 1 in text
            ("doc2", 8.0),  // Rank 2 in text
            ("doc3", 5.0)   // Rank 3 in text
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc2", Score = 0.95 }, // Rank 1 in vector
            new VectorSearchResult { DocumentId = "doc1", Score = 0.85 }, // Rank 2 in vector
            new VectorSearchResult { DocumentId = "doc4", Score = 0.75 }  // Rank 3 in vector, not in text
        };

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults, k: 60);

        // Assert
        Assert.Equal(4, results.Count);
        
        // doc1: text rank 1 + vector rank 2 = 1/61 + 1/62
        var doc1 = results.First(r => r.DocumentId == "doc1");
        Assert.Equal(1.0 / 61 + 1.0 / 62, doc1.Score, 6);
        Assert.Equal(1, doc1.TextRank);
        Assert.Equal(2, doc1.VectorRank);

        // doc2: text rank 2 + vector rank 1 = 1/62 + 1/61
        var doc2 = results.First(r => r.DocumentId == "doc2");
        Assert.Equal(1.0 / 62 + 1.0 / 61, doc2.Score, 6);
        Assert.Equal(2, doc2.TextRank);
        Assert.Equal(1, doc2.VectorRank);

        // doc1 and doc2 should have same RRF score and be at top
        Assert.Equal(doc1.Score, doc2.Score, 6);
    }

    [Fact]
    public void FuseWithRRF_DocumentInBothSets_HasHigherScoreThanSingleSet()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("shared", 10.0),
            ("text-only", 8.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "shared", Score = 0.95 },
            new VectorSearchResult { DocumentId = "vector-only", Score = 0.85 }
        };

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults, k: 60);

        // Assert
        var sharedDoc = results.First(r => r.DocumentId == "shared");
        var textOnlyDoc = results.First(r => r.DocumentId == "text-only");
        var vectorOnlyDoc = results.First(r => r.DocumentId == "vector-only");

        // Shared doc should have higher score
        Assert.True(sharedDoc.Score > textOnlyDoc.Score);
        Assert.True(sharedDoc.Score > vectorOnlyDoc.Score);
    }

    [Fact]
    public void FuseWithRRF_DifferentKValues_AffectsScoreDistribution()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0),
            ("doc2", 8.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var resultsK10 = service.FuseWithRRF(textResults, vectorResults, k: 10);
        var resultsK60 = service.FuseWithRRF(textResults, vectorResults, k: 60);

        // Assert
        // With smaller k, the difference between ranks is more pronounced
        var diffK10 = resultsK10[0].Score - resultsK10[1].Score;
        var diffK60 = resultsK60[0].Score - resultsK60[1].Score;
        
        Assert.True(diffK10 > diffK60);
    }

    [Fact]
    public void FuseWithRRF_TopK_LimitsResults()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Range(1, 100)
            .Select(i => ($"doc{i}", (double)(100 - i)))
            .ToList();
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults, topK: 10);

        // Assert
        Assert.Equal(10, results.Count);
        Assert.Equal("doc1", results[0].DocumentId);
    }

    [Fact]
    public void FuseWithRRF_PreservesOriginalScores()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 42.5)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.876 }
        };

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults);

        // Assert
        var doc = results[0];
        Assert.Equal(42.5, doc.TextScore);
        Assert.Equal(0.876, doc.VectorScore);
    }

    #endregion

    #region Weighted Fusion Tests

    [Fact]
    public void FuseWithWeightedScores_EmptyResults_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Empty<(string DocumentId, double Score)>();
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithWeightedScores_OnlyTextResults_UsesTextWeight()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 100.0),
            ("doc2", 50.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.7, textWeight: 0.3);

        // Assert
        Assert.Equal(2, results.Count);
        
        // doc1 normalized: (100-50)/(100-50) = 1.0, weighted: 1.0 * 0.3 = 0.3
        Assert.Equal("doc1", results[0].DocumentId);
        Assert.Equal(0.3, results[0].Score, 6);

        // doc2 normalized: (50-50)/(100-50) = 0.0, weighted: 0.0 * 0.3 = 0.0
        Assert.Equal("doc2", results[1].DocumentId);
        Assert.Equal(0.0, results[1].Score, 6);
    }

    [Fact]
    public void FuseWithWeightedScores_OnlyVectorResults_UsesVectorWeight()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Empty<(string DocumentId, double Score)>();
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.9 },
            new VectorSearchResult { DocumentId = "doc2", Score = 0.6 }
        };

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.7, textWeight: 0.3);

        // Assert
        Assert.Equal(2, results.Count);
        
        // Vector scores are already normalized, so: 0.9 * 0.7 = 0.63
        Assert.Equal("doc1", results[0].DocumentId);
        Assert.Equal(0.63, results[0].Score, 6);
    }

    [Fact]
    public void FuseWithWeightedScores_CombinedResults_WeightsApplied()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 100.0), // Normalized to 1.0
            ("doc2", 0.0)    // Normalized to 0.0
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.5 },
            new VectorSearchResult { DocumentId = "doc2", Score = 1.0 }
        };

        // Act - 50/50 weighting
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.5, textWeight: 0.5);

        // Assert
        // doc1: (1.0 * 0.5) + (0.5 * 0.5) = 0.5 + 0.25 = 0.75
        var doc1 = results.First(r => r.DocumentId == "doc1");
        Assert.Equal(0.75, doc1.Score, 6);

        // doc2: (0.0 * 0.5) + (1.0 * 0.5) = 0 + 0.5 = 0.5
        var doc2 = results.First(r => r.DocumentId == "doc2");
        Assert.Equal(0.5, doc2.Score, 6);
    }

    [Fact]
    public void FuseWithWeightedScores_HighVectorWeight_FavorsVectorResults()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("text-winner", 100.0),
            ("vector-winner", 50.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "vector-winner", Score = 1.0 },
            new VectorSearchResult { DocumentId = "text-winner", Score = 0.2 }
        };

        // Act - High vector weight
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.9, textWeight: 0.1);

        // Assert
        // vector-winner should be first due to high vector weight
        Assert.Equal("vector-winner", results[0].DocumentId);
    }

    [Fact]
    public void FuseWithWeightedScores_HighTextWeight_FavorsTextResults()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("text-winner", 100.0),
            ("vector-winner", 50.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "vector-winner", Score = 1.0 },
            new VectorSearchResult { DocumentId = "text-winner", Score = 0.2 }
        };

        // Act - High text weight
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.1, textWeight: 0.9);

        // Assert
        // text-winner should be first due to high text weight
        Assert.Equal("text-winner", results[0].DocumentId);
    }

    [Fact]
    public void FuseWithWeightedScores_AllSameTextScores_NormalizesToOne()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 50.0),
            ("doc2", 50.0),
            ("doc3", 50.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.0, textWeight: 1.0);

        // Assert - All should be normalized to 1.0 when scores are identical
        foreach (var result in results)
        {
            Assert.Equal(1.0, result.Score, 6);
        }
    }

    [Fact]
    public void FuseWithWeightedScores_TopK_LimitsResults()
    {
        // Arrange
        var service = CreateService();
        var textResults = Enumerable.Range(1, 100)
            .Select(i => ($"doc{i}", (double)(100 - i)))
            .ToList();
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, topK: 5);

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void FuseWithWeightedScores_PreservesOriginalScores()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 42.5)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.876 }
        };

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults);

        // Assert
        var doc = results[0];
        Assert.Equal(42.5, doc.TextScore);
        Assert.Equal(0.876, doc.VectorScore);
    }

    [Fact]
    public void FuseWithWeightedScores_DocumentOnlyInVector_GetsVectorScoreOnly()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("text-doc", 100.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "vector-doc", Score = 0.8 }
        };

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.5, textWeight: 0.5);

        // Assert
        var vectorDoc = results.First(r => r.DocumentId == "vector-doc");
        Assert.Equal(0.8 * 0.5, vectorDoc.Score, 6); // Only vector contribution
        Assert.Equal(0, vectorDoc.TextRank);
        Assert.Equal(1, vectorDoc.VectorRank);
    }

    #endregion

    #region FuseResults (Method Selection) Tests

    [Fact]
    public void FuseResults_DefaultsToRRF()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.9 }
        };

        // Act
        var resultsDefault = service.FuseResults(textResults, vectorResults);
        var resultsRRF = service.FuseWithRRF(textResults, vectorResults);

        // Assert
        Assert.Equal(resultsRRF[0].Score, resultsDefault[0].Score, 6);
    }

    [Fact]
    public void FuseResults_MethodRRF_UsesRRFAlgorithm()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.9 }
        };

        // Act
        var results = service.FuseResults(textResults, vectorResults, method: FusionMethod.RRF);

        // Assert - RRF score for rank 1 in both: 1/61 + 1/61
        Assert.Equal(2.0 / 61, results[0].Score, 6);
    }

    [Fact]
    public void FuseResults_MethodWeighted_UsesWeightedAlgorithm()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 100.0)
        };
        var vectorResults = new List<VectorSearchResult>
        {
            new VectorSearchResult { DocumentId = "doc1", Score = 0.8 }
        };

        // Act
        var results = service.FuseResults(textResults, vectorResults, 
            method: FusionMethod.Weighted, vectorWeight: 0.6, textWeight: 0.4);

        // Assert - Weighted: (1.0 * 0.4) + (0.8 * 0.6) = 0.4 + 0.48 = 0.88
        Assert.Equal(0.88, results[0].Score, 6);
    }

    [Fact]
    public void FuseResults_PassesKParameterToRRF()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var resultsK10 = service.FuseResults(textResults, vectorResults, 
            method: FusionMethod.RRF, rrfK: 10);
        var resultsK100 = service.FuseResults(textResults, vectorResults, 
            method: FusionMethod.RRF, rrfK: 100);

        // Assert
        Assert.Equal(1.0 / 11, resultsK10[0].Score, 6);
        Assert.Equal(1.0 / 101, resultsK100[0].Score, 6);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FuseWithRRF_DuplicateDocumentIds_LastOneWins()
    {
        // Arrange
        var service = CreateService();
        // This shouldn't happen in practice, but let's test the behavior
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0),
            ("doc1", 5.0) // Duplicate - overwrites first
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithRRF(textResults, vectorResults);

        // Assert - Should use rank 2 since that's where the second occurrence is
        Assert.Single(results);
        // The rank will be 2 (last occurrence)
        Assert.Equal("doc1", results[0].DocumentId);
    }

    [Fact]
    public void FuseWithWeightedScores_NegativeScores_NormalizesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0),
            ("doc2", -10.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseWithWeightedScores(textResults, vectorResults, 
            vectorWeight: 0.0, textWeight: 1.0);

        // Assert
        // doc1: (10 - (-10)) / (10 - (-10)) = 20/20 = 1.0
        var doc1 = results.First(r => r.DocumentId == "doc1");
        Assert.Equal(1.0, doc1.Score, 6);

        // doc2: (-10 - (-10)) / (10 - (-10)) = 0/20 = 0.0
        var doc2 = results.First(r => r.DocumentId == "doc2");
        Assert.Equal(0.0, doc2.Score, 6);
    }

    [Fact]
    public void FuseResults_TopKZero_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var textResults = new List<(string DocumentId, double Score)>
        {
            ("doc1", 10.0)
        };
        var vectorResults = Enumerable.Empty<VectorSearchResult>();

        // Act
        var results = service.FuseResults(textResults, vectorResults, topK: 0);

        // Assert
        Assert.Empty(results);
    }

    #endregion
}
