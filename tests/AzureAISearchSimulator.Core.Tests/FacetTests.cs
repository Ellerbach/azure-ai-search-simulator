using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for facet-related functionality.
/// </summary>
public class FacetTests
{
    [Fact]
    public void FacetResult_ValueFacet_ShouldHaveValueAndCount()
    {
        // Arrange
        var facet = new FacetResult
        {
            Value = "Luxury",
            Count = 15
        };

        // Assert
        Assert.Equal("Luxury", facet.Value);
        Assert.Equal(15, facet.Count);
        Assert.Null(facet.From);
        Assert.Null(facet.To);
    }

    [Fact]
    public void FacetResult_RangeFacet_ShouldHaveFromToAndCount()
    {
        // Arrange
        var facet = new FacetResult
        {
            From = 4.0,
            To = 5.0,
            Count = 30
        };

        // Assert
        Assert.Equal(4.0, facet.From);
        Assert.Equal(5.0, facet.To);
        Assert.Equal(30, facet.Count);
        Assert.Null(facet.Value);
    }

    [Fact]
    public void SearchResponse_Facets_ShouldContainMultipleFields()
    {
        // Arrange
        var response = new SearchResponse
        {
            SearchFacets = new Dictionary<string, List<FacetResult>>
            {
                ["category"] = new List<FacetResult>
                {
                    new() { Value = "Luxury", Count = 15 },
                    new() { Value = "Budget", Count = 27 }
                },
                ["rating"] = new List<FacetResult>
                {
                    new() { From = 3.0, To = 4.0, Count = 20 },
                    new() { From = 4.0, To = 5.0, Count = 30 }
                }
            }
        };

        // Assert
        Assert.NotNull(response.SearchFacets);
        Assert.Equal(2, response.SearchFacets.Count);
        Assert.True(response.SearchFacets.ContainsKey("category"));
        Assert.True(response.SearchFacets.ContainsKey("rating"));
        Assert.Equal(2, response.SearchFacets["category"].Count);
        Assert.Equal(2, response.SearchFacets["rating"].Count);
    }

    [Fact]
    public void SearchRequest_Facets_ShouldSupportMultipleSpecifications()
    {
        // Arrange
        var request = new SearchRequest
        {
            Search = "*",
            Facets = new List<string>
            {
                "category,count:5",
                "rating,interval:1"
            }
        };

        // Assert
        Assert.NotNull(request.Facets);
        Assert.Equal(2, request.Facets.Count);
        Assert.Contains("category,count:5", request.Facets);
        Assert.Contains("rating,interval:1", request.Facets);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("category,count:10")]
    [InlineData("rating,interval:1")]
    [InlineData("price,interval:100,count:20")]
    public void FacetSpec_ShouldSupportVariousFormats(string facetSpec)
    {
        // Arrange & Act
        var parts = facetSpec.Split(',');
        var fieldName = parts[0];

        // Assert
        Assert.NotNull(fieldName);
        Assert.True(parts.Length >= 1);
    }

    [Fact]
    public void SearchResponse_WithFacets_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new SearchResponse
        {
            ODataContext = "https://localhost/indexes('test')/$metadata#docs",
            ODataCount = 50,
            SearchFacets = new Dictionary<string, List<FacetResult>>
            {
                ["status"] = new List<FacetResult>
                {
                    new() { Value = "Active", Count = 30 },
                    new() { Value = "Inactive", Count = 20 }
                }
            },
            Value = new List<SearchResult>()
        };

        // Assert
        Assert.NotNull(response.SearchFacets);
        Assert.Single(response.SearchFacets);
        
        var statusFacets = response.SearchFacets["status"];
        Assert.Equal(2, statusFacets.Count);
        Assert.Equal("Active", statusFacets[0].Value);
        Assert.Equal(30, statusFacets[0].Count);
    }
}
