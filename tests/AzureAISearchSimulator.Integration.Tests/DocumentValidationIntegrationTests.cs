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
    [InlineData("""{"id":"1","rating":true}""")]
    [InlineData("""{"id":"1","rating":"abc"}""")]
    [InlineData("""{"id":"1","rating":"infinity"}""")]
    [InlineData("""{"id":"1","rating":"Infinity "}""")]
    [InlineData("""{"id":"1","count":true}""")]
    [InlineData("""{"id":"1","count":1.5}""")]
    [InlineData("""{"id":"1","count":5.0}""")]
    [InlineData("""{"id":"1","count":1e2}""")]
    [InlineData("""{"id":"1","count":"5.0"}""")]
    [InlineData("""{"id":"1","count":1e-29}""")]
    [InlineData("""{"id":"1","count":1.00000000000000000000000000001}""")]
    [InlineData("""{"id":"1","count":3e9}""")]
    [InlineData("""{"id":"1","big":9223372036854775807.5}""")]
    [InlineData("""{"id":"1","count":2147483648}""")]
    [InlineData("""{"id":"1","count":-2147483649}""")]
    [InlineData("""{"id":"1","big":9223372036854775808}""")]
    [InlineData("""{"id":"1","big":1e2}""")]
    [InlineData("""{"id":"1","big":"5"}""")]
    [InlineData("""{"id":"1","active":"1"}""")]
    [InlineData("""{"id":"1","active":1}""")]
    [InlineData("""{"id":"1","updated":"yesterday"}""")]
    [InlineData("""{"id":"1","tags":"a"}""")]
    [InlineData("""{"id":"1","tags":[1]}""")]
    [InlineData("""{"id":"1","address":"Paris"}""")]
    [InlineData("""{"id":"1","contacts":[{"name":1}]}""")]
    [InlineData("""{"id":"1","tags":["a",null]}""")]
    [InlineData("""{"id":"1","contacts":[null]}""")]
    [InlineData("""{"id":"1","updated":"2024-03-05T10:20:30"}""")]
    [InlineData("""{"id":"1","updated":"2024-03"}""")]
    [InlineData("""{"id":"1","updated":"2024-03-05T24:00:00Z"}""")]
    [InlineData("""{"id":"1","updated":" 2024-03-05T10:20:30Z "}""")]
    [InlineData("""{"id":"1","location":{}}""")]
    [InlineData("""{"id":"1","location":[2,48]}""")]
    [InlineData("""{"id":"1","location":{"type":"point","coordinates":[2,48]}}""")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[2]}}""")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":["2",48]}}""")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[2,48],"crs":{"type":"link"}}}""")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[200,0]}}""")]
    [InlineData("""{"id":"1","location":{"type":"LineString","coordinates":[1,2]}}""")]
    [InlineData("""{"id":"1","embedding":[0.1,0.2]}""")]
    [InlineData("""{"id":"1","embedding":[0.1,0.2,0.3,0.4]}""")]
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
            ("upload", """{"id":"5","rating":"NaN","scores":["INF",1.5,"-INF"],"updated":"2024-03-05T10:20+02:00","location":{"type":"Point","coordinates":[-122.13,47.67]},"embedding":[0.1,0.2,0.3]}"""),
            ("delete", """{"id":"3"}"""));

        // Assert
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "a-b_c=1"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "2"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "4"));
        Assert.NotNull(await _documentService.GetDocumentAsync(indexName, "5"));
    }

    [Fact]
    public async Task IndexDocumentsAsync_ConvertsValuesAsAzureReadsThem()
    {
        // Arrange
        var indexName = SeedIndex();

        // Act
        await IndexAsync(indexName, ("upload", """{"id":"1","rating":"1,000.5","count":" +5 ","big":2.5,"active":" TRUE ","updated":"2024-03-05","tags":["x"],"scores":["nan","-Inf","Infinity",2,1e400],"location":{"coordinates":[2,48,10],"type":"Point","bbox":[1,2,3,4]},"contacts":[{"name":"n"}]}"""));

        // Assert: Azure returns the converted values, Int64 decimals rounded half to even and points in WGS 84.
        Assert.Equal(
            """{"id":"1","title":null,"rating":1000.5,"count":5,"big":2,"active":true,"updated":"2024-03-05T00:00:00Z","tags":["x"],"scores":["NaN","-INF","INF",2.0,"INF"],"location":{"type":"Point","coordinates":[2.0,48.0],"crs":{"type":"name","properties":{"name":"EPSG:4326"}}},"embedding":null,"address":null,"contacts":[{"name":"n"}]}""",
            JsonSerializer.Serialize(await _documentService.GetDocumentAsync(indexName, "1")));
    }

    [Theory]
    [InlineData("2024-03-05", "2024-03-05T00:00:00Z")]
    [InlineData("2024-03-05+02:00", "2024-03-04T22:00:00Z")]
    [InlineData("2024-03-05T10:20Z", "2024-03-05T10:20:00Z")]
    [InlineData("2024-03-05T10:20:30z", "2024-03-05T10:20:30Z")]
    [InlineData("2024-03-05T10:20:30.12345678+14:00", "2024-03-04T20:20:30.123Z")]
    public async Task IndexDocumentsAsync_Date_IsReadAsAzureReadsIt(string date, string expected)
    {
        // Arrange
        var indexName = SeedIndex();

        // Act
        await IndexAsync(indexName, ("upload", $$"""{"id":"1","updated":"{{date}}"}"""));

        // Assert
        var document = await _documentService.GetDocumentAsync(indexName, "1");
        Assert.Equal(expected, document!["updated"]);
    }

    [Theory]
    [InlineData("""{"id":"1","count":5.0}""", "Cannot convert the literal '5.0' to the expected type 'Edm.Int32'.")]
    [InlineData("""{"id":"1","count":1e2}""", "Cannot convert the literal '100' to the expected type 'Edm.Int32'.")]
    [InlineData("""{"id":"1","big":1e19}""", "Cannot convert the literal '1E+19' to the expected type 'Edm.Int64'.")]
    [InlineData("""{"id":"1","rating":true}""", "Cannot convert the literal 'True' to the expected type 'Edm.Double'.")]
    [InlineData("""{"id":"1","big":"5"}""", "Cannot convert a value to target type 'Edm.Int64' because of conflict between input format string/number and parameter 'IEEE754Compatible' false/true.")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[2,100]}}""", "actions : 1: The data field 'location' has an invalid latitude coordinate '100'. A latitude coordinate must be a value between -90.0 and +90.0 degrees. The coordinates should be specified as [longitude, latitude].")]
    [InlineData("""{"id":"1","location":{"type":"Point","coordinates":[2]}}""", "Invalid GeoJSON. A position must contain at least two and no more than four elements.")]
    public async Task IndexDocumentsAsync_InvalidValue_FailsWithAzuresMessage(string invalid, string message)
    {
        // Arrange
        var indexName = SeedIndex();

        // Act
        var error = await Assert.ThrowsAsync<ArgumentException>(() => IndexAsync(indexName, ("upload", """{"id":"ok"}"""), ("upload", invalid)));

        // Assert
        Assert.Equal($"The request is invalid. Details: {message}", error.Message);
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
                new SearchField { Name = "embedding", Type = "Collection(Edm.Single)", Searchable = true, Dimensions = 3 },
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
