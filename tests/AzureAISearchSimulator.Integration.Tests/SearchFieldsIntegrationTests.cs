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
/// searchFields tests. The expected behaviour (matching, validation messages, case sensitivity,
/// complex sub-field paths) was checked against a real Azure AI Search service (API 2024-07-01).
/// </summary>
public class SearchFieldsIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly Mock<ISynonymMapResolver> _synonymMapResolverMock = new();
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public SearchFieldsIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "searchfields-tests", Guid.NewGuid().ToString());
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
            _synonymMapResolverMock.Object,
            scoringProfileService);
    }

    /// <summary>
    /// Hotel 1 has "ocean" in its name, hotel 2 only in its description.
    /// </summary>
    private async Task<string> CreateHotelsIndexWithDocuments(List<string>? roomTypeSynonymMaps = null)
    {
        var indexName = $"searchfields-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "hotelId", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true },
                new()
                {
                    Name = "rooms",
                    Type = "Collection(Edm.ComplexType)",
                    Fields = new List<SearchField>
                    {
                        new() { Name = "type", Type = "Edm.String", Searchable = true, SynonymMaps = roomTypeSynonymMaps },
                        new() { Name = "baseRate", Type = "Edm.Double", Filterable = true }
                    }
                }
            }
        };

        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);

        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                new()
                {
                    ["@search.action"] = "upload", ["hotelId"] = "1", ["hotelName"] = "Ocean Retreat",
                    ["description"] = "Quiet rooms near the harbor", ["rating"] = 4.5,
                    ["rooms"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "suite", ["baseRate"] = 250.0 } }
                },
                new()
                {
                    ["@search.action"] = "upload", ["hotelId"] = "2", ["hotelName"] = "Harbor Inn",
                    ["description"] = "Every room has an ocean view", ["rating"] = 3.5,
                    ["rooms"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "standard", ["baseRate"] = 120.0 } }
                }
            }
        });

        return indexName;
    }

    private async Task<List<string?>> SearchIds(string indexName, string search, string? searchFields, string queryType = "simple")
    {
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = search,
            SearchFields = searchFields,
            QueryType = queryType
        });
        return response.Value.Select(r => r["hotelId"]?.ToString()).OrderBy(id => id).ToList();
    }

    [Fact]
    public async Task Search_WithoutSearchFields_MatchesAllSearchableFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        Assert.Equal(new List<string?> { "1", "2" }, await SearchIds(indexName, "ocean", null));
    }

    [Theory]
    [InlineData("hotelName", "1")]
    [InlineData("description", "2")]
    public async Task Search_WithSearchFields_MatchesOnlyThoseFields(string searchFields, string expectedId)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        Assert.Equal(new List<string?> { expectedId }, await SearchIds(indexName, "ocean", searchFields));
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("full")]
    public async Task Search_WithSearchFields_AppliesToBothQueryTypes(string queryType)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        Assert.Equal(new List<string?> { "2" }, await SearchIds(indexName, "oce*", "description", queryType));
    }

    [Fact]
    public async Task Search_WithSpacesInSearchFields_TrimsNames()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        Assert.Equal(new List<string?> { "1", "2" }, await SearchIds(indexName, "ocean", "hotelName, description"));
    }

    [Fact]
    public async Task Search_WithComplexSubFieldPath_MatchesThatSubField()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        Assert.Equal(new List<string?> { "1" }, await SearchIds(indexName, "suite", "rooms/type"));
    }

    [Fact]
    public async Task Search_FullQueryFieldedTerm_IsNotLimitedBySearchFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // Azure still searches a field named in the query itself
        Assert.Equal(new List<string?> { "2" }, await SearchIds(indexName, "description:ocean", "hotelName", "full"));
    }

    [Theory]
    [InlineData("nope", "Unknown field 'nope' in search field list.")]
    [InlineData("HotelName", "Unknown field 'HotelName' in search field list.")]
    [InlineData("rooms/nope", "Unknown field 'rooms/nope' in search field list.")]
    [InlineData("rating", "The field 'rating' in the search field list is not searchable.")]
    [InlineData("rooms", "The field 'rooms' in the search field list is not searchable.")]
    [InlineData("rooms/baseRate", "The field 'rooms/baseRate' in the search field list is not searchable.")]
    [InlineData(" , ", "Unknown field '' in search field list.")]
    [InlineData(",", "If a query contains the search option the target index must contain one or more searchable string fields.")]
    [InlineData(",,", "If a query contains the search option the target index must contain one or more searchable string fields.")]
    public async Task Search_WithInvalidSearchField_Throws(string searchFields, string expectedMessage)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => SearchIds(indexName, "ocean", searchFields));
        Assert.StartsWith(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Search_WithBlankSearchFields_MatchesAllSearchableFields(string searchFields)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // Azure treats a blank searchFields like an absent one
        Assert.Equal(new List<string?> { "1", "2" }, await SearchIds(indexName, "ocean", searchFields));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Search_WithoutSearchFields_AlsoMatchesComplexSubFields(string? searchFields)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // "suite" is only in rooms/type; Azure searches searchable sub-fields by default
        Assert.Equal(new List<string?> { "1" }, await SearchIds(indexName, "suite", searchFields));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rooms/type")]
    public async Task Search_ExpandsSynonymsOnComplexSubFields(string? searchFields)
    {
        var indexName = await CreateHotelsIndexWithDocuments(roomTypeSynonymMaps: new List<string> { "room-types" });
        _synonymMapResolverMock
            .Setup(x => x.ExpandTerms(It.IsAny<IEnumerable<string>>(), "penthouse"))
            .Returns(new List<string> { "penthouse", "suite" });

        // Only the synonym "suite" is in the index, in rooms/type
        Assert.Equal(new List<string?> { "1" }, await SearchIds(indexName, "penthouse", searchFields));
    }

    [Fact]
    public async Task Search_WithFeaturesMode_ReportsComplexSubFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "suite",
            FeaturesMode = "enabled"
        });

        // Azure keys @search.features by the sub-field's path
        var result = Assert.Single(response.Value);
        Assert.NotNull(result.Features);
        Assert.Contains("rooms/type", result.Features.Keys);
    }

    [Fact]
    public async Task Search_WithWildcardSearch_IgnoresSearchFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // Azure doesn't validate searchFields when there's no search text to match
        Assert.Equal(new List<string?> { "1", "2" }, await SearchIds(indexName, "*", "nope"));
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
