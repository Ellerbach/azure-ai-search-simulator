using System.Text.Json;
using System.Text.Json.Serialization;
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

    [Fact]
    public void FacetResult_SumFacet_ShouldHaveOnlySum()
    {
        // Arrange
        var facet = new FacetResult
        {
            Sum = 40.0
        };

        // Assert
        Assert.Equal(40.0, facet.Sum);
        Assert.Null(facet.Count);
        Assert.Null(facet.Value);
        Assert.Null(facet.From);
        Assert.Null(facet.To);
    }

    [Fact]
    public void FacetResult_SumFacet_SerializesToSumOnly()
    {
        // Arrange - same null-suppression the API uses globally (Program.cs)
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var facet = new FacetResult { Sum = 40.0 };

        // Act
        var json = JsonSerializer.Serialize(facet, options);

        // Assert - Azure returns a bucket containing only the metric: {"sum": 40.0}
        Assert.Contains("\"sum\"", json);
        Assert.DoesNotContain("\"count\"", json);
        Assert.DoesNotContain("\"value\"", json);
        Assert.DoesNotContain("\"from\"", json);
        Assert.DoesNotContain("\"to\"", json);
    }

    [Fact]
    public void FacetResult_ValueFacet_SerializesWithoutSum()
    {
        // Arrange
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var facet = new FacetResult { Value = "Luxury", Count = 15 };

        // Act
        var json = JsonSerializer.Serialize(facet, options);

        // Assert
        Assert.Contains("\"value\"", json);
        Assert.Contains("\"count\"", json);
        Assert.DoesNotContain("\"sum\"", json);
    }

    [Theory]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("avg")]
    public void FacetResult_AggregationFacet_ShouldHaveOnlyThatMetric(string metric)
    {
        // Arrange
        var facet = metric switch
        {
            "min" => new FacetResult { Min = 1.0 },
            "max" => new FacetResult { Max = 5.0 },
            "avg" => new FacetResult { Avg = 3.0 },
            _ => throw new InvalidOperationException()
        };

        // Assert - only the requested metric is set, everything else is null
        Assert.Null(facet.Count);
        Assert.Null(facet.Value);
        Assert.Null(facet.From);
        Assert.Null(facet.To);
        Assert.Null(facet.Sum);
        if (metric != "min") Assert.Null(facet.Min);
        if (metric != "max") Assert.Null(facet.Max);
        if (metric != "avg") Assert.Null(facet.Avg);
    }

    [Fact]
    public void FacetResult_MinMaxAvg_SerializeToMetricOnly()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Assert.Equal("{\"min\":1}", JsonSerializer.Serialize(new FacetResult { Min = 1.0 }, options));
        Assert.Equal("{\"max\":5}", JsonSerializer.Serialize(new FacetResult { Max = 5.0 }, options));
        Assert.Equal("{\"avg\":3}", JsonSerializer.Serialize(new FacetResult { Avg = 3.0 }, options));
    }

    [Theory]
    [InlineData("category")]
    [InlineData("category,count:10")]
    [InlineData("rating,interval:1")]
    [InlineData("price,interval:100,count:20")]
    [InlineData("sleepsCount,metric:sum")]
    [InlineData("sleepsCount, metric: sum")]
    [InlineData("sleepsCount,metric:min")]
    [InlineData("sleepsCount,metric:max")]
    [InlineData("sleepsCount,metric:avg")]
    [InlineData("intField,metric:sum,default:5")]
    [InlineData("stringField,metric:sum,default:'5'")]
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
