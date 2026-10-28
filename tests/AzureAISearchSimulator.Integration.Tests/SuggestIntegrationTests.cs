using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;

namespace AzureAISearchSimulator.Integration.Tests;

public class SuggestIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public SuggestIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "suggest-tests", Guid.NewGuid().ToString());
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

    private static SearchIndex CreatePurchaserIndex(string indexName)
    {
        return new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "Id", Type = "Edm.String", Key = true },
                new() { Name = "PurchaserLastName", Type = "Edm.String", Searchable = true },
                new() { Name = "LocationCode", Type = "Edm.String", Filterable = true },
                new()
                {
                    Name = "Tiles",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "PendingPaymentCount", Type = "Edm.Int32", Filterable = true }
                    }
                },
                new()
                {
                    Name = "Items",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "Sku", Type = "Edm.String" }
                    }
                }
            },
            Suggesters = new List<Suggester>
            {
                new() { Name = "sg", SearchMode = "analyzingInfixMatching", SourceFields = new List<string> { "PurchaserLastName" } }
            }
        };
    }

    private async Task<string> CreatePurchaserIndexWithDocuments()
    {
        var indexName = $"suggest-{Guid.NewGuid():N}";
        RegisterIndex(CreatePurchaserIndex(indexName));

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["Id"] = "1", ["PurchaserLastName"] = "Smithson", ["LocationCode"] = "0705",
                ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 2 },
                ["Items"] = new List<object?> { new Dictionary<string, object?> { ["Sku"] = "A-1" } }
            },
            new Dictionary<string, object?>
            {
                ["Id"] = "2", ["PurchaserLastName"] = "Smirnov", ["LocationCode"] = "0001",
                ["Tiles"] = new Dictionary<string, object?> { ["PendingPaymentCount"] = 0 },
                ["Items"] = new List<object?>()
            },
            new Dictionary<string, object?>
            {
                ["Id"] = "3", ["PurchaserLastName"] = "Jones", ["LocationCode"] = "0705"
            });

        return indexName;
    }

    private static List<string?> Ids(SuggestResponse response) =>
        response.Value.Select(r => r["Id"]?.ToString()).OrderBy(id => id).ToList();

    [Fact]
    public async Task Suggest_WithoutFilter_ReturnsAllPrefixMatches()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "sg"
        });

        Assert.Equal(new List<string?> { "1", "2" }, Ids(response));
    }

    [Fact]
    public async Task Suggest_WithEqFilterAndHighlightTags_ReturnsOnlyMatchingDocumentHighlighted()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "sg",
            Filter = "LocationCode eq '0705'",
            HighlightPreTag = "<b>",
            HighlightPostTag = "</b>"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal("1", result["Id"]?.ToString());
        Assert.Equal("<b>Smi</b>thson", result.Text);
    }

    [Fact]
    public async Task Suggest_WithSearchInFilter_ReturnsOnlyMatchingDocuments()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "sg",
            Filter = "search.in(LocationCode, '0001,9999')"
        });

        Assert.Equal(new List<string?> { "2" }, Ids(response));
    }

    [Fact]
    public async Task Suggest_WithNotSearchInFilter_ExcludesMatchingDocuments()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "sg",
            Filter = "not search.in(LocationCode, '0001')"
        });

        Assert.Equal(new List<string?> { "1" }, Ids(response));
    }

    [Fact]
    public async Task Suggest_WithoutHighlightTags_ReturnsPlainText()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "jon",
            SuggesterName = "sg"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal("Jones", result.Text);
    }

    [Fact]
    public async Task Suggest_WithoutSelect_ReturnsOnlyKeyAndText()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "jon",
            SuggesterName = "sg"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal(new[] { "@search.text", "Id" }, result.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Suggest_WithSelectIncludingComplexFields_ReturnsSelectedFields()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smiths",
            SuggesterName = "sg",
            Select = "Id,PurchaserLastName,LocationCode,Tiles,Items"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal("Smithson", result["PurchaserLastName"]?.ToString());
        Assert.Equal("0705", result["LocationCode"]?.ToString());

        var tiles = Assert.IsType<JsonElement>(result["Tiles"]);
        Assert.Equal(2, tiles.GetProperty("PendingPaymentCount").GetInt32());

        var items = Assert.IsType<JsonElement>(result["Items"]);
        Assert.Equal("A-1", items[0].GetProperty("Sku").GetString());
    }

    [Fact]
    public async Task Suggest_WithSelectStar_ReturnsAllFields()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "jon",
            SuggesterName = "sg",
            Select = "*"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal("Jones", result["PurchaserLastName"]?.ToString());
        Assert.Equal("0705", result["LocationCode"]?.ToString());
    }

    [Fact]
    public async Task Suggest_MatchesOnlySuggesterSourceFields()
    {
        var indexName = $"suggest-sources-{Guid.NewGuid():N}";
        var index = CreatePurchaserIndex(indexName);
        index.Fields.Add(new SearchField { Name = "Notes", Type = "Edm.String", Searchable = true });
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["Id"] = "1", ["PurchaserLastName"] = "Jones", ["Notes"] = "smith referral" });

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "sg"
        });

        Assert.Empty(response.Value);
    }

    [Fact]
    public async Task Suggest_WithUnknownSuggester_Throws()
    {
        var indexName = await CreatePurchaserIndexWithDocuments();

        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi",
            SuggesterName = "missing"
        }));
    }

    [Fact]
    public async Task Suggest_WithMultipleTerms_RequiresAllTermsAndHighlightsEach()
    {
        var indexName = $"suggest-multi-{Guid.NewGuid():N}";
        var index = CreatePurchaserIndex(indexName);
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["Id"] = "1", ["PurchaserLastName"] = "Smith Jones" },
            new Dictionary<string, object?> { ["Id"] = "2", ["PurchaserLastName"] = "Smith Brown" });

        var response = await _searchService.SuggestAsync(indexName, new SuggestRequest
        {
            Search = "smi jo",
            SuggesterName = "sg",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>"
        });

        var result = Assert.Single(response.Value);
        Assert.Equal("<em>Smi</em>th <em>Jo</em>nes", result.Text);
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
