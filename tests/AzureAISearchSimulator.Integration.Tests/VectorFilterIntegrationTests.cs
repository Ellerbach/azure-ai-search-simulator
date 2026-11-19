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
/// Integration tests for vector queries with a <c>$filter</c>: as in Azure (pre-filtering), the k nearest neighbours
/// are taken among the documents that pass the filter, and a vector query without search text returns only them.
/// </summary>
public class VectorFilterIntegrationTests : IDisposable
{
    private const string Client = "acme_42";

    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public VectorFilterIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "vector-filter-tests", Guid.NewGuid().ToString());
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
    public async Task SearchAsync_VectorQueryWithFilter_ReturnsTheNearestMatchingDocuments()
    {
        // Arrange
        var indexName = await SeedAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, VectorRequest(k: 3));

        // Assert: d0 is the nearest overall, but belongs to another client.
        Assert.Equal(["d1", "d3", "d5"], Ids(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task SearchAsync_VectorQueryWithoutSearchText_ReturnsOnlyTheNeighboursAndCountsThem(string? search)
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Search = search;
        request.Count = true;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d1", "d3", "d5"], Ids(response));
        Assert.Equal(3, response.ODataCount);
    }

    [Fact]
    public async Task SearchAsync_VectorQueryWithSkipAndTop_PagesTheNeighbours()
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Skip = 1;
        request.Top = 1;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d3"], Ids(response));
    }

    [Fact]
    public async Task SearchAsync_VectorQueryWithFilterMatchingNothing_ReturnsNothing()
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Filter = "ClientIdentifier eq 'nobody_1'";

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Empty(response.Value);
    }

    private static SearchRequest VectorRequest(int k) => new()
    {
        Filter = $"ClientIdentifier eq '{Client}'",
        Select = "id",
        VectorQueries = [new VectorQuery { Kind = "vector", Vector = [1f, 0f], Fields = "Embedding", K = k }],
    };

    private static string[] Ids(SearchResponse response) =>
        response.Value.Select(r => r["id"]!.ToString()!).ToArray();

    private async Task<string> SeedAsync()
    {
        var indexName = $"vector-filter-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new SearchField { Name = "ClientIdentifier", Type = "Edm.String", Filterable = true },
                new SearchField { Name = "Embedding", Type = "Collection(Edm.Single)", Searchable = true, Dimensions = 2, VectorSearchProfile = "p" },
            ],
            VectorSearch = new VectorSearchConfiguration
            {
                Algorithms = [new VectorSearchAlgorithm { Name = "hnsw", Kind = "hnsw" }],
                Profiles = [new VectorSearchProfile { Name = "p", Algorithm = "hnsw" }],
            },
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);

        // Unit vectors at 0.05 rad apart: d0 is nearest to [1, 0], then d1, d2, ...; odd ones belong to the client.
        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value = Enumerable.Range(0, 60).Select(i => new IndexAction
            {
                ["@search.action"] = "upload",
                ["id"] = $"d{i}",
                ["ClientIdentifier"] = i % 2 == 1 ? Client : "acme_43",
                ["Embedding"] = new[] { (float)Math.Cos(i * 0.05), (float)Math.Sin(i * 0.05) },
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
