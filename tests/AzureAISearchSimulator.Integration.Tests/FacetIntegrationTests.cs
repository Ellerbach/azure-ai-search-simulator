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

    public void Dispose()
    {
        _luceneManager?.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
