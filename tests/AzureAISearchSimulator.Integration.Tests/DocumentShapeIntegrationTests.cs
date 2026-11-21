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
/// Integration tests for the shape of returned documents: as in Azure, lookups and search results contain every
/// retrievable (or selected) field, missing values are null, missing collections are empty, and dates are UTC to
/// the millisecond.
/// </summary>
public class DocumentShapeIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public DocumentShapeIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "document-shape-tests", Guid.NewGuid().ToString());
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

        _searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            Mock.Of<IVectorSearchService>(),
            _indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            new ScoringProfileService(Mock.Of<ILogger<ScoringProfileService>>()));
    }

    [Fact]
    public async Task GetDocumentAsync_WithMissingFields_ReturnsNullsAndEmptyCollections()
    {
        // Arrange
        var indexName = await SeedAsync("""{"id":"1"}""");

        // Act
        var doc = await _documentService.GetDocumentAsync(indexName, "1");

        // Assert
        Assert.NotNull(doc);
        Assert.Equal(new[] { "id", "title", "tags", "updated", "address", "contacts" }, doc!.Keys);
        Assert.Null(doc["title"]);
        Assert.Empty(Assert.IsType<object?[]>(doc["tags"]));
        Assert.Null(doc["updated"]);
        Assert.Null(doc["address"]);
        Assert.Empty(Assert.IsType<object?[]>(doc["contacts"]));
    }

    [Fact]
    public async Task GetDocumentAsync_WithMissingSubFields_ReturnsThemAsNull()
    {
        // Arrange
        var indexName = await SeedAsync("""{"id":"1","address":{"city":"Paris"},"contacts":[{}]}""");

        // Act
        var doc = await _documentService.GetDocumentAsync(indexName, "1");

        // Assert
        var address = Assert.IsType<Dictionary<string, object?>>(doc!["address"]);
        Assert.Equal("Paris", address["city"]?.ToString());
        Assert.Null(address["zip"]);
        var contact = Assert.IsType<Dictionary<string, object?>>(Assert.Single(Assert.IsType<List<object?>>(doc["contacts"])));
        Assert.Null(contact["name"]);
    }

    [Theory]
    [InlineData("2024-03-05T10:20:30.1239999+02:00", "2024-03-05T08:20:30.123Z")]
    [InlineData("2024-03-05T10:20:30.5Z", "2024-03-05T10:20:30.5Z")]
    [InlineData("2024-03-05T10:20:30Z", "2024-03-05T10:20:30Z")]
    [InlineData("2024-03-05T10:20:30.0004Z", "2024-03-05T10:20:30Z")]
    public async Task GetDocumentAsync_WithDate_ReturnsItInUtcToTheMillisecond(string stored, string expected)
    {
        // Arrange
        var indexName = await SeedAsync($$"""{"id":"1","updated":"{{stored}}"}""");

        // Act
        var doc = await _documentService.GetDocumentAsync(indexName, "1");

        // Assert
        Assert.Equal(expected, doc!["updated"]);
    }

    [Fact]
    public async Task GetDocumentAsync_WithSelectedSubField_ReturnsOnlyIt()
    {
        // Arrange
        var indexName = await SeedAsync("""{"id":"1","title":"t","address":{"city":"Paris","zip":"75001"}}""");

        // Act
        var doc = await _documentService.GetDocumentAsync(indexName, "1", ["address/city"]);

        // Assert
        Assert.Equal(new[] { "address" }, doc!.Keys);
        var address = Assert.IsType<Dictionary<string, object?>>(doc["address"]);
        Assert.Equal(new[] { "city" }, address.Keys);
    }

    [Fact]
    public async Task SearchAsync_Results_HaveTheShapeOfLookups()
    {
        // Arrange
        var indexName = await SeedAsync("""{"id":"1","updated":"2024-03-05T10:20:30.1234567Z"}""");

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Select = "id, tags, updated" });

        // Assert
        var result = Assert.Single(response.Value);
        Assert.Equal("1", result["id"]?.ToString());
        Assert.Empty(Assert.IsType<object?[]>(result["tags"]));
        Assert.Equal("2024-03-05T10:20:30.123Z", result["updated"]);
        Assert.False(result.ContainsKey("title"));
    }

    private async Task<string> SeedAsync(string json)
    {
        var indexName = $"document-shape-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "secret", Type = "Edm.String", Retrievable = false },
                new SearchField { Name = "tags", Type = "Collection(Edm.String)", Filterable = true },
                new SearchField { Name = "updated", Type = "Edm.DateTimeOffset", Filterable = true },
                new SearchField
                {
                    Name = "address",
                    Type = "Edm.ComplexType",
                    Fields =
                    [
                        new SearchField { Name = "city", Type = "Edm.String" },
                        new SearchField { Name = "zip", Type = "Edm.String" },
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

        var action = new IndexAction { ["@search.action"] = "upload" };
        foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!)
        {
            action[name] = value;
        }

        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest { Value = [action] });
        return indexName;
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
