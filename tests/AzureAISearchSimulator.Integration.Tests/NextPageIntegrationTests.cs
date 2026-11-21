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
/// Integration tests for server-side paging: as in Azure, a search without <c>top</c> returns 50 results and, when
/// more remain, the request for the next page in <c>@search.nextPageParameters</c>.
/// </summary>
public class NextPageIntegrationTests : IDisposable
{
    private const int DocumentCount = 120;

    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public NextPageIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "next-page-tests", Guid.NewGuid().ToString());
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
    public async Task SearchAsync_WithoutTop_ReturnsFiftyResultsAndTheNextPage()
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Filter = "Group eq 'a'" });

        // Assert
        Assert.Equal(50, response.Value.Count);
        Assert.NotNull(response.NextPageParameters);
        Assert.Equal(50, response.NextPageParameters!.Skip);
        Assert.Equal("Group eq 'a'", response.NextPageParameters.Filter);
    }

    [Fact]
    public async Task SearchAsync_FollowingTheNextPages_ReturnsEveryDocumentOnce()
    {
        // Arrange
        var indexName = await SeedAsync();
        SearchRequest? request = new() { Search = "*", OrderBy = "id" };
        var ids = new List<string>();

        // Act
        while (request != null)
        {
            var response = await _searchService.SearchAsync(indexName, request);
            ids.AddRange(response.Value.Select(r => r["id"]!.ToString()!));
            request = response.NextPageParameters;
        }

        // Assert
        Assert.Equal(DocumentCount, ids.Count);
        Assert.Equal(DocumentCount, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public async Task SearchAsync_WithTop_ReturnsNoNextPage(int top)
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Top = top });

        // Assert
        Assert.Equal(top, response.Value.Count);
        Assert.Null(response.NextPageParameters);
        Assert.Null(response.ODataNextLink);
    }

    [Fact]
    public async Task SearchAsync_WithoutTopOnTheLastPage_ReturnsNoNextPage()
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest { Search = "*", Skip = 100 });

        // Assert
        Assert.Equal(DocumentCount - 100, response.Value.Count);
        Assert.Null(response.NextPageParameters);
    }

    private async Task<string> SeedAsync()
    {
        var indexName = $"next-page-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true, Filterable = true, Sortable = true },
                new SearchField { Name = "Group", Type = "Edm.String", Filterable = true },
            ],
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);

        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value = Enumerable.Range(0, DocumentCount).Select(i => new IndexAction
            {
                ["@search.action"] = "upload",
                ["id"] = $"d{i:D3}",
                ["Group"] = "a",
            }).ToList(),
        });

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
