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
                new() { Name = "tagsCi", Type = "Collection(Edm.String)", Filterable = true, Normalizer = "lowercase" },
                new() { Name = "roomNumbers", Type = "Collection(Edm.Int32)", Filterable = true },
                new() { Name = "bookingIds", Type = "Collection(Edm.Int64)", Filterable = true },
                new() { Name = "floorAreas", Type = "Collection(Edm.Double)", Filterable = true }
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
                ["tags"] = new[] { "luxury", "spa", "pool", "wifi", "O'Brien" },
                ["tagsCi"] = new[] { "WiFi", "Pool" },
                ["roomNumbers"] = new[] { 101, 102, 205 },
                ["bookingIds"] = new[] { 9000000000001, 9000000000002 },
                ["floorAreas"] = new[] { 120.5, 85.25 }
            },
            new Dictionary<string, object?>
            {
                ["id"] = "2",
                ["hotelName"] = "Budget Inn Express",
                ["tags"] = new[] { "budget", "breakfast", "wifi", "bed and breakfast" },
                ["tagsCi"] = new[] { "Breakfast" },
                ["roomNumbers"] = new[] { 301, 302 },
                ["bookingIds"] = new[] { 9000000000101 },
                ["floorAreas"] = new[] { 45.75 }
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
    public async Task Filter_AnyGt_WithUnparsableOperand_ReturnsNoDocumentsRatherThanEveryDocument()
    {
        var indexName = $"any-int-gt-invalid-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "roomNumbers/any(c: c gt invalid)"
        });

        // BuildNumericRangeQuery's operand parsing fails for "invalid" on every numeric/date
        // type it tries. The important regression to guard against: this must NOT fall back
        // to matching every document (see SearchService.BuildNumericRangeQuery).
        Assert.Empty(response.Value);
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
    public async Task Filter_AnyEq_OnInt64Collection_ReturnsOnlyMatchingDocument()
    {
        var indexName = $"any-int64-eq-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        // 9000000000002 is well outside Int32's range - this only matches if Collection(Edm.Int64)
        // is indexed/queried through the Int64 (not Int32) Lucene field encoding.
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "bookingIds/any(b: b eq 9000000000002)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyGt_OnInt64Collection_ReturnsMatchingDocument()
    {
        var indexName = $"any-int64-gt-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "bookingIds/any(b: b gt 9000000000050)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("2", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyEq_OnDoubleCollection_ReturnsOnlyMatchingDocument()
    {
        var indexName = $"any-double-eq-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "floorAreas/any(a: a eq 85.25)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyGt_OnDoubleCollection_ReturnsMatchingDocument()
    {
        var indexName = $"any-double-gt-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "floorAreas/any(a: a gt 100)"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyEq_OnNormalizedStringCollection_MatchesLiteralNormalizedLikeIndexedValue()
    {
        var indexName = $"any-normalized-eq-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        // "tagsCi" has a "lowercase" normalizer, so "WiFi" is indexed as "wifi". The query
        // literal "WIFI" must go through the same normalization to match (see
        // SearchService.BuildCollectionElementEqualityQuery).
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "tagsCi/any(t: t eq 'WIFI')"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyGt_AtInt32MaxValue_ReturnsNoDocumentsRatherThanEveryDocument()
    {
        var indexName = $"any-int-gt-maxvalue-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        // No Int32 value is greater than int.MaxValue. BuildNumericRangeQuery used to compute
        // this bound as `intValue + 1`, which overflows to int.MinValue and produces a full-range
        // (match-everything) query instead - see SearchService.BuildNumericRangeQuery.
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = $"roomNumbers/any(c: c gt {int.MaxValue})"
        });

        Assert.Empty(response.Value);
    }

    [Fact]
    public async Task Filter_AnyEq_StringLiteralWithEscapedQuote_MatchesDecodedValue()
    {
        var indexName = $"any-string-eq-escaped-quote-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        // OData escapes a literal single quote inside a string literal as '' (doubled). The eq
        // regex used to require an unescaped '([^']*)' literal, so it couldn't match a value
        // containing a quote at all - see SearchService.ParseCollectionElementPredicate.
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "tags/any(t: t eq 'O''Brien')"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("1", doc["id"]?.ToString());
    }

    [Fact]
    public async Task Filter_AnyEq_StringLiteralContainingAndKeyword_IsNotSplitAtTopLevel()
    {
        var indexName = $"any-string-eq-and-literal-{Guid.NewGuid():N}";
        await SeedHotels(indexName);

        // BuildFilterQuery used to split the whole filter string on " and ", including inside a
        // quoted literal, breaking this into two unparseable fragments - see
        // SearchService.SplitTopLevelAndClauses.
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "tags/any(t: t eq 'bed and breakfast')"
        });

        var doc = Assert.Single(response.Value);
        Assert.Equal("2", doc["id"]?.ToString());
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
