using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for vector search functionality.
/// </summary>
public class VectorSearchTests
{
    [Fact]
    public void VectorQuery_ShouldHaveRequiredProperties()
    {
        // Arrange
        var query = new VectorQuery
        {
            Kind = "vector",
            Vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            Fields = "embedding",
            K = 10
        };

        // Assert
        Assert.Equal("vector", query.Kind);
        Assert.NotNull(query.Vector);
        Assert.Equal(4, query.Vector.Length);
        Assert.Equal("embedding", query.Fields);
        Assert.Equal(10, query.K);
    }

    [Fact]
    public void SearchRequest_HybridSearch_ShouldCombineTextAndVector()
    {
        // Arrange
        var request = new SearchRequest
        {
            Search = "luxury hotel",
            VectorQueries = new List<VectorQuery>
            {
                new()
                {
                    Kind = "vector",
                    Vector = new[] { 0.1f, 0.2f, 0.3f },
                    Fields = "descriptionVector",
                    K = 5
                }
            }
        };

        // Assert
        Assert.NotNull(request.Search);
        Assert.NotNull(request.VectorQueries);
        Assert.Single(request.VectorQueries);
        Assert.Equal("descriptionVector", request.VectorQueries[0].Fields);
    }

    [Fact]
    public void VectorQuery_MultipleFields_ShouldBeSupported()
    {
        // Arrange
        var query = new VectorQuery
        {
            Kind = "vector",
            Vector = new[] { 0.5f, 0.5f },
            Fields = "embedding1,embedding2",
            K = 10
        };

        // Assert
        Assert.Contains(",", query.Fields);
        var fields = query.Fields.Split(',');
        Assert.Equal(2, fields.Length);
    }

    [Fact]
    public void SearchIndex_VectorField_ShouldHaveDimensions()
    {
        // Arrange
        var field = new SearchField
        {
            Name = "contentVector",
            Type = "Collection(Edm.Single)",
            Dimensions = 1536,
            VectorSearchProfile = "my-profile"
        };

        // Assert
        Assert.Equal("Collection(Edm.Single)", field.Type);
        Assert.Equal(1536, field.Dimensions);
        Assert.Equal("my-profile", field.VectorSearchProfile);
    }

    [Fact]
    public void VectorSearchConfiguration_ShouldSupportHNSW()
    {
        // Arrange
        var config = new VectorSearchConfiguration
        {
            Algorithms = new List<VectorSearchAlgorithm>
            {
                new()
                {
                    Name = "hnsw-config",
                    Kind = "hnsw"
                }
            },
            Profiles = new List<VectorSearchProfile>
            {
                new()
                {
                    Name = "vector-profile",
                    Algorithm = "hnsw-config"
                }
            }
        };

        // Assert
        Assert.Single(config.Algorithms);
        Assert.Equal("hnsw", config.Algorithms[0].Kind);
        Assert.Single(config.Profiles);
        Assert.Equal("hnsw-config", config.Profiles[0].Algorithm);
    }

    [Theory]
    [InlineData(new float[] { 1.0f, 0.0f, 0.0f }, new float[] { 1.0f, 0.0f, 0.0f }, 1.0)]
    [InlineData(new float[] { 1.0f, 0.0f, 0.0f }, new float[] { 0.0f, 1.0f, 0.0f }, 0.0)]
    [InlineData(new float[] { 1.0f, 0.0f, 0.0f }, new float[] { -1.0f, 0.0f, 0.0f }, -1.0)]
    public void CosineSimilarity_ShouldCalculateCorrectly(float[] vec1, float[] vec2, double expected)
    {
        // Arrange & Act
        var similarity = CalculateCosineSimilarity(vec1, vec2);

        // Assert
        Assert.Equal(expected, similarity, precision: 5);
    }

    [Fact]
    public void VectorQuery_DefaultK_ShouldBeTen()
    {
        // Arrange
        var query = new VectorQuery
        {
            Kind = "vector",
            Vector = new[] { 0.1f },
            Fields = "embedding"
            // K not specified - should default to 10
        };

        // Assert - K defaults to 10
        Assert.Equal(10, query.K);
    }

    /// <summary>
    /// Helper method to calculate cosine similarity.
    /// </summary>
    private static double CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Vectors must have the same length");

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}
