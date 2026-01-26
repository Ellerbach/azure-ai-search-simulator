using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for VectorSearchSettings and related configuration classes.
/// </summary>
public class VectorSearchSettingsTests
{
    [Fact]
    public void VectorSearchSettings_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new VectorSearchSettings();

        // Assert
        Assert.Equal(1536, settings.DefaultDimensions);
        Assert.Equal(100000, settings.MaxVectorsPerIndex);
        Assert.Equal("cosine", settings.SimilarityMetric);
        Assert.True(settings.UseHnsw);
        Assert.NotNull(settings.HnswSettings);
        Assert.NotNull(settings.HybridSearchSettings);
    }

    [Fact]
    public void VectorSearchSettings_SectionName_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("VectorSearchSettings", VectorSearchSettings.SectionName);
    }

    [Fact]
    public void HnswSettings_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new HnswSettings();

        // Assert
        Assert.Equal(16, settings.M);
        Assert.Equal(200, settings.EfConstruction);
        Assert.Equal(100, settings.EfSearch);
        Assert.Equal(5, settings.OversampleMultiplier);
        Assert.Equal(42, settings.RandomSeed);
    }

    [Fact]
    public void HnswSettings_MParameter_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HnswSettings { M = 32 };

        // Assert
        Assert.Equal(32, settings.M);
    }

    [Fact]
    public void HnswSettings_EfConstruction_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HnswSettings { EfConstruction = 400 };

        // Assert
        Assert.Equal(400, settings.EfConstruction);
    }

    [Fact]
    public void HnswSettings_EfSearch_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HnswSettings { EfSearch = 200 };

        // Assert
        Assert.Equal(200, settings.EfSearch);
    }

    [Fact]
    public void HnswSettings_OversampleMultiplier_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HnswSettings { OversampleMultiplier = 10 };

        // Assert
        Assert.Equal(10, settings.OversampleMultiplier);
    }

    [Fact]
    public void HnswSettings_RandomSeed_NegativeValue_ShouldIndicateRandomSeed()
    {
        // Arrange
        var settings = new HnswSettings { RandomSeed = -1 };

        // Assert
        Assert.Equal(-1, settings.RandomSeed);
    }

    [Fact]
    public void HybridSearchSettings_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new HybridSearchSettings();

        // Assert
        Assert.Equal("RRF", settings.DefaultFusionMethod);
        Assert.Equal(0.7, settings.DefaultVectorWeight);
        Assert.Equal(0.3, settings.DefaultTextWeight);
        Assert.Equal(60, settings.RrfK);
    }

    [Fact]
    public void HybridSearchSettings_FusionMethod_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HybridSearchSettings { DefaultFusionMethod = "Weighted" };

        // Assert
        Assert.Equal("Weighted", settings.DefaultFusionMethod);
    }

    [Fact]
    public void HybridSearchSettings_Weights_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HybridSearchSettings
        {
            DefaultVectorWeight = 0.5,
            DefaultTextWeight = 0.5
        };

        // Assert
        Assert.Equal(0.5, settings.DefaultVectorWeight);
        Assert.Equal(0.5, settings.DefaultTextWeight);
    }

    [Fact]
    public void HybridSearchSettings_RrfK_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new HybridSearchSettings { RrfK = 100 };

        // Assert
        Assert.Equal(100, settings.RrfK);
    }

    [Fact]
    public void VectorSearchSettings_UseHnsw_CanBeDisabled()
    {
        // Arrange
        var settings = new VectorSearchSettings { UseHnsw = false };

        // Assert
        Assert.False(settings.UseHnsw);
    }

    [Fact]
    public void VectorSearchSettings_SimilarityMetric_SupportsMultipleOptions()
    {
        // Test cosine
        var cosineSettings = new VectorSearchSettings { SimilarityMetric = "cosine" };
        Assert.Equal("cosine", cosineSettings.SimilarityMetric);

        // Test euclidean
        var euclideanSettings = new VectorSearchSettings { SimilarityMetric = "euclidean" };
        Assert.Equal("euclidean", euclideanSettings.SimilarityMetric);

        // Test dotProduct
        var dotProductSettings = new VectorSearchSettings { SimilarityMetric = "dotProduct" };
        Assert.Equal("dotProduct", dotProductSettings.SimilarityMetric);
    }

    [Fact]
    public void VectorSearchSettings_NestedSettings_ShouldBeModifiable()
    {
        // Arrange
        var settings = new VectorSearchSettings
        {
            HnswSettings = new HnswSettings
            {
                M = 24,
                EfConstruction = 300,
                EfSearch = 150
            },
            HybridSearchSettings = new HybridSearchSettings
            {
                DefaultFusionMethod = "Weighted",
                DefaultVectorWeight = 0.8,
                DefaultTextWeight = 0.2
            }
        };

        // Assert
        Assert.Equal(24, settings.HnswSettings.M);
        Assert.Equal(300, settings.HnswSettings.EfConstruction);
        Assert.Equal(150, settings.HnswSettings.EfSearch);
        Assert.Equal("Weighted", settings.HybridSearchSettings.DefaultFusionMethod);
        Assert.Equal(0.8, settings.HybridSearchSettings.DefaultVectorWeight);
    }
}
