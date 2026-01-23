using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for SearchRequest and SearchResponse models.
/// </summary>
public class SearchModelTests
{
    [Fact]
    public void SearchRequest_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var request = new SearchRequest();

        // Assert
        Assert.Null(request.Search);
        Assert.Equal("any", request.SearchMode);
        Assert.Equal("simple", request.QueryType);
        Assert.Null(request.Filter);
        Assert.Null(request.OrderBy);
        Assert.Null(request.Select);
        Assert.Null(request.Top);
        Assert.Null(request.Skip);
        Assert.Null(request.Count);
    }

    [Fact]
    public void SearchRequest_WithAllParameters_ShouldBeConfigured()
    {
        // Arrange & Act
        var request = new SearchRequest
        {
            Search = "luxury hotel",
            SearchMode = "all",
            QueryType = "full",
            Filter = "rating ge 4",
            OrderBy = "rating desc",
            Select = "id,name,rating",
            Top = 10,
            Skip = 0,
            Count = true,
            Highlight = "description",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>"
        };

        // Assert
        Assert.Equal("luxury hotel", request.Search);
        Assert.Equal("all", request.SearchMode);
        Assert.Equal("full", request.QueryType);
        Assert.Equal("rating ge 4", request.Filter);
        Assert.Equal("rating desc", request.OrderBy);
        Assert.Equal("id,name,rating", request.Select);
        Assert.Equal(10, request.Top);
        Assert.Equal(0, request.Skip);
        Assert.True(request.Count);
        Assert.Equal("description", request.Highlight);
    }

    [Fact]
    public void SearchRequest_VectorQueries_ShouldBeConfigurable()
    {
        // Arrange
        var vectorQuery = new VectorQuery
        {
            Kind = "vector",
            Vector = new[] { 0.1f, 0.2f, 0.3f },
            Fields = "embedding",
            K = 10
        };

        var request = new SearchRequest
        {
            VectorQueries = new List<VectorQuery> { vectorQuery }
        };

        // Assert
        Assert.NotNull(request.VectorQueries);
        Assert.Single(request.VectorQueries);
        Assert.Equal("vector", request.VectorQueries[0].Kind);
        Assert.Equal(3, request.VectorQueries[0].Vector?.Length);
        Assert.Equal(10, request.VectorQueries[0].K);
    }

    [Fact]
    public void SearchResponse_WithResults_ShouldHaveCorrectStructure()
    {
        // Arrange
        var response = new SearchResponse
        {
            ODataContext = "https://simulator.search.windows.net/indexes('test')/$metadata#docs",
            ODataCount = 100,
            Value = new List<SearchResult>
            {
                new()
                {
                    Score = 1.5,
                    ["id"] = "1",
                    ["title"] = "Test Document"
                }
            }
        };

        // Assert
        Assert.NotNull(response.Value);
        Assert.Single(response.Value);
        Assert.Equal(100, response.ODataCount);
        Assert.Equal(1.5, response.Value[0].Score);
    }

    [Fact]
    public void SearchResult_WithHighlights_ShouldContainHighlightedFields()
    {
        // Arrange
        var result = new SearchResult
        {
            Score = 2.0,
            ["id"] = "1",
            ["description"] = "A luxury spa hotel",
            Highlights = new Dictionary<string, List<string>>
            {
                ["description"] = new() { "A <em>luxury</em> <em>spa</em> hotel" }
            }
        };

        // Assert
        Assert.NotNull(result.Highlights);
        Assert.True(result.Highlights.ContainsKey("description"));
        Assert.Contains("<em>luxury</em>", result.Highlights["description"][0]);
    }
}
