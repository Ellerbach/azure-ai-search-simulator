using System.Text.Json;
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
/// Covers JSON null sub-field values inside Edm.ComplexType and Collection(Edm.ComplexType)
/// fields. Documents are built from JSON text (as they are when uploaded over HTTP) so nested
/// values arrive as JsonElement, and a nested null is a JsonElement of kind Null rather than C# null.
/// </summary>
public class ComplexNullSubFieldIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public ComplexNullSubFieldIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "complex-null-subfield-tests", Guid.NewGuid().ToString());
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

    private async Task<IndexDocumentsResponse> UploadJsonDocuments(string indexName, params string[] jsonDocuments)
    {
        var request = new IndexDocumentsRequest
        {
            Value = jsonDocuments.Select(json =>
            {
                var doc = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
                var action = new IndexAction { ["@search.action"] = "upload" };
                foreach (var kvp in doc)
                {
                    action[kvp.Key] = kvp.Value;
                }
                return action;
            }).ToList()
        };

        return await _documentService.IndexDocumentsAsync(indexName, request);
    }

    private static List<SearchField> CreateSubFields()
    {
        return new List<SearchField>
        {
            new() { Name = "When", Type = "Edm.DateTimeOffset", Filterable = true },
            new() { Name = "Count", Type = "Edm.Int32", Filterable = true, Facetable = true }
        };
    }

    private SearchIndex CreateAndRegisterIndex(string indexName)
    {
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "Id", Type = "Edm.String", Key = true },
                new() { Name = "Tiles", Type = "Edm.ComplexType", Fields = CreateSubFields() },
                new() { Name = "Items", Type = "Collection(Edm.ComplexType)", Fields = CreateSubFields() }
            }
        };
        RegisterIndex(index);
        return index;
    }

    private static void AssertAllSucceeded(IndexDocumentsResponse response)
    {
        Assert.All(response.Value, result => Assert.True(result.Status, $"Document '{result.Key}' failed: {result.ErrorMessage}"));
    }

    [Fact]
    public async Task Upload_ComplexFieldWithNullSubFields_Succeeds()
    {
        var indexName = $"complex-null-{Guid.NewGuid():N}";
        CreateAndRegisterIndex(indexName);

        var response = await UploadJsonDocuments(indexName,
            """{"Id":"1","Tiles":{"When":"2026-01-01T00:00:00Z","Count":3}}""",
            """{"Id":"2","Tiles":{"When":null}}""",
            """{"Id":"3","Tiles":{"Count":null}}""");

        Assert.Equal(3, response.Value.Count);
        AssertAllSucceeded(response);
        Assert.Equal(3, await _documentService.GetDocumentCountAsync(indexName));
    }

    [Fact]
    public async Task Upload_ComplexCollectionWithNullSubFields_Succeeds()
    {
        var indexName = $"complex-coll-null-{Guid.NewGuid():N}";
        CreateAndRegisterIndex(indexName);

        var response = await UploadJsonDocuments(indexName,
            """{"Id":"4","Items":[{"When":null}]}""",
            """{"Id":"5","Items":[{"When":"2026-01-01T00:00:00Z","Count":null},{"Count":2}]}""");

        Assert.Equal(2, response.Value.Count);
        AssertAllSucceeded(response);
        Assert.Equal(2, await _documentService.GetDocumentCountAsync(indexName));
    }

    [Fact]
    public async Task Facets_ComplexSubFieldWithNullValue_IsNotCountedAsZero()
    {
        // Top-level $filter on a complex sub-field path (e.g. "Tiles/Count eq 0") isn't parsed
        // by the filter engine yet, so metric facets are used to observe what was indexed:
        // a null indexed as 0 would drag min down to 0 and avg down to 10/3.
        var indexName = $"complex-null-facet-{Guid.NewGuid():N}";
        CreateAndRegisterIndex(indexName);

        AssertAllSucceeded(await UploadJsonDocuments(indexName,
            """{"Id":"1","Tiles":{"Count":4}}""",
            """{"Id":"2","Tiles":{"Count":null}}""",
            """{"Id":"3","Tiles":{"Count":6}}"""));

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Facets = new List<string> { "Tiles/Count,metric:min", "Tiles/Count,metric:avg" }
        });

        Assert.NotNull(response.SearchFacets);
        var countFacets = response.SearchFacets["Tiles/Count"];
        Assert.Equal(4.0, countFacets.Single(f => f.Min.HasValue).Min);
        Assert.Equal(5.0, countFacets.Single(f => f.Avg.HasValue).Avg);
    }

    [Fact]
    public async Task Filter_ComplexCollectionSubFieldEqZero_DoesNotMatchNullValue()
    {
        var indexName = $"complex-coll-null-filter-{Guid.NewGuid():N}";
        CreateAndRegisterIndex(indexName);

        AssertAllSucceeded(await UploadJsonDocuments(indexName,
            """{"Id":"1","Items":[{"Count":0}]}""",
            """{"Id":"2","Items":[{"Count":null}]}""",
            """{"Id":"3","Items":[{"Count":5}]}"""));

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            Filter = "Items/any(i: i/Count eq 0)"
        });

        var ids = response.Value.Select(d => d["Id"]?.ToString()).ToList();
        Assert.Equal(new[] { "1" }, ids);
    }

    [Fact]
    public async Task Lookup_DocumentWithNullSubFields_RoundTripsNulls()
    {
        var indexName = $"complex-null-lookup-{Guid.NewGuid():N}";
        CreateAndRegisterIndex(indexName);

        AssertAllSucceeded(await UploadJsonDocuments(indexName,
            """{"Id":"2","Tiles":{"When":null,"Count":null},"Items":[{"When":null,"Count":null}]}"""));

        var doc = await _documentService.GetDocumentAsync(indexName, "2");

        Assert.NotNull(doc);
        var tiles = Assert.IsType<JsonElement>(doc!["Tiles"]);
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("When").ValueKind);
        Assert.Equal(JsonValueKind.Null, tiles.GetProperty("Count").ValueKind);

        var items = Assert.IsType<JsonElement>(doc["Items"]);
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("When").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("Count").ValueKind);
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
