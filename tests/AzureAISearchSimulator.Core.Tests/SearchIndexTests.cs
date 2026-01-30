using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for SearchIndex model validation.
/// </summary>
public class SearchIndexTests
{
    [Fact]
    public void SearchIndex_WithValidFields_ShouldBeValid()
    {
        // Arrange
        var index = new SearchIndex
        {
            Name = "test-index",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true }
            }
        };

        // Assert
        Assert.NotNull(index);
        Assert.Equal("test-index", index.Name);
        Assert.Equal(3, index.Fields.Count);
        Assert.Single(index.Fields, f => f.Key);
    }

    [Fact]
    public void SearchIndex_KeyField_ShouldBeIdentified()
    {
        // Arrange
        var index = new SearchIndex
        {
            Name = "test-index",
            Fields = new List<SearchField>
            {
                new() { Name = "documentId", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true }
            }
        };

        // Act
        var keyField = index.Fields.FirstOrDefault(f => f.Key);

        // Assert
        Assert.NotNull(keyField);
        Assert.Equal("documentId", keyField.Name);
    }

    [Fact]
    public void SearchField_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var field = new SearchField { Name = "test", Type = "Edm.String" };

        // Assert - Key is non-nullable bool (false by default), others are nullable
        Assert.False(field.Key);
        Assert.Null(field.Searchable);
        Assert.Null(field.Filterable);
        Assert.Null(field.Sortable);
        Assert.Null(field.Facetable);
        Assert.True(field.Retrievable ?? true); // Default is null, treated as true
    }

    [Fact]
    public void SearchField_AllEdmTypes_ShouldBeSupported()
    {
        // Arrange
        var supportedTypes = new[]
        {
            "Edm.String",
            "Edm.Int32",
            "Edm.Int64",
            "Edm.Double",
            "Edm.Boolean",
            "Edm.DateTimeOffset",
            "Edm.GeographyPoint",
            "Collection(Edm.String)",
            "Collection(Edm.Single)"
        };

        // Act & Assert
        foreach (var type in supportedTypes)
        {
            var field = new SearchField { Name = $"field_{type.Replace(".", "_")}", Type = type };
            Assert.Equal(type, field.Type);
        }
    }

    [Fact]
    public void SearchIndex_VectorSearchConfig_ShouldBeConfigurable()
    {
        // Arrange
        var index = new SearchIndex
        {
            Name = "vector-index",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "embedding", Type = "Collection(Edm.Single)", Dimensions = 1536 }
            },
            VectorSearch = new VectorSearchConfiguration
            {
                Algorithms = new List<VectorSearchAlgorithm>
                {
                    new() { Name = "hnsw-config", Kind = "hnsw" }
                },
                Profiles = new List<VectorSearchProfile>
                {
                    new() { Name = "vector-profile", Algorithm = "hnsw-config" }
                }
            }
        };

        // Assert
        Assert.NotNull(index.VectorSearch);
        Assert.Single(index.VectorSearch.Algorithms);
        Assert.Single(index.VectorSearch.Profiles);
        Assert.Equal(1536, index.Fields[1].Dimensions);
    }

    [Fact]
    public void SearchIndex_WithSuggesters_ShouldBeConfigurable()
    {
        // Arrange
        var index = new SearchIndex
        {
            Name = "suggester-index",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true }
            },
            Suggesters = new List<Suggester>
            {
                new()
                {
                    Name = "sg",
                    SearchMode = "analyzingInfixMatching",
                    SourceFields = new List<string> { "title" }
                }
            }
        };

        // Assert
        Assert.NotNull(index.Suggesters);
        Assert.Single(index.Suggesters);
        Assert.Equal("sg", index.Suggesters[0].Name);
    }

    [Fact]
    public void SearchIndex_Description_ShouldBeConfigurable()
    {
        // Arrange - API 2025-09-01 feature
        var index = new SearchIndex
        {
            Name = "test-index",
            Description = "An index for testing hotel data with semantic search capabilities",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true }
            }
        };

        // Assert
        Assert.Equal("An index for testing hotel data with semantic search capabilities", index.Description);
    }

    [Fact]
    public void SearchField_Normalizer_ShouldBeConfigurable()
    {
        // Arrange - API 2025-09-01 feature
        var field = new SearchField
        {
            Name = "category",
            Type = "Edm.String",
            Filterable = true,
            Sortable = true,
            Facetable = true,
            Normalizer = "lowercase"
        };

        // Assert
        Assert.Equal("lowercase", field.Normalizer);
    }

    [Fact]
    public void SearchIndex_WithNormalizers_ShouldBeConfigurable()
    {
        // Arrange - API 2025-09-01 feature
        var index = new SearchIndex
        {
            Name = "normalizer-index",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "category", Type = "Edm.String", Filterable = true, Normalizer = "my_normalizer" }
            },
            Normalizers = new List<CustomNormalizer>
            {
                new()
                {
                    Name = "my_normalizer",
                    ODataType = "#Microsoft.Azure.Search.CustomNormalizer",
                    TokenFilters = new List<string> { "lowercase", "asciifolding" }
                }
            }
        };

        // Assert
        Assert.NotNull(index.Normalizers);
        Assert.Single(index.Normalizers);
        Assert.Equal("my_normalizer", index.Normalizers[0].Name);
        Assert.Contains("lowercase", index.Normalizers[0].TokenFilters);
        Assert.Contains("asciifolding", index.Normalizers[0].TokenFilters);
    }

    [Fact]
    public void NormalizerName_IsBuiltIn_ShouldRecognizeBuiltInNormalizers()
    {
        // Arrange & Act & Assert
        Assert.True(NormalizerName.IsBuiltIn("lowercase"));
        Assert.True(NormalizerName.IsBuiltIn("LOWERCASE")); // Case insensitive
        Assert.True(NormalizerName.IsBuiltIn("uppercase"));
        Assert.True(NormalizerName.IsBuiltIn("standard"));
        Assert.False(NormalizerName.IsBuiltIn("custom_normalizer"));
        Assert.False(NormalizerName.IsBuiltIn(null));
    }
}
