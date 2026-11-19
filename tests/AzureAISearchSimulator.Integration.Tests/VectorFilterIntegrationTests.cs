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
/// Integration tests for vector queries: as in Azure, with a <c>$filter</c> the k nearest neighbours are taken among the
/// documents that pass it (pre-filtering), neighbours are ranked by the field's metric with or without a filter, a vector
/// query without search text returns only them, and a hybrid query counts both its text matches and its neighbours.
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
    public async Task SearchAsync_VectorQueryWithFilterMatchingOnlyTheFarthestDocument_ReturnsIt()
    {
        // Arrange: 59 nearer documents fail the filter, more than any oversampling of k = 1 would look at.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 1);
        request.Filter = "id eq 'd59'";

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d59"], Ids(response));
    }

    [Theory]
    [InlineData(null, "d1")]
    [InlineData("preFilter", "d1")]
    [InlineData("postFilter", "")]
    [InlineData("strictPostFilter", "")]
    public async Task SearchAsync_VectorQueryWithFilterMode_AppliesTheFilterBeforeOrAfterTheNeighbours(string? mode, string expected)
    {
        // Arrange: the nearest document, d0, fails the filter.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 1);
        request.VectorFilterMode = mode;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(expected, string.Join(',', Ids(response)));
    }

    [Fact]
    public async Task SearchAsync_VectorQueryWithPostFilter_KeepsTheNeighboursThatPass()
    {
        // Arrange: of the 4 nearest documents (d0..d3), d1 and d3 pass the filter.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 4);
        request.VectorFilterMode = "postFilter";

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d1", "d3"], Ids(response));
    }

    [Theory]
    [InlineData("Embedding", 3, "unknown")]
    [InlineData("Missing", 2, null)]
    [InlineData("ClientIdentifier", 2, null)]
    [InlineData("Embedding", 3, null)]
    [InlineData("Embedding", 0, null)]
    [InlineData("", 2, null)]
    public async Task SearchAsync_InvalidVectorQuery_Throws(string field, int dimensions, string? mode)
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 1);
        request.VectorQueries![0].Fields = field;
        request.VectorQueries[0].Vector = new float[dimensions];
        request.VectorFilterMode = mode;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, request));
    }

    [Theory]
    [InlineData("vector", false)]
    [InlineData("text", false)]
    [InlineData("text", true)]
    public async Task SearchAsync_VectorQueryWithoutAVectorOrOfAnotherKind_Throws(string kind, bool withVector)
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 1);
        request.VectorQueries![0].Kind = kind;
        request.VectorQueries[0].Vector = withVector ? [1f, 0f] : null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, request));
    }

    [Fact]
    public async Task SearchAsync_ExhaustiveVectorQueryWithoutFilter_ReturnsTheExactNeighbours()
    {
        // Arrange: the approximate (HNSW) search is mocked to find nothing.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Filter = null;
        request.VectorQueries![0].Exhaustive = true;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d0", "d1", "d2"], Ids(response));
    }

    [Fact]
    public async Task SearchAsync_VectorQueryOverSeveralFieldsWithoutFilter_SearchesEachField()
    {
        // Arrange
        var indexName = await SeedAsync();
        var index = await _indexServiceMock.Object.GetIndexAsync(indexName);
        index!.Fields.Add(new SearchField { Name = "Other", Type = "Collection(Edm.Single)", Searchable = true, Dimensions = 2, VectorSearchProfile = "p" });
        var vectors = new Mock<IVectorSearchService>();
        vectors.Setup(x => x.Search(indexName, "Embedding", It.IsAny<float[]>(), 1)).Returns([new VectorSearchResult { DocumentId = "d3", Score = 0.5 }]);
        vectors.Setup(x => x.Search(indexName, "Other", It.IsAny<float[]>(), 1)).Returns([new VectorSearchResult { DocumentId = "d5", Score = 0.9 }]);
        var searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            vectors.Object,
            _indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            new ScoringProfileService(Mock.Of<ILogger<ScoringProfileService>>()));
        var request = VectorRequest(k: 1);
        request.Filter = null;
        request.VectorQueries![0].Fields = "Embedding, Other";

        // Act
        var response = await searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(["d5", "d3"], Ids(response));
    }

    [Theory]
    [InlineData("hnsw", "dotProduct", "a", true)]
    [InlineData("hnsw", "dotProduct", "a", false)]
    [InlineData("exhaustiveKnn", "euclidean", "c", true)]
    [InlineData("exhaustiveKnn", "euclidean", "c", false)]
    [InlineData("exhaustiveKnn", "cosine", "a", true)]
    [InlineData("exhaustiveKnn", "cosine", "a", false)]
    public async Task SearchAsync_VectorQuery_RanksByTheAlgorithmMetric(string kind, string metric, string expected, bool filtered)
    {
        // Arrange: for [1, 0], dot product prefers a = [3, 0], euclidean prefers c = [1, 1], cosine prefers a (then b).
        var indexName = $"vector-metric-{Guid.NewGuid():N}";
        var algorithm = new VectorSearchAlgorithm { Name = "alg", Kind = kind };
        if (kind == "hnsw")
        {
            algorithm.HnswParameters = new HnswParameters { Metric = metric };
        }
        else
        {
            algorithm.ExhaustiveKnnParameters = new ExhaustiveKnnParameters { Metric = metric };
        }

        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new SearchField { Name = "Embedding", Type = "Collection(Edm.Single)", Searchable = true, Dimensions = 2, VectorSearchProfile = "p" },
            ],
            VectorSearch = new VectorSearchConfiguration
            {
                Algorithms = [algorithm],
                Profiles = [new VectorSearchProfile { Name = "p", Algorithm = "alg" }],
            },
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);
        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value =
            [
                new IndexAction { ["@search.action"] = "upload", ["id"] = "a", ["Embedding"] = new[] { 3f, 0f } },
                new IndexAction { ["@search.action"] = "upload", ["id"] = "b", ["Embedding"] = new[] { 2.5f, 0f } },
                new IndexAction { ["@search.action"] = "upload", ["id"] = "c", ["Embedding"] = new[] { 1f, 1f } },
            ],
        });

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Filter = filtered ? "id ne 'none'" : null,
            VectorQueries = [new VectorQuery { Kind = "vector", Vector = [1f, 0f], Fields = "Embedding", K = 1 }],
        });

        // Assert
        var result = Assert.Single(response.Value);
        Assert.Equal(expected, result["id"]?.ToString());
        Assert.True(result.Score is { } score && double.IsFinite(score) && score > 0);
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

    [Fact]
    public async Task SearchAsync_HybridQueryWithCount_CountsTextMatchesAndNeighbours()
    {
        // Arrange: the text matches d51..d59, the vector neighbours are d1, d3 and d5.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Search = "alpha";
        request.Count = true;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(8, response.Value.Count);
        Assert.Equal(8, response.ODataCount);
    }

    [Fact]
    public async Task SearchAsync_HybridQueryWhoseNeighboursAlsoMatchTheText_CountsThemOnce()
    {
        // Arrange: "beta" matches d0..d49, more than the page; the neighbours d0, d1 and d2 are among them.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Filter = "ClientIdentifier ne 'nobody_1'";
        request.Search = "beta";
        request.Top = 2;
        request.Count = true;

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(50, response.ODataCount);
    }

    [Fact]
    public async Task SearchAsync_HybridQueryPagedWithSkip_ReturnsEachResultOnce()
    {
        // Arrange
        var indexName = await SeedAsync();
        var ids = new List<string>();

        // Act
        for (var skip = 0; skip < 60; skip += 7)
        {
            var request = VectorRequest(k: 5);
            request.Filter = "ClientIdentifier ne 'nobody_1'";
            request.Search = "beta";
            request.Skip = skip;
            request.Top = 7;
            ids.AddRange(Ids(await _searchService.SearchAsync(indexName, request)));
        }

        // Assert: the 50 text matches (which include the 5 neighbours), each once.
        Assert.Equal(50, ids.Count);
        Assert.Equal(50, ids.Distinct().Count());
    }

    [Fact]
    public async Task SearchAsync_VectorQueryWithFacets_CountsOnlyTheNeighbours()
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Facets = ["ClientIdentifier"];

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        var facet = Assert.Single(response.SearchFacets!["ClientIdentifier"]);
        Assert.Equal(Client, facet.Value);
        Assert.Equal(3, facet.Count);
    }

    [Fact]
    public async Task SearchAsync_HybridQueryWithFacets_CountsTextMatchesAndNeighbours()
    {
        // Arrange: the text matches d51..d59 (odd), the vector neighbours are d1, d3 and d5.
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.Search = "alpha";
        request.Facets = ["ClientIdentifier"];

        // Act
        var response = await _searchService.SearchAsync(indexName, request);

        // Assert
        Assert.Equal(8, Assert.Single(response.SearchFacets!["ClientIdentifier"]).Count);
    }

    [Fact]
    public async Task SearchAsync_VectorQueryWithInvalidOrderBy_Throws()
    {
        // Arrange
        var indexName = await SeedAsync();
        var request = VectorRequest(k: 3);
        request.OrderBy = "unknown asc";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, request));
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
                new SearchField { Name = "ClientIdentifier", Type = "Edm.String", Filterable = true, Facetable = true },
                new SearchField { Name = "Title", Type = "Edm.String", Searchable = true },
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
                ["Title"] = i >= 50 ? "alpha" : "beta",
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
