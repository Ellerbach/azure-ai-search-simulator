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
/// Integration tests for batch validation: as in Azure, a batch with any invalid action (bad key, unknown property,
/// value of the wrong type) is rejected as a whole, before any of its actions is applied.
/// </summary>
public class DocumentValidationIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;

    public DocumentValidationIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "document-validation-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            Options.Create(new LuceneSettings { IndexPath = _testDir }));

        _indexServiceMock = new Mock<IIndexService>();

        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object);
    }

    [Theory]
    [InlineData("""{"title":"no key"}""")]
    [InlineData("""{"id":""}""")]
    [InlineData("""{"id":"a.b"}""")]
    [InlineData("""{"id":"a b"}""")]
    [InlineData("""{"id":"_a"}""")]
    [InlineData("""{"id":"abc\n"}""")]
    [InlineData("""{"id":"1","unknown":1}""")]
    [InlineData("""{"id":"1","address":{"unknown":1}}""")]
    [InlineData("""{"id":"1","title":5}""")]
    [InlineData("""{"id":"1","title":{"a":1}}""")]
    [InlineData("""{"id":"1","rating":"5"}""")]
    [InlineData("""{"id":"1","count":true}""")]
    [InlineData("""{"id":"1","count":1.5}""")]
    [InlineData("""{"id":"1","count":1e-29}""")]
    [InlineData("""{"id":"1","count":1.00000000000000000000000000001}""")]
    [InlineData("""{"id":"1","count":3e9}""")]
    [InlineData("""{"id":"1","big":9223372036854775807.5}""")]
    [InlineData("""{"id":"1","count":2147483648}""")]
    [InlineData("""{"id":"1","count":-2147483649}""")]
    [InlineData("""{"id":"1","big":9223372036854775808}""")]
    [InlineData("""{"id":"1","big":0.5}""")]
    [InlineData("""{"id":"1","active":"true"}""")]
    [InlineData("""{"id":"1","updated":"yesterday"}""")]
    [InlineData("""{"id":"1","tags":"a"}""")]
    [InlineData("""{"id":"1","tags":[1]}""")]
    [InlineData("""{"id":"1","address":"Paris"}""")]
    [InlineData("""{"id":"1","contacts":[{"name":1}]}""")]
    [InlineData("""{"id":"1","tags":["a",null]}""")]
    [InlineData("""{"id":"1","contacts":[null]}""")]
    [InlineData("""{"id":"1","updated":"2024-03-05T10:20:30"}""")]
    [InlineData("""{"id":"1","updated":"2024-03-05"}""")]
    [InlineData("""{"id":"1","rating":"nan"}""")]
    [InlineData("""{"id":"1","location":{}}""")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[200,0]}}""")]
    [InlineData("""{"id":"1","location":{"type":"LineString","coordinates":[1,2]}}""")]
    public async Task IndexDocumentsAsync_InvalidAction_RejectsTheWholeBatch(string invalid)
    {
        // Arrange
        var indexName = SeedIndex();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => IndexAsync(indexName, ("upload", """{"id":"ok"}"""), ("upload", invalid)));
        Assert.Null(await _documentService.GetDocumentAsync(indexName, "ok"));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("upload ")]
    public async Task IndexDocumentsAsync_UnknownSearchAction_RejectsTheWholeBatch(string action)
    {
        // Arrange
        var indexName = SeedIndex();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => IndexAsync(indexName, ("upload", """{"id":"ok"}"""), (action, """{"id":"1"}""")));
        Assert.Null(await _documentService.GetDocumentAsync(indexName, "ok"));
        Assert.Null(await _documentService.GetDocumentAsync(indexName, "1"));
    }

    [Fact]
    public async Task IndexDocumentsAsync_ValidActions_AreApplied()
    {
        // Arrange
        var indexName = SeedIndex();

        // Act
        await IndexAsync(indexName,
            ("upload", """{"id":"a-b_c=1","title":"t","rating":4.5,"count":-2147483648,"big":9223372036854775807,"active":true,"updated":"2024-03-05T10:20:30Z","tags":["x"],"address":{"city":"Paris"},"contacts":[{"name":"n"}]}"""),
            ("upload", """{"id":"2","title":null,"tags":null,"address":null}"""),
            ("MergeOrUpload", """{"id":"4"}"""),
            ("upload", """{"id":"5","rating":"NaN","scores":["INF",1.5,"-INF"],"updated":"2024-03-05T10:20+02:00","location":{"type":"Point","coordinates":[-122.13,47.67]}}"""),
            ("delete", """{"id":"3"}"""));

        // Assert
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "a-b_c=1"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "2"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "4"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "5"));
    }

    private string SeedIndex()
    {
        var indexName = $"document-validation-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "rating", Type = "Edm.Double", Filterable = true },
                new SearchField { Name = "count", Type = "Edm.Int32", Filterable = true },
                new SearchField { Name = "big", Type = "Edm.Int64", Filterable = true },
                new SearchField { Name = "active", Type = "Edm.Boolean", Filterable = true },
                new SearchField { Name = "updated", Type = "Edm.DateTimeOffset", Filterable = true },
                new SearchField { Name = "tags", Type = "Collection(Edm.String)", Filterable = true },
                new SearchField { Name = "scores", Type = "Collection(Edm.Double)", Filterable = true },
                new SearchField { Name = "location", Type = "Edm.GeographyPoint", Filterable = true },
                new SearchField
                {
                    Name = "address",
                    Type = "Edm.ComplexType",
                    Fields = [new SearchField { Name = "city", Type = "Edm.String" }],
                },
                new SearchField
                {
                    Name = "contacts",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = [new SearchField { Name = "name", Type = "Edm.String" }],
                },
            ],
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);
        return indexName;
    }

    private async Task IndexAsync(string indexName, params (string Type, string Json)[] actions)
    {
        var request = new IndexDocumentsRequest
        {
            Value = actions.Select(a =>
            {
                var action = new IndexAction { ["@search.action"] = a.Type };
                foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, object?>>(a.Json)!)
                {
                    action[name] = value;
                }

                return action;
            }).ToList(),
        };

        var response = await _documentService.IndexDocumentsAsync(indexName, request);
        Assert.All(response.Value, r => Assert.True(r.Status, r.ErrorMessage));
    }

    public void Dispose()
    {
        _luceneManager.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
