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
/// Integration tests for merge and mergeOrUpload: as in Azure, only the given values change, complex objects merge
/// recursively, and collections (including collections of complex objects) are replaced.
/// </summary>
public class DocumentMergeIntegrationTests : IDisposable
{
    private const string Original = """
        {"id":"1","title":"t","tags":["a","b"],"address":{"city":"Paris","zip":"75001","geo":{"lat":1,"lon":2}},"contacts":[{"name":"x"},{"name":"y"}]}
        """;

    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;

    public DocumentMergeIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "document-merge-tests", Guid.NewGuid().ToString());
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
    [InlineData("merge")]
    [InlineData("mergeOrUpload")]
    public async Task IndexDocumentsAsync_MergeOfComplexSubField_KeepsTheOtherSubFields(string action)
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        await IndexAsync(indexName, action, """{"id":"1","address":{"zip":"75002","geo":{"lat":3}}}""");

        // Assert
        var doc = await _documentService.GetDocumentAsync(indexName, "1");
        Assert.Equal(
            """{"city":"Paris","zip":"75002","geo":{"lat":3,"lon":2}}""",
            JsonSerializer.Serialize(doc!["address"]));
        Assert.Equal("t", doc["title"]?.ToString());
    }

    [Fact]
    public async Task IndexDocumentsAsync_MergeOfCollections_ReplacesThem()
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        await IndexAsync(indexName, "merge", """{"id":"1","tags":["c"],"contacts":[{"name":"z"}]}""");

        // Assert
        var doc = await _documentService.GetDocumentAsync(indexName, "1");
        Assert.Equal("""["c"]""", JsonSerializer.Serialize(doc!["tags"]));
        Assert.Equal("""[{"name":"z"}]""", JsonSerializer.Serialize(doc["contacts"]));
    }

    [Fact]
    public async Task IndexDocumentsAsync_MergeOfNullComplexField_ClearsIt()
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        await IndexAsync(indexName, "merge", """{"id":"1","address":null}""");

        // Assert
        var doc = await _documentService.GetDocumentAsync(indexName, "1");
        Assert.Null(doc!["address"]);
        Assert.Equal("t", doc["title"]?.ToString());
    }

    private async Task<string> SeedAsync()
    {
        var indexName = $"document-merge-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "tags", Type = "Collection(Edm.String)", Filterable = true },
                new SearchField
                {
                    Name = "address",
                    Type = "Edm.ComplexType",
                    Fields =
                    [
                        new SearchField { Name = "city", Type = "Edm.String" },
                        new SearchField { Name = "zip", Type = "Edm.String" },
                        new SearchField
                        {
                            Name = "geo",
                            Type = "Edm.ComplexType",
                            Fields =
                            [
                                new SearchField { Name = "lat", Type = "Edm.Double" },
                                new SearchField { Name = "lon", Type = "Edm.Double" },
                            ],
                        },
                    ],
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

        await IndexAsync(indexName, "upload", Original);
        return indexName;
    }

    private async Task IndexAsync(string indexName, string actionType, string json)
    {
        var action = new IndexAction { ["@search.action"] = actionType };
        foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!)
        {
            action[name] = value;
        }

        var response = await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest { Value = [action] });
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
