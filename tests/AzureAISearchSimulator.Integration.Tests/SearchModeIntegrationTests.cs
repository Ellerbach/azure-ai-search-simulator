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
/// Integration tests for <c>searchMode</c>: as in Azure, it decides whether unqualified terms are OR'ed (<c>any</c>,
/// the default) or AND'ed (<c>all</c>), whatever the <c>queryType</c>.
/// </summary>
public class SearchModeIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public SearchModeIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "search-mode-tests", Guid.NewGuid().ToString());
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

    [Theory]
    [InlineData("simple", null, "1,2,3")]
    [InlineData("simple", "any", "1,2,3")]
    [InlineData("simple", "all", "1")]
    [InlineData("full", null, "1,2,3")]
    [InlineData("full", "any", "1,2,3")]
    [InlineData("full", "all", "1")]
    public async Task SearchAsync_WithSearchMode_CombinesUnqualifiedTermsAsAzureDoes(string queryType, string? searchMode, string expected)
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = new SearchRequest
        {
            Search = "ocean view",
            QueryType = queryType,
        };
        if (searchMode is not null)
        {
            request.SearchMode = searchMode;
        }

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(expected.Split(','), response.Value.Select(r => r["id"]!.ToString()!).Order());
    }

    private async Task<string> SeedAsync()
    {
        var indexName = $"search-mode-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
            ],
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);

        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value =
            [
                new IndexAction { ["@search.action"] = "upload", ["id"] = "1", ["title"] = "ocean view" },
                new IndexAction { ["@search.action"] = "upload", ["id"] = "2", ["title"] = "ocean" },
                new IndexAction { ["@search.action"] = "upload", ["id"] = "3", ["title"] = "view" },
                new IndexAction { ["@search.action"] = "upload", ["id"] = "4", ["title"] = "garden" },
            ],
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
