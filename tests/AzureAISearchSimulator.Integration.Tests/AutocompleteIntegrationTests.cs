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
/// Autocomplete tests. The data mirrors a side-by-side comparison with a real Azure AI Search
/// service (API 2024-07-01): the same document counts and term layout, with hotel names in place
/// of the original values, so the expected rankings follow the results Azure returned.
/// </summary>
public class AutocompleteIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public AutocompleteIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "autocomplete-tests", Guid.NewGuid().ToString());
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

    /// <summary>
    /// Seaview (4 hotels), Seaside (3), Seashell (2) and Seabird, Seacliff, Seagull (1 each).
    /// Every description says "Seafront", in a searchable field that isn't one of the suggester's
    /// source fields.
    /// </summary>
    private async Task<string> CreateHotelsIndexWithDocuments()
    {
        var indexName = $"autocomplete-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "hotelId", Type = "Edm.String", Key = true },
                new() { Name = "hotelName", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Searchable = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            },
            Suggesters = new List<Suggester>
            {
                new()
                {
                    Name = "sg",
                    SearchMode = "analyzingInfixMatching",
                    SourceFields = new List<string> { "hotelName", "category" }
                }
            }
        };

        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);

        var hotels = new[]
        {
            ("Seaside Hotel", 4.0), ("Seaside Hotel", 4.0), ("Seaside Suites", 2.0),
            ("Seashell Hotel", 2.0), ("Seashell Suites", 2.0),
            ("Seabird Hotel", 4.0), ("Seacliff Suites", 4.0), ("Seagull Hotel", 4.0),
            ("Seaview Resort", 2.0), ("Seaview Resort", 2.0), ("Seaview Resort", 2.0), ("Seaview Resort", 2.0)
        };

        await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value = hotels.Select((hotel, i) => new IndexAction
            {
                ["@search.action"] = "upload",
                ["hotelId"] = (i + 1).ToString(),
                ["hotelName"] = hotel.Item1,
                ["category"] = "Boutique",
                ["description"] = "Seafront location",
                ["rating"] = hotel.Item2
            }).ToList()
        });

        return indexName;
    }

    private Task<AutocompleteResponse> Autocomplete(string indexName, string search, Action<AutocompleteRequest>? configure = null)
    {
        var request = new AutocompleteRequest
        {
            Search = search,
            SuggesterName = "sg",
            HighlightPreTag = "<hl>",
            HighlightPostTag = "</hl>"
        };
        configure?.Invoke(request);
        return _searchService.AutocompleteAsync(indexName, request);
    }

    private static List<string> Texts(AutocompleteResponse response) =>
        response.Value.Select(v => v.Text).ToList();

    [Fact]
    public async Task Autocomplete_RanksByDocumentCountThenAlphabetically()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "sea");

        Assert.Equal(new List<string> { "seaview", "seaside", "seashell", "seabird", "seacliff" }, Texts(response));
    }

    [Fact]
    public async Task Autocomplete_WrapsCompletionInHighlightTags()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "sea");

        Assert.Equal("<hl>seaview</hl>", response.Value[0].QueryPlusText);
    }

    [Fact]
    public async Task Autocomplete_WithoutHighlightTags_ReturnsPlainQueryPlusText()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "sea", r =>
        {
            r.HighlightPreTag = null;
            r.HighlightPostTag = null;
        });

        Assert.Equal("seaview", response.Value[0].QueryPlusText);
    }

    [Fact]
    public async Task Autocomplete_CompletesOnlyFromSuggesterSourceFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // "seafront" is only in description, which isn't a source field of the suggester
        var response = await Autocomplete(indexName, "sea", r => r.Top = 10);

        Assert.Equal(new List<string> { "seaview", "seaside", "seashell", "seabird", "seacliff", "seagull" }, Texts(response));
    }

    [Fact]
    public async Task Autocomplete_WithFilter_CountsOnlyMatchingDocuments()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "sea", r => r.Filter = "rating ge 3");

        Assert.Equal(new List<string> { "seaside", "seabird", "seacliff", "seagull" }, Texts(response));
    }

    [Fact]
    public async Task Autocomplete_WithSeveralWords_CompletesLastWordAndKeepsTheRest()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // Earlier words don't narrow the completions: no Seaview is a Hotel
        var response = await Autocomplete(indexName, "hotel sea");

        Assert.Equal(new List<string> { "seaview", "seaside", "seashell", "seabird", "seacliff" }, Texts(response));
        Assert.Equal("hotel <hl>seaview</hl>", response.Value[0].QueryPlusText);
    }

    [Fact]
    public async Task Autocomplete_WithSeveralWords_CompletesFromAnySourceTerm()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "seaside ho");

        var completion = Assert.Single(response.Value);
        Assert.Equal("hotel", completion.Text);
        Assert.Equal("seaside <hl>hotel</hl>", completion.QueryPlusText);
    }

    [Fact]
    public async Task Autocomplete_BreaksCountTiesAlphabetically()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        // seaside and suites are both in 3 documents
        var response = await Autocomplete(indexName, "s");

        Assert.Equal(new List<string> { "seaview", "seaside", "suites", "seashell", "seabird" }, Texts(response));
    }

    [Theory]
    [InlineData("sea ")]
    [InlineData("SEA")]
    public async Task Autocomplete_IgnoresTrailingSpaceAndCase(string search)
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, search);

        Assert.Equal(new List<string> { "seaview", "seaside", "seashell", "seabird", "seacliff" }, Texts(response));
    }

    [Fact]
    public async Task Autocomplete_WithSearchFields_CompletesOnlyFromThoseFields()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var response = await Autocomplete(indexName, "sea", r => r.SearchFields = "category");

        Assert.Empty(response.Value);
    }

    [Fact]
    public async Task Autocomplete_WithoutSuggesterName_Throws()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Autocomplete(indexName, "sea", r => r.SuggesterName = ""));
        Assert.Contains("suggesterName", ex.Message);
    }

    [Fact]
    public async Task Autocomplete_WithUnknownSuggester_Throws()
    {
        var indexName = await CreateHotelsIndexWithDocuments();

        await Assert.ThrowsAsync<ArgumentException>(() => Autocomplete(indexName, "sea", r => r.SuggesterName = "nope"));
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
