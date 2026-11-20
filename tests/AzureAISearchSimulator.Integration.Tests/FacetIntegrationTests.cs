using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests for facets on fields that are facetable but not filterable.
/// </summary>
public class FacetIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public FacetIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "facet-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = _testDir });
        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        _indexServiceMock = new Mock<IIndexService>();

        var scoringProfileService = new ScoringProfileService(
            Mock.Of<ILogger<ScoringProfileService>>());

        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object);

        _searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            scoringProfileService);
    }

    private void RegisterIndex(SearchIndex index)
    {
        _indexServiceMock.Setup(x => x.GetIndexAsync(index.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);
        _luceneManager.GetWriter(index.Name);
    }

    private async Task UploadDocuments(string indexName, params Dictionary<string, object?>[] documents)
    {
        var request = new IndexDocumentsRequest
        {
            Value = documents.Select(doc =>
            {
                var action = new IndexAction { ["@search.action"] = "upload" };
                foreach (var kvp in doc) action[kvp.Key] = kvp.Value;
                return action;
            }).ToList()
        };
        await _documentService.IndexDocumentsAsync(indexName, request);
    }

    [Fact]
    public async Task Facets_OnFacetableOnlyField_ReturnsFacetValues()
    {
        var indexName = $"facet-only-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "Category", Type = "Edm.String", Facetable = true, Filterable = false }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["title"] = "Hotel A", ["Category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["title"] = "Hotel B", ["Category"] = "Budget" },
            new Dictionary<string, object?> { ["id"] = "3", ["title"] = "Hotel C", ["Category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "4", ["title"] = "Hotel D", ["Category"] = "Budget" },
            new Dictionary<string, object?> { ["id"] = "5", ["title"] = "Hotel E", ["Category"] = "Mid-range" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Category" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Category"),
            "Facets should be returned for a facetable-only field (not filterable)");

        var categoryFacets = response.SearchFacets["Category"];
        Assert.Equal(3, categoryFacets.Count); // Luxury, Budget, Mid-range

        var luxury = categoryFacets.FirstOrDefault(f => f.Value?.ToString() == "Luxury");
        Assert.NotNull(luxury);
        Assert.Equal(2, luxury.Count);

        var budget = categoryFacets.FirstOrDefault(f => f.Value?.ToString() == "Budget");
        Assert.NotNull(budget);
        Assert.Equal(2, budget.Count);
    }

    [Fact]
    public async Task Facets_OnFilterableAndFacetableField_AlsoWorks()
    {
        var indexName = $"facet-filt-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "Category", Type = "Edm.String", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["title"] = "Hotel A", ["Category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["title"] = "Hotel B", ["Category"] = "Budget" },
            new Dictionary<string, object?> { ["id"] = "3", ["title"] = "Hotel C", ["Category"] = "Luxury" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Category" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Category"));

        var categoryFacets = response.SearchFacets["Category"];
        Assert.Equal(2, categoryFacets.Count); // Luxury, Budget

        var luxury = categoryFacets.FirstOrDefault(f => f.Value?.ToString() == "Luxury");
        Assert.NotNull(luxury);
        Assert.Equal(2, luxury.Count);
    }

    [Fact]
    public async Task Facets_WithFilter_OnlyCountsFilteredDocuments()
    {
        var indexName = $"facet-filter-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true },
                new() { Name = "rating", Type = "Edm.Double", Facetable = true, Filterable = true, Sortable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Grand Palace", ["category"] = "Luxury", ["rating"] = 4.8 },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Budget Stay", ["category"] = "Budget", ["rating"] = 3.2 },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Royal Suite", ["category"] = "Luxury", ["rating"] = 4.2 },
            new Dictionary<string, object?> { ["id"] = "4", ["hotelName"] = "Comfort Inn", ["category"] = "Mid-range", ["rating"] = 3.8 },
            new Dictionary<string, object?> { ["id"] = "5", ["hotelName"] = "The Ritz", ["category"] = "Luxury", ["rating"] = 4.9 });

        // Without filter: all 3 categories should appear
        var noFilterResponse = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category" }
        });

        Assert.NotNull(noFilterResponse.SearchFacets);
        var allFacets = noFilterResponse.SearchFacets["category"];
        Assert.Equal(3, allFacets.Count);
        Assert.Equal(3, allFacets.First(f => f.Value?.ToString() == "Luxury").Count);

        // With filter: only Luxury should appear in facets
        var filteredResponse = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category" },
            Filter = "category eq 'Luxury'"
        });

        Assert.NotNull(filteredResponse.SearchFacets);
        var filteredFacets = filteredResponse.SearchFacets["category"];
        Assert.Single(filteredFacets);
        Assert.Equal("Luxury", filteredFacets[0].Value?.ToString());
        Assert.Equal(3, filteredFacets[0].Count);
    }

    [Fact]
    public async Task Facets_WithNumericFilter_ReducesFacetCounts()
    {
        var indexName = $"facet-numfilt-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true },
                new() { Name = "rating", Type = "Edm.Double", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Grand Palace", ["category"] = "Luxury", ["rating"] = 4.8 },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Budget Stay", ["category"] = "Budget", ["rating"] = 3.2 },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Royal Suite", ["category"] = "Luxury", ["rating"] = 3.5 },
            new Dictionary<string, object?> { ["id"] = "4", ["hotelName"] = "The Ritz", ["category"] = "Luxury", ["rating"] = 4.9 });

        // Filter by rating > 4: only 2 Luxury hotels match (Grand Palace 4.8, The Ritz 4.9)
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category" },
            Filter = "rating gt 4"
        });

        Assert.NotNull(response.SearchFacets);
        var facets = response.SearchFacets["category"];
        // Only Luxury should appear (Budget 3.2 and Luxury 3.5 are filtered out)
        Assert.Single(facets);
        Assert.Equal("Luxury", facets[0].Value?.ToString());
        Assert.Equal(2, facets[0].Count);
    }

    [Fact]
    public async Task Facets_WithTextSearch_OnlyCountsMatchingDocuments()
    {
        var indexName = $"facet-text-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["description"] = "A luxury spa resort with pool", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["description"] = "Affordable rooms for travelers", ["category"] = "Budget" },
            new Dictionary<string, object?> { ["id"] = "3", ["description"] = "Spa and wellness retreat", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "4", ["description"] = "Basic motel with parking", ["category"] = "Budget" });

        // Search for "spa": only doc 1 and 3 match
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "spa",
            Facets = new List<string> { "category" }
        });

        Assert.NotNull(response.SearchFacets);
        var facets = response.SearchFacets["category"];
        // Only Luxury should appear (both spa-matching docs are Luxury)
        Assert.Single(facets);
        Assert.Equal("Luxury", facets[0].Value?.ToString());
        Assert.Equal(2, facets[0].Count);
    }

    [Theory]
    [InlineData("Edm.Int32")]
    [InlineData("Edm.Int64")]
    [InlineData("Edm.Double")]
    public async Task Facets_MetricSum_ReturnsSumOfNumericField(string fieldType)
    {
        var indexName = $"facet-sum-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "sleepsCount", Type = fieldType, Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["title"] = "Hotel A", ["sleepsCount"] = 4 },
            new Dictionary<string, object?> { ["id"] = "2", ["title"] = "Hotel B", ["sleepsCount"] = 2 },
            new Dictionary<string, object?> { ["id"] = "3", ["title"] = "Hotel C", ["sleepsCount"] = 6 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "sleepsCount, metric: sum" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("sleepsCount"));

        var sumFacets = response.SearchFacets["sleepsCount"];
        Assert.Single(sumFacets);
        Assert.Equal(12.0, sumFacets[0].Sum);
        Assert.Null(sumFacets[0].Count);
        Assert.Null(sumFacets[0].Value);
        Assert.Null(sumFacets[0].From);
        Assert.Null(sumFacets[0].To);
    }

    [Fact]
    public async Task Facets_MetricSum_RespectsFilter()
    {
        var indexName = $"facet-sum-filt-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true },
                new() { Name = "price", Type = "Edm.Double", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["category"] = "Luxury", ["price"] = 100.5 },
            new Dictionary<string, object?> { ["id"] = "2", ["category"] = "Budget", ["price"] = 50.0 },
            new Dictionary<string, object?> { ["id"] = "3", ["category"] = "Luxury", ["price"] = 200.25 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "category eq 'Luxury'",
            Facets = new List<string> { "price,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        var sumFacets = response.SearchFacets["price"];
        Assert.Single(sumFacets);
        Assert.Equal(300.75, sumFacets[0].Sum);
    }

    [Fact]
    public async Task Facets_MetricSum_RespectsTextSearch()
    {
        var indexName = $"facet-sum-text-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "rooms", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["description"] = "A luxury spa resort", ["rooms"] = 10 },
            new Dictionary<string, object?> { ["id"] = "2", ["description"] = "Affordable rooms", ["rooms"] = 20 },
            new Dictionary<string, object?> { ["id"] = "3", ["description"] = "Spa and wellness retreat", ["rooms"] = 5 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "spa",
            Facets = new List<string> { "rooms,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        var sumFacets = response.SearchFacets["rooms"];
        Assert.Single(sumFacets);
        Assert.Equal(15.0, sumFacets[0].Sum);
    }

    [Fact]
    public async Task Facets_MetricSum_DocumentsMissingFieldContributeNothing()
    {
        var indexName = $"facet-sum-miss-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "quantity", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["quantity"] = 7 },
            new Dictionary<string, object?> { ["id"] = "2" },
            new Dictionary<string, object?> { ["id"] = "3", ["quantity"] = 3 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "quantity,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        var sumFacets = response.SearchFacets["quantity"];
        Assert.Single(sumFacets);
        Assert.Equal(10.0, sumFacets[0].Sum);
    }

    [Fact]
    public async Task Facets_MetricSum_OnNonNumericField_Throws()
    {
        var indexName = $"facet-sum-str-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["category"] = "Luxury" });

        // As in Azure, a numeric metric on a string field is an error.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category,metric:sum" }
        }));
        Assert.StartsWith("The metric parameter 'sum' is only supported in numeric fields.", ex.Message);
    }

    [Fact]
    public async Task Facets_SameFieldAsValueAndSumFacet_ReturnsBothBuckets()
    {
        var indexName = $"facet-sum-both-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "rating", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["rating"] = 4 },
            new Dictionary<string, object?> { ["id"] = "2", ["rating"] = 4 },
            new Dictionary<string, object?> { ["id"] = "3", ["rating"] = 5 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "rating,interval:1", "rating,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        var ratingFacets = response.SearchFacets["rating"];

        // Interval buckets (4-5 -> 2 docs, 5-6 -> 1 doc) plus one sum bucket
        var sumBucket = ratingFacets.FirstOrDefault(f => f.Sum.HasValue);
        Assert.NotNull(sumBucket);
        Assert.Equal(13.0, sumBucket.Sum);
        Assert.Null(sumBucket.Count);

        var intervalBuckets = ratingFacets.Where(f => f.Sum == null).ToList();
        Assert.Equal(2, intervalBuckets.Count);
        Assert.Equal(3, intervalBuckets.Sum(b => b.Count));
    }

    [Fact]
    public async Task Facets_MetricSum_FieldNameCasingDiffersFromSchema_StillComputesSum()
    {
        var indexName = $"facet-sum-casing-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "Rating", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["Rating"] = 4 },
            new Dictionary<string, object?> { ["id"] = "2", ["Rating"] = 5 });

        // Request uses different casing than the schema field ("Rating") - the field lookup
        // is case-insensitive, but the actual Lucene reads and response key must use the
        // schema's canonical casing or the sum silently comes back empty/zero.
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "rating,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Rating"));
        Assert.False(response.SearchFacets.ContainsKey("rating"));

        var sumFacets = response.SearchFacets["Rating"];
        Assert.Single(sumFacets);
        Assert.Equal(9.0, sumFacets[0].Sum);
    }

    [Fact]
    public async Task Facets_MetricSum_SameFieldRequestedWithDifferentCasing_AppendsToOneBucket()
    {
        var indexName = $"facet-sum-casing-merge-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "Price", Type = "Edm.Double", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["Price"] = 10.0 },
            new Dictionary<string, object?> { ["id"] = "2", ["Price"] = 40.0 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "price,metric:min", "PRICE,metric:max" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Price"));

        var priceFacets = response.SearchFacets["Price"];
        Assert.Equal(2, priceFacets.Count);
        Assert.Equal(10.0, priceFacets.Single(f => f.Min.HasValue).Min);
        Assert.Equal(40.0, priceFacets.Single(f => f.Max.HasValue).Max);
    }

    [Fact]
    public async Task Facets_MetricMinMaxAvg_ComputeAcrossMatchingDocuments()
    {
        var indexName = $"facet-minmaxavg-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "price", Type = "Edm.Double", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["price"] = 10.0 },
            new Dictionary<string, object?> { ["id"] = "2", ["price"] = 40.0 },
            new Dictionary<string, object?> { ["id"] = "3", ["price"] = 25.0 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "price,metric:min", "price,metric:max", "price,metric:avg" }
        });

        Assert.NotNull(response.SearchFacets);
        var priceFacets = response.SearchFacets["price"];
        Assert.Equal(3, priceFacets.Count);

        Assert.Equal(10.0, priceFacets.Single(f => f.Min.HasValue).Min);
        Assert.Equal(40.0, priceFacets.Single(f => f.Max.HasValue).Max);
        Assert.Equal(25.0, priceFacets.Single(f => f.Avg.HasValue).Avg);
    }

    [Fact]
    public async Task Facets_MetricMinMaxAvg_RespectDefaultForMissingValues()
    {
        var indexName = $"facet-minmaxavg-default-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "score", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["score"] = 10 },
            new Dictionary<string, object?> { ["id"] = "2" }, // missing -> substitutes default:2
            new Dictionary<string, object?> { ["id"] = "3", ["score"] = 6 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "score,metric:min,default:2", "score,metric:avg,default:2" }
        });

        Assert.NotNull(response.SearchFacets);
        var scoreFacets = response.SearchFacets["score"];

        Assert.Equal(2.0, scoreFacets.Single(f => f.Min.HasValue).Min);
        // (10 + 2 + 6) / 3 = 6
        Assert.Equal(6.0, scoreFacets.Single(f => f.Avg.HasValue).Avg);
    }

    [Fact]
    public async Task Facets_MetricMinMaxAvg_NoContributingValuesAndNoDefault_ReturnsNoBucket()
    {
        var indexName = $"facet-minmaxavg-empty-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "score", Type = "Edm.Int32", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        // No document has a value for "score", and no default is specified, so min/max/avg
        // have nothing to contribute - each should return no bucket rather than one
        // that serializes to an empty, unhelpful {} object.
        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1" },
            new Dictionary<string, object?> { ["id"] = "2" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "score,metric:min", "score,metric:max", "score,metric:avg" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("score"));
        Assert.Empty(response.SearchFacets["score"]);
    }

    [Fact]
    public async Task Facets_MetricMinMax_OnNonNumericField_Throws()
    {
        var indexName = $"facet-minmax-str-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["category"] = "Luxury" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category,metric:min" }
        }));
        Assert.StartsWith("The metric parameter 'min' is only supported in numeric fields.", ex.Message);
    }

    [Fact]
    public async Task Facets_UnsupportedMetric_Throws()
    {
        var indexName = $"facet-metric-unsupported-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "price", Type = "Edm.Double", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["price"] = 10.0 });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "price,metric:median" }
        }));
        Assert.StartsWith("Invalid metric parameter in facet for 'price'.", ex.Message);
    }

    [Fact]
    public async Task Search_WithTopZero_ReturnsFacetsAndCountWithNoDocuments()
    {
        // Azure allows "top": 0 for facet-only or count-only queries (e.g. the "distinct values"
        // pattern in Azure's own docs). Lucene's searcher.Search requires numHits > 0 internally,
        // so this must be handled without surfacing that as a client-facing error.
        var indexName = $"facet-top-zero-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "category", Type = "Edm.String", Facetable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["category"] = "Budget" },
            new Dictionary<string, object?> { ["id"] = "3", ["category"] = "Luxury" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Count = true,
            Top = 0,
            Facets = new List<string> { "category,count:5" }
        });

        Assert.Empty(response.Value);
        Assert.Equal(3, response.ODataCount);
        Assert.NotNull(response.SearchFacets);
        var facets = response.SearchFacets["category"];
        Assert.Equal(2, facets.Single(f => f.Value?.ToString() == "Luxury").Count);
        Assert.Equal(1, facets.Single(f => f.Value?.ToString() == "Budget").Count);
    }

    [Fact]
    public async Task Facets_OnComplexTypeSubField_ValueFacet_ReturnsFacetValues()
    {
        var indexName = $"facet-complex-value-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new()
                {
                    Name = "Tiles",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "PendingPaymentCount", Type = "Edm.Int32", Facetable = true, Filterable = true },
                        new() { Name = "Status", Type = "Edm.String", Facetable = true, Filterable = true }
                    }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 3, ["Status"] = "Open" } },
            new Dictionary<string, object?> { ["id"] = "2", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 0, ["Status"] = "Closed" } },
            new Dictionary<string, object?> { ["id"] = "3", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 5, ["Status"] = "Open" } });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Tiles/Status" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Tiles/Status"),
            "Facets should resolve a path into a complex-type field's sub-fields");

        var statusFacets = response.SearchFacets["Tiles/Status"];
        Assert.Equal(2, statusFacets.Single(f => f.Value?.ToString() == "Open").Count);
        Assert.Equal(1, statusFacets.Single(f => f.Value?.ToString() == "Closed").Count);
    }

    [Fact]
    public async Task Facets_OnComplexTypeSubField_MetricFacet_AggregatesAcrossDocuments()
    {
        var indexName = $"facet-complex-metric-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new()
                {
                    Name = "Tiles",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "PendingPaymentCount", Type = "Edm.Int32", Facetable = true, Filterable = true }
                    }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 3 } },
            new Dictionary<string, object?> { ["id"] = "2", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 0 } },
            new Dictionary<string, object?> { ["id"] = "3", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 5 } });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Tiles/PendingPaymentCount,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.True(response.SearchFacets.ContainsKey("Tiles/PendingPaymentCount"));
        Assert.Equal(8, response.SearchFacets["Tiles/PendingPaymentCount"].Single().Sum);
    }

    [Fact]
    public async Task Facets_OnComplexTypeSubField_WithJsonElementDocument_ReturnsFacetValues()
    {
        // Documents submitted over HTTP are deserialized with nested objects as JsonElement
        // (not Dictionary<string, object?>), since IndexAction's value type is `object?`.
        // This reproduces that shape directly rather than going through the controller.
        var indexName = $"facet-complex-jsonelement-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new()
                {
                    Name = "Tiles",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "PendingPaymentCount", Type = "Edm.Int32", Facetable = true, Filterable = true }
                    }
                }
            }
        };
        RegisterIndex(index);

        var doc1 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
            "{\"id\":\"1\",\"Tiles\":{\"PendingPaymentCount\":3}}")!;
        var doc2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
            "{\"id\":\"2\",\"Tiles\":{\"PendingPaymentCount\":5}}")!;

        Assert.IsType<System.Text.Json.JsonElement>(doc1["Tiles"]);

        await UploadDocuments(indexName, doc1, doc2);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Tiles/PendingPaymentCount,metric:sum" }
        });

        Assert.NotNull(response.SearchFacets);
        Assert.Equal(8, response.SearchFacets["Tiles/PendingPaymentCount"].Single().Sum);
    }

    [Fact]
    public async Task Facets_OnComplexTypeSubField_NotFacetable_Throws()
    {
        var indexName = $"facet-complex-notfacetable-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new()
                {
                    Name = "Tiles",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "PendingPaymentCount", Type = "Edm.Int32", Facetable = false, Filterable = true }
                    }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 3 } });

        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Tiles/PendingPaymentCount" }
        }));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("unknown")]
    [InlineData("city,count:abc")]
    [InlineData("city,count:-1")]
    public async Task Facets_OnFieldThatIsNotFacetableOrWithAnInvalidParameter_Throws(string facet)
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { facet }
        }));
    }

    [Fact]
    public async Task Facets_OnComplexCollectionSubField_CountsEachDocumentOncePerValue()
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "rooms/type" }
        });

        // Assert: document 1 has two suites and a single, document 2 a single.
        var facets = response.SearchFacets!["rooms/type"];
        Assert.Equal(new object[] { "single", "suite" }, facets.Select(f => f.Value));
        Assert.Equal(new long?[] { 2, 1 }, facets.Select(f => f.Count));
    }

    [Theory]
    [InlineData("city,count:0", 3)]
    [InlineData("city,count:1", 1)]
    public async Task Facets_WithCount_LimitsTheBucketsAndZeroMeansAll(string facet, int buckets)
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { facet }
        });

        // Assert
        Assert.Equal(buckets, response.SearchFacets!["city"].Count);
    }

    [Fact]
    public async Task Facets_OnSearchableField_CountsExactCaseSensitiveValues()
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "city" }
        });

        // Assert: whole values, not analyzed terms, and Paris and paris are different values.
        var facets = response.SearchFacets!["city"];
        Assert.Equal(new object[] { "New York", "Paris", "paris" }, facets.Select(f => f.Value));
        Assert.Equal(new long?[] { 2, 1, 1 }, facets.Select(f => f.Count));
    }

    [Fact]
    public async Task Facets_OnCollection_CountsEachDocumentOncePerValue()
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "tags" }
        });

        // Assert
        var facets = response.SearchFacets!["tags"];
        Assert.Equal(new object[] { "a", "b" }, facets.Select(f => f.Value));
        Assert.Equal(new long?[] { 3, 1 }, facets.Select(f => f.Count));
    }

    [Fact]
    public async Task Facets_OnDateField_ReturnsUtcValuesToTheMillisecond()
    {
        // Arrange
        var indexName = await SeedValueFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "opened" }
        });

        // Assert
        var facet = Assert.Single(response.SearchFacets!["opened"]);
        Assert.Equal("2024-03-05T08:20:30.123Z", facet.Value);
        Assert.Equal(4, facet.Count);
    }

    [Fact]
    public async Task Facets_OnFieldWithNormalizer_CountsNormalizedValues()
    {
        // Arrange
        var indexName = $"facet-normalizer-{Guid.NewGuid():N}";
        RegisterIndex(new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "city", Type = "Edm.String", Facetable = true, Filterable = true, Normalizer = "lowercase" },
                new() { Name = "tags", Type = "Collection(Edm.String)", Facetable = true, Normalizer = "asciifolding" }
            }
        });

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["city"] = "Paris", ["tags"] = new[] { "café" } },
            new Dictionary<string, object?> { ["id"] = "2", ["city"] = "PARIS", ["tags"] = new[] { "cafe", "Café" } },
            new Dictionary<string, object?> { ["id"] = "3", ["city"] = "Lyon", ["tags"] = new[] { "Cafe" } });

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "city", "tags" }
        });

        // Assert: one bucket per normalized value, holding the normalized value.
        var cities = response.SearchFacets!["city"];
        Assert.Equal(new object[] { "paris", "lyon" }, cities.Select(f => f.Value));
        Assert.Equal(new long?[] { 2, 1 }, cities.Select(f => f.Count));
        var tags = response.SearchFacets["tags"];
        Assert.Equal(new object[] { "Cafe", "cafe" }, tags.Select(f => f.Value));
        Assert.Equal(new long?[] { 2, 2 }, tags.Select(f => f.Count));
    }

    [Fact]
    public async Task Facets_OnDoubleField_CountsEqualDoublesInOneBucket()
    {
        // Arrange: 2^53 and 2^53 + 1 are the same double.
        var indexName = $"facet-double-{Guid.NewGuid():N}";
        RegisterIndex(new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "score", Type = "Edm.Double", Facetable = true }
            }
        });

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["score"] = 9007199254740992L },
            new Dictionary<string, object?> { ["id"] = "2", ["score"] = 9007199254740993L });

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "score" }
        });

        // Assert
        var facet = Assert.Single(response.SearchFacets!["score"]);
        Assert.Equal(9007199254740992d, facet.Value);
        Assert.Equal(2, facet.Count);
    }

    [Theory]
    [InlineData("rate,interval:100", "-100:1 0:1 200:1")]
    [InlineData("floors,interval:5", "0:3 10:1")]
    [InlineData("rooms/baseRate,interval:100", "0:1 100:1 600:1")]
    [InlineData("rooms/beds,interval:2", "0:1 2:2 4:1")]
    [InlineData("opened,interval:month", "2023-12-01T00:00:00Z:1 2024-01-01T00:00:00Z:1 2024-03-01T00:00:00Z:1")]
    [InlineData("opened,interval:week", "2023-12-25T00:00:00Z:1 2024-01-15T00:00:00Z:1 2024-02-26T00:00:00Z:1")]
    [InlineData("opened,interval:quarter", "2023-10-01T00:00:00Z:1 2024-01-01T00:00:00Z:2")]
    [InlineData("opened,interval:hour", "2023-12-31T23:00:00Z:1 2024-01-15T10:00:00Z:1 2024-03-02T23:00:00Z:1")]
    [InlineData("opened,interval:day,timeoffset:+01:00", "2023-12-31T23:00:00Z:1 2024-01-14T23:00:00Z:1 2024-03-02T23:00:00Z:1")]
    [InlineData("nums", "2:2 1:1 3:1 7:1")]
    [InlineData("nums,interval:2", "0:1 2:2 6:1")]
    [InlineData("rooms/beds", "2:2 1:1 4:1")]
    [InlineData("kind,sort:value", "a:2 b:2 c:1")]
    [InlineData("kind,sort:-value", "c:1 b:2 a:2")]
    [InlineData("kind,sort:-count", "c:1 a:2 b:2")]
    [InlineData("kind,count:1,sort:count", "a:2")]
    public async Task Facets_Buckets_AreAzures(string facet, string expected)
    {
        // Arrange: the documents compared with Azure, whose buckets these are.
        var indexName = await SeedBucketFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Facets = [facet] });

        // Assert: intervals are each value's lower bound (Monday-based weeks), counted once per document.
        Assert.Equal(expected, string.Join(" ", response.SearchFacets!.Single().Value.Select(f => $"{f.Value}:{f.Count}")));
    }

    [Theory]
    [InlineData("floors,values:5|10", "-:5.0:3 5.0:10.0:0 10.0:-:1")]
    [InlineData("floors,values:10|5", "-:10.0:3 5.0:-:1 10.0:5.0:0")]
    [InlineData("rooms/baseRate,values:100", "-:100.0:1 100.0:-:2")]
    [InlineData("opened,values:2024-01-01T00:00:00Z", "-:2024-01-01T00:00:00.000+0000:1 2024-01-01T00:00:00.000+0000:-:2")]
    public async Task Facets_WithValues_CountEachRangeIncludingEmptyOnes(string facet, string expected)
    {
        // Arrange
        var indexName = await SeedBucketFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Facets = [facet] });

        // Assert: ranges are listed by their lower bound, which an unordered list can make overlap.
        Assert.Equal(expected, string.Join(" ", response.SearchFacets!.Single().Value.Select(f => $"{f.From ?? "-"}:{f.To ?? "-"}:{f.Count}")));
    }

    [Fact]
    public async Task Facets_MetricsOnComplexCollectionSubField_AggregateEveryElement()
    {
        // Arrange
        var indexName = await SeedBucketFacetIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = ["rooms/baseRate,metric:sum", "rooms/baseRate,metric:max", "rooms/baseRate,metric:avg", "rooms/beds,metric:cardinality,precisionThreshold:5"]
        });

        // Assert
        var rates = response.SearchFacets!["rooms/baseRate"];
        Assert.Equal(969.99, rates[0].Sum!.Value, 6);
        Assert.Equal(620, rates[1].Max);
        Assert.Equal(242.4975, rates[2].Avg!.Value, 6);
        Assert.Equal(3, response.SearchFacets["rooms/beds"].Single().Cardinality);
    }

    [Theory]
    [InlineData("kind,interval:5", "Intervals are not supported in facets for 'kind'. They are only supported on integer and dateTime fields.")]
    [InlineData("rate,interval:0.5", "The value '0.5' in facet specification for field 'rate' is not a valid integer greater than zero.")]
    [InlineData("opened,interval:Month", "The value 'Month' in facet specification for field 'opened' is incorrect. Please use one of the following values for date histogram intervals: year, quarter, month, week, day, hour, minute.")]
    [InlineData("rate,interval:100,count:5", "Error in facet specification for field 'rate': The 'sort' and 'count' faceting options cannot be used with the 'interval' or 'values' options.")]
    [InlineData("rate,metric:sum,count:5", "Error in facet specification for field 'rate': The 'metric' faceting option cannot be used with other faceting options in the same specification.")]
    [InlineData("rate,metric:SUM", "Invalid metric parameter in facet for 'rate'.")]
    [InlineData("kind,values:a", "Invalid facet parameter for field 'kind'. Values can only be specified in facets for fields of type Edm.Int32, Edm.Int64, Edm.Double, or Edm.DateTimeOffset.")]
    [InlineData("floors,values:10.5", "The value '10.5' in facet specification for field 'floors' is not a valid 'Edm.Int32' value.")]
    [InlineData("kind,sort:foo", "Invalid value parameter in facet for 'kind'.")]
    [InlineData("kind,Count:1", "Unknown parameter 'Count' in facet specification for field 'kind'.")]
    [InlineData("kind,count:-1", "Invalid count parameter in facet for 'kind'.")]
    [InlineData("opened,timeoffset:+01:00", "Error in facet specification for field 'opened': The 'timeoffset' faceting option must only be used with the 'interval' option.")]
    [InlineData("opened,interval:day,timeoffset:abc", "Invalid timeoffset value 'abc' for field 'opened'. The timeoffset value must given in the form [+-]hh:mm, [+-]hhmm, or [+-]hh.")]
    [InlineData("kind,precisionThreshold:100", "Error in facet specification for field 'kind': The 'precisionThreshold' faceting option must only be used when the 'metric: cardinality' option is specified.")]
    [InlineData("kind,metric:cardinality,precisionThreshold:abc", "Invalid 'precisionThreshold' parameter in facet for 'kind'. The value must be an integer between 0 and 40000.")]
    [InlineData("kind,includeTermFilter:/a.*/", "Regular expression facet filtering (includeTermFilter, excludeTermFilter) isn't supported by the simulator: 'kind,includeTermFilter:/a.*/'.")]
    public async Task Facets_WithAnInvalidExpression_FailWithAzuresMessage(string facet, string message)
    {
        // Arrange
        var indexName = await SeedBucketFacetIndexAsync();

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Facets = [facet] }));

        // Assert
        Assert.Equal($"{message}\r\nParameter name: facet", ex.Message);
    }

    [Theory]
    [InlineData("Evaluation error: Duplicate facet specifications are specified for field: 'kind'.", "kind", "kind,count:1")]
    [InlineData("Evaluation error: Duplicate metric facet specifications are specified for fields: 'rate', type: 'sum'.", "rate,metric:sum", "rate,metric:sum")]
    public async Task Facets_RepeatedOnAField_FailWithAzuresMessage(string message, params string[] facets)
    {
        // Arrange
        var indexName = await SeedBucketFacetIndexAsync();

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Facets = [.. facets] }));

        // Assert
        Assert.Equal($"{message}\r\nParameter name: facet", ex.Message);
    }

    private async Task<string> SeedBucketFacetIndexAsync()
    {
        var indexName = $"facet-buckets-{Guid.NewGuid():N}";
        RegisterIndex(new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "rate", Type = "Edm.Double", Facetable = true },
                new() { Name = "floors", Type = "Edm.Int32", Facetable = true },
                new() { Name = "opened", Type = "Edm.DateTimeOffset", Facetable = true },
                new() { Name = "kind", Type = "Edm.String", Facetable = true },
                new() { Name = "nums", Type = "Collection(Edm.Int32)", Facetable = true },
                new()
                {
                    Name = "rooms",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "beds", Type = "Edm.Int32", Facetable = true },
                        new() { Name = "baseRate", Type = "Edm.Double", Facetable = true }
                    }
                }
            }
        });

        Dictionary<string, object?> Room(int beds, double baseRate) => new() { ["beds"] = beds, ["baseRate"] = baseRate };
        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["rate"] = 95.5, ["floors"] = 3, ["opened"] = "2024-01-15T10:00:00Z", ["kind"] = "b", ["nums"] = new[] { 1, 2, 2 }, ["rooms"] = new[] { Room(1, 100), Room(2, 150) } },
            new Dictionary<string, object?> { ["id"] = "2", ["rate"] = 250.0, ["floors"] = 12, ["opened"] = "2024-03-02T23:30:00Z", ["kind"] = "a", ["nums"] = new[] { 2, 3 }, ["rooms"] = new[] { Room(2, 99.99) } },
            new Dictionary<string, object?> { ["id"] = "3", ["rate"] = -20.0, ["floors"] = 1, ["opened"] = "2023-12-31T23:59:59Z", ["kind"] = "b", ["nums"] = Array.Empty<int>(), ["rooms"] = new[] { Room(4, 620) } },
            new Dictionary<string, object?> { ["id"] = "4", ["floors"] = 3, ["kind"] = "c", ["nums"] = new[] { 7 } },
            new Dictionary<string, object?> { ["id"] = "5", ["kind"] = "a" });

        return indexName;
    }

    private async Task<string> SeedValueFacetIndexAsync()
    {
        var indexName = $"facet-values-{Guid.NewGuid():N}";
        RegisterIndex(new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "city", Type = "Edm.String", Searchable = true, Facetable = true },
                new() { Name = "tags", Type = "Collection(Edm.String)", Facetable = true },
                new() { Name = "opened", Type = "Edm.DateTimeOffset", Facetable = true },
                new()
                {
                    Name = "rooms",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "type", Type = "Edm.String", Facetable = true },
                        new() { Name = "rate", Type = "Edm.Double", Facetable = true }
                    }
                }
            }
        });

        var opened = "2024-03-05T10:20:30.1234567+02:00";
        var rooms = new[]
        {
            new Dictionary<string, object?> { ["type"] = "suite", ["rate"] = 300.0 },
            new Dictionary<string, object?> { ["type"] = "suite" },
            new Dictionary<string, object?> { ["type"] = "single" },
        };
        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["city"] = "Paris", ["tags"] = new[] { "a", "a", "b" }, ["opened"] = opened, ["rooms"] = rooms },
            new Dictionary<string, object?> { ["id"] = "2", ["city"] = "paris", ["tags"] = new[] { "a" }, ["opened"] = opened, ["rooms"] = new[] { rooms[2] } },
            new Dictionary<string, object?> { ["id"] = "3", ["city"] = "New York", ["tags"] = new[] { "a" }, ["opened"] = opened },
            new Dictionary<string, object?> { ["id"] = "4", ["city"] = "New York", ["opened"] = opened });

        return indexName;
    }

    public void Dispose()
    {
        _luceneManager?.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
