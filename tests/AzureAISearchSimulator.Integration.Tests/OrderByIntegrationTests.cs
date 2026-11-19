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
/// Integration tests for orderby on string and numeric fields.
/// </summary>
public class OrderByIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public OrderByIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "orderby-tests", Guid.NewGuid().ToString());
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
                foreach (var kvp in doc) action[kvp.Key] = kvp.Value;
                return action;
            }).ToList()
        };
        await _documentService.IndexDocumentsAsync(indexName, request);
    }

    [Fact]
    public async Task OrderBy_StringField_Asc_ReturnsSortedResults()
    {
        var indexName = $"sort-asc-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true, Sortable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Zephyr Hotel" },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Alpine Lodge" },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Beachside Inn" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            OrderBy = "hotelName asc"
        });

        Assert.Equal(3, response.Value.Count);
        Assert.Equal("Alpine Lodge", response.Value[0]["hotelName"]?.ToString());
        Assert.Equal("Beachside Inn", response.Value[1]["hotelName"]?.ToString());
        Assert.Equal("Zephyr Hotel", response.Value[2]["hotelName"]?.ToString());
    }

    [Fact]
    public async Task OrderBy_StringField_Desc_ReturnsSortedResults()
    {
        var indexName = $"sort-desc-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true, Sortable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Zephyr Hotel" },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Alpine Lodge" },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Beachside Inn" });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            OrderBy = "hotelName desc"
        });

        Assert.Equal(3, response.Value.Count);
        Assert.Equal("Zephyr Hotel", response.Value[0]["hotelName"]?.ToString());
        Assert.Equal("Beachside Inn", response.Value[1]["hotelName"]?.ToString());
        Assert.Equal("Alpine Lodge", response.Value[2]["hotelName"]?.ToString());
    }

    [Fact]
    public async Task OrderBy_NumericField_Asc_ReturnsSortedResults()
    {
        var indexName = $"sort-num-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Sortable = true, Filterable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?> { ["id"] = "1", ["hotelName"] = "Hotel A", ["rating"] = 4.5 },
            new Dictionary<string, object?> { ["id"] = "2", ["hotelName"] = "Hotel B", ["rating"] = 3.2 },
            new Dictionary<string, object?> { ["id"] = "3", ["hotelName"] = "Hotel C", ["rating"] = 4.9 });

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            OrderBy = "rating asc"
        });

        Assert.Equal(3, response.Value.Count);
        Assert.Equal("2", response.Value[0]["id"]?.ToString()); // 3.2
        Assert.Equal("1", response.Value[1]["id"]?.ToString()); // 4.5
        Assert.Equal("3", response.Value[2]["id"]?.ToString()); // 4.9
    }

    [Theory]
    [InlineData("hotelName asc")]
    [InlineData("unknown asc")]
    [InlineData("tags asc")]
    [InlineData("address asc")]
    [InlineData("address/city asc")]
    [InlineData("rooms/rate asc")]
    [InlineData("rating ascending")]
    [InlineData("rating asc desc")]
    [InlineData("rating asc, ")]
    [InlineData("rating,,id")]
    [InlineData("Rating asc")]
    public async Task OrderBy_NotASortableFieldOrDirection_Throws(string orderBy)
    {
        // Arrange
        var indexName = await SeedSortValidationIndexAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            OrderBy = orderBy
        }));
    }

    [Fact]
    public async Task OrderBy_MoreThan32Clauses_Throws()
    {
        // Arrange
        var indexName = await SeedSortValidationIndexAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "*",
            OrderBy = string.Join(",", Enumerable.Repeat("rating", 33))
        }));
    }

    [Theory]
    [InlineData("*", "rating desc", "2,3,1,4")]
    [InlineData("*", "rating", "4,1,3,2")]
    [InlineData("*", "address/rank desc", "3,1,2,4")]
    [InlineData("*", "address/code", "4,2,1,3")]
    [InlineData("spa", "rating desc", "2,3,1")]
    [InlineData("spa", "search.score() desc, rating desc", "1,2,3")]
    public async Task OrderBy_SortableFieldOrScore_ReturnsSortedResults(string search, string orderBy, string expectedIds)
    {
        // Arrange: in "spa", document 1 scores highest and documents 2 and 3 tie; document 4 has no values.
        var indexName = await SeedSortValidationIndexAsync();

        // Act
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = search,
            OrderBy = orderBy
        });

        // Assert: as in Azure, documents without a value come first ascending and last descending.
        Assert.Equal(expectedIds, string.Join(',', response.Value.Select(r => r["id"]?.ToString())));
    }

    private async Task<string> SeedSortValidationIndexAsync()
    {
        var indexName = $"sort-validation-{Guid.NewGuid():N}";
        RegisterIndex(new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true, Sortable = false },
                new() { Name = "rating", Type = "Edm.Double", Sortable = true },
                new() { Name = "tags", Type = "Collection(Edm.String)", Filterable = true },
                new()
                {
                    Name = "address",
                    Type = "Edm.ComplexType",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "city", Type = "Edm.String", Sortable = false },
                        new() { Name = "rank", Type = "Edm.Int32", Sortable = true },
                        new() { Name = "code", Type = "Edm.String", Sortable = true }
                    }
                },
                new()
                {
                    Name = "rooms",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = new List<SearchField> { new() { Name = "rate", Type = "Edm.Double", Sortable = true } }
                }
            }
        });

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1", ["hotelName"] = "spa spa spa", ["rating"] = 1.0,
                ["address"] = new Dictionary<string, object?> { ["rank"] = 2, ["code"] = "B" }
            },
            new Dictionary<string, object?>
            {
                ["id"] = "2", ["hotelName"] = "spa resort", ["rating"] = 5.0,
                ["address"] = new Dictionary<string, object?> { ["rank"] = -1, ["code"] = "A" }
            },
            new Dictionary<string, object?>
            {
                ["id"] = "3", ["hotelName"] = "spa resort", ["rating"] = 4.0,
                ["address"] = new Dictionary<string, object?> { ["rank"] = 7, ["code"] = "C" }
            },
            new Dictionary<string, object?> { ["id"] = "4", ["hotelName"] = "resort" });

        return indexName;
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
