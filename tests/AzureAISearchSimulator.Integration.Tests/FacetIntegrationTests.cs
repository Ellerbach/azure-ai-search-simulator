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
    public async Task Facets_MetricSum_OnNonNumericField_IsSkipped()
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

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category,metric:sum" }
        });

        // Non-numeric sum facet is skipped with a warning, not an error
        Assert.True(response.SearchFacets == null || !response.SearchFacets.ContainsKey("category"));
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
    public async Task Facets_MetricMinMax_OnNonNumericField_IsSkipped()
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

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "category,metric:min" }
        });

        Assert.True(response.SearchFacets == null || !response.SearchFacets.ContainsKey("category"));
    }

    [Fact]
    public async Task Facets_UnsupportedMetric_IsSkipped()
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

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "price,metric:median" }
        });

        Assert.True(response.SearchFacets == null || !response.SearchFacets.ContainsKey("price"));
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

    public void Dispose()
    {
        _luceneManager?.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
