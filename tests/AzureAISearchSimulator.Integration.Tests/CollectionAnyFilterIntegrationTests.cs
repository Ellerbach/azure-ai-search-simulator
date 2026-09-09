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

public class CollectionAnyFilterIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public CollectionAnyFilterIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "collection-any-filter-tests", Guid.NewGuid().ToString());
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

    private static SearchIndex CreateHotelsIndex(string indexName)
    {
        return new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true, Filterable = true },
                new() { Name = "tags", Type = "Collection(Edm.String)", Searchable = true, Filterable = true },
                new() { Name = "roomNumbers", Type = "Collection(Edm.Int32)", Filterable = true }
            }
        };
    }

    private async Task<int> SeedHotels(string indexName)
    {
        var index = CreateHotelsIndex(indexName);
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["hotelName"] = "Grand Azure Hotel",
                ["tags"] = new[] { "luxury", "spa", "pool", "wifi" },
                ["roomNumbers"] = new[] { 101, 102, 205 }
            },
            new Dictionary<string, object?>
            {
                ["id"] = "2",
                ["hotelName"] = "Budget Inn Express",
                ["tags"] = new[] { "budget", "breakfast", "wifi" },
                ["roomNumbers"] = new[] { 301, 302 }
            });

        return 2;
    }

    [Fact]
    public async Task Filter_AnyEq_OnIntCollection_ReturnsOnlyMatchingDocument()
    {
        var indexName = $"any-int-eq-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "roomNumbers/any(c: c eq 101)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyEq_OnIntCollection_NoMatchingElement_ReturnsNoDocuments()
    {
        var indexName = $"any-int-eq-nomatch-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "roomNumbers/any(c: c eq 999)"
        });

        Assert.Empty(response.Value);
    }

    [Fact]
    public async Task Filter_AnyGt_OnIntCollection_ReturnsMatchingDocument()
    {
        var indexName = $"any-int-gt-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "roomNumbers/any(c: c gt 300)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("2", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyEq_OnStringCollection_ReturnsOnlyMatchingDocument()
    {
        var indexName = $"any-string-eq-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "tags/any(t: t eq 'luxury')"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnySearchIn_OnStringCollection_ReturnsAllMatchingDocuments()
    {
        var indexName = $"any-string-searchin-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "tags/any(t: search.in(t, 'spa,breakfast', ','))"
        });

        Assert.Equal(2, response.Value.Count);
    }

    [Fact]
    public async Task Filter_All_IsUnsupported_ReturnsNoDocumentsRatherThanEveryDocument()
    {
        var indexName = $"all-unsupported-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "roomNumbers/all(c: c eq 101)"
        });

        // "all()" isn't implemented (see SearchService.ParseCollectionLambdaFilter). The important
        // regression to guard against: this must NOT silently fall back to matching every document.
        Assert.Empty(response.Value);
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
