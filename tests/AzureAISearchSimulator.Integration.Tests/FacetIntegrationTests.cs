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

    public void Dispose()
    {
        _luceneManager?.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
