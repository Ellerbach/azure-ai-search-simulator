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

public class SearchInFilterIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public SearchInFilterIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "search-in-filter-tests", Guid.NewGuid().ToString());
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
                foreach (var kvp in doc)
                {
                    action[kvp.Key] = kvp.Value;
                }
                return action;
            }).ToList()
        };

        await _documentService.IndexDocumentsAsync(indexName, request);
    }

    private static SearchIndex CreateCategoryIndex(string indexName)
    {
        return new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Searchable = true, Filterable = true }
            }
        };
    }

    [Fact]
    public async Task Filter_SearchIn_WithDefaultCommaDelimiter_ReturnsMatchingDocuments()
    {
        var indexName = $"searchin-comma-{Guid.NewGuid():N}";
        var index = CreateCategoryIndex(indexName);
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Grand Palace", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Ocean View", ["category"] = "Resort" },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Town Stay", ["category"] = "Budget" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "search.in(category, 'Luxury,Resort')"
        });

        Assert.Equal(2, response.Value.Count);
    }

    [Fact]
    public async Task Filter_SearchIn_WithCustomDelimiter_ReturnsMatchingDocuments()
    {
        var indexName = $"searchin-pipe-{Guid.NewGuid():N}";
        var index = CreateCategoryIndex(indexName);
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Grand Palace", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Ocean View", ["category"] = "Resort" },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "City Central", ["category"] = "Boutique" },
            new Dictionary<string, object?> { ["id"] = "4", ["hotelName"] = "Town Stay", ["category"] = "Budget" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "search.in(category, 'Luxury|Resort|Boutique', '|')"
        });

        Assert.Equal(3, response.Value.Count);
    }

    [Fact]
    public async Task Filter_SearchIn_WithMultipleDelimiterCharacters_ReturnsMatchingDocuments()
    {
        var indexName = $"searchin-multi-{Guid.NewGuid():N}";
        var index = CreateCategoryIndex(indexName);
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Grand Palace", ["category"] = "Luxury" },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Ocean View", ["category"] = "Resort" },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "City Central", ["category"] = "Boutique" },
            new Dictionary<string, object?> { ["id"] = "4", ["hotelName"] = "Town Stay", ["category"] = "Budget" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "search.in(category, 'Luxury|Resort;Boutique', '|;')"
        });

        Assert.Equal(3, response.Value.Count);
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