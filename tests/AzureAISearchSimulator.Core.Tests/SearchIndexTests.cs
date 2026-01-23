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
}
