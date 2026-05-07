using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureAISearchSimulator.Core.Configuration;
using Moq;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for search highlight parity with Azure AI Search:
/// - Only requested fields are highlighted (not all searchable fields)
/// - @search.highlights appears before document fields in results
/// - Multiple highlight fields can be specified comma-separated
/// </summary>
public class SearchHighlightTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;
    private readonly SearchIndex _testIndex;

    public SearchHighlightTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "highlight-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = _testDir });
        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var vectorSearchService = Mock.Of<IVectorSearchService>();

        _testIndex = new SearchIndex
        {
            Name = "highlight-test",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "category", Type = "Edm.String", Searchable = true, Filterable = true }
            }
        };

        var indexServiceMock = new Mock<IIndexService>();
        indexServiceMock.Setup(x => x.GetIndexAsync("highlight-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testIndex);

        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            vectorSearchService,
            indexServiceMock.Object);

        _searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            _luceneManager,
            vectorSearchService,
            indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            Mock.Of<IScoringProfileService>());

        // Seed documents
        var upload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "1",
                    ["title"] = "Luxury Spa Resort",
                    ["description"] = "A beautiful spa with luxury amenities and a relaxing pool.",
                    ["category"] = "Luxury"
                }),
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "2",
                    ["title"] = "Budget Hotel",
                    ["description"] = "Affordable rooms for travelers looking for comfort.",
                    ["category"] = "Budget"
                })
            }
        };
        _documentService.IndexDocumentsAsync("highlight-test", upload).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _luceneManager.Dispose();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private static IndexAction CreateAction(string actionType, Dictionary<string, object?> fields)
    {
        var action = new IndexAction { ["@search.action"] = actionType };
        foreach (var kvp in fields)
            action[kvp.Key] = kvp.Value;
        return action;
    }

    // ─── Highlight field restriction tests ─────────────────────────

    [Fact]
    public async Task Highlight_OnlyRequestedField_IsHighlighted()
    {
        var request = new SearchRequest
        {
            Search = "luxury spa",
            Highlight = "description",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        var result = response.Value.First(r => r.ContainsKey("id") && r["id"]?.ToString() == "1");
        Assert.NotNull(result.Highlights);

        // Only "description" should be highlighted, not "title" or "category"
        Assert.True(result.Highlights.ContainsKey("description"));
        Assert.False(result.Highlights.ContainsKey("title"));
        Assert.False(result.Highlights.ContainsKey("category"));
    }

    [Fact]
    public async Task Highlight_MultipleFields_OnlyRequestedFieldsHighlighted()
    {
        var request = new SearchRequest
        {
            Search = "luxury",
            Highlight = "title,description",
            HighlightPreTag = "<b>",
            HighlightPostTag = "</b>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        var result = response.Value.First(r => r.ContainsKey("id") && r["id"]?.ToString() == "1");
        Assert.NotNull(result.Highlights);

        // "category" contains "Luxury" but was NOT requested for highlighting
        Assert.False(result.Highlights.ContainsKey("category"));
    }

    [Fact]
    public async Task Highlight_FieldWithNoMatch_IsOmitted()
    {
        // Search for "pool" — only doc 1 has it, and only in description
        var request = new SearchRequest
        {
            Search = "pool",
            Highlight = "title",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        // Doc 1 matches "pool" in description, but we asked for title highlights only
        var result = response.Value.FirstOrDefault(r => r.ContainsKey("id") && r["id"]?.ToString() == "1");
        Assert.NotNull(result);
        // title doesn't contain "pool", so no highlights should be present
        Assert.True(result.Highlights == null || !result.Highlights.ContainsKey("title"));
    }

    [Fact]
    public async Task Highlight_UsesCustomPrePostTags()
    {
        var request = new SearchRequest
        {
            Search = "spa",
            Highlight = "description",
            HighlightPreTag = "<<START>>",
            HighlightPostTag = "<<END>>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        var result = response.Value.First(r => r.ContainsKey("id") && r["id"]?.ToString() == "1");
        Assert.NotNull(result.Highlights);
        Assert.True(result.Highlights.ContainsKey("description"));

        var fragment = result.Highlights["description"][0];
        Assert.Contains("<<START>>", fragment);
        Assert.Contains("<<END>>", fragment);
        Assert.DoesNotContain("<em>", fragment);
    }

    // ─── Property ordering tests ──────────────────────────────────

    [Fact]
    public async Task Highlight_PropertyOrder_ScoreThenHighlightsThenFields()
    {
        var request = new SearchRequest
        {
            Search = "spa",
            Highlight = "description",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        var result = response.Value.First(r => r.ContainsKey("id") && r["id"]?.ToString() == "1");
        var keys = result.Keys.ToList();

        var scoreIndex = keys.IndexOf("@search.score");
        var highlightsIndex = keys.IndexOf("@search.highlights");
        var firstDocField = keys.FindIndex(k => !k.StartsWith("@search."));

        // @search.score should be first
        Assert.Equal(0, scoreIndex);
        // @search.highlights should be before document fields
        Assert.True(highlightsIndex < firstDocField,
            $"@search.highlights (index {highlightsIndex}) should come before first document field (index {firstDocField})");
    }

    [Fact]
    public async Task NoHighlight_NoSearchHighlightsProperty()
    {
        var request = new SearchRequest
        {
            Search = "luxury",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        var result = response.Value.First();
        Assert.Null(result.Highlights);
        Assert.False(result.ContainsKey("@search.highlights"));
    }

    // ─── Phrase search tests ──────────────────────────────────────

    [Fact]
    public async Task PhraseSearch_ExactMatch_ReturnsMatchingDocWithHighlights()
    {
        // Phrase search: "luxury spa" should only match doc 1 (title = "Luxury Spa Resort")
        var request = new SearchRequest
        {
            Search = "\"luxury spa\"",
            Highlight = "title,description",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        // Only doc 1 contains the exact phrase "luxury spa"
        Assert.Single(response.Value);
        var result = response.Value[0];
        Assert.Equal("1", result["id"]?.ToString());

        // Highlights should be present and contain the phrase as one contiguous span
        Assert.NotNull(result.Highlights);
        Assert.True(result.Highlights.ContainsKey("title"));
        var titleHighlight = result.Highlights["title"][0];
        Assert.Contains("<em>Luxury Spa</em>", titleHighlight);

        // Description has "spa" and "luxury" but NOT adjacent as "luxury spa",
        // so phrase query should NOT highlight the description
        Assert.False(result.Highlights.ContainsKey("description"));
    }

    [Fact]
    public async Task PhraseSearch_WordsNotAdjacent_ReturnsEmpty()
    {
        // "affordable spa" — no document has these words adjacent in this order
        var request = new SearchRequest
        {
            Search = "\"affordable spa\"",
            Top = 10
        };

        var response = await _searchService.SearchAsync("highlight-test", request);

        Assert.Empty(response.Value);
    }

    [Fact]
    public async Task PhraseSearch_VsNonPhrase_ReturnsDifferentResults()
    {
        // Non-phrase: "luxury spa" (without quotes) matches both docs
        // because "luxury" appears in doc 1 title/description and doc 2 doesn't,
        // but the OR semantics may match broadly.
        var nonPhraseRequest = new SearchRequest
        {
            Search = "beautiful spa",
            Top = 10
        };
        var nonPhraseResponse = await _searchService.SearchAsync("highlight-test", nonPhraseRequest);

        // Phrase: "beautiful spa" (with quotes) must match only doc 1
        // where "beautiful spa" appears adjacent in description
        var phraseRequest = new SearchRequest
        {
            Search = "\"beautiful spa\"",
            Highlight = "description",
            HighlightPreTag = "<em>",
            HighlightPostTag = "</em>",
            Top = 10
        };
        var phraseResponse = await _searchService.SearchAsync("highlight-test", phraseRequest);

        // Non-phrase should return more results (OR of "beautiful" and "spa")
        Assert.True(nonPhraseResponse.Value.Count >= phraseResponse.Value.Count);

        // Phrase should return exactly doc 1 (description = "A beautiful spa with...")
        Assert.Single(phraseResponse.Value);
        Assert.Equal("1", phraseResponse.Value[0]["id"]?.ToString());

        // Highlight should wrap the entire phrase as one contiguous span
        var result = phraseResponse.Value[0];
        Assert.NotNull(result.Highlights);
        Assert.True(result.Highlights.ContainsKey("description"));
        var fragment = result.Highlights["description"][0];
        Assert.Contains("<em>beautiful spa</em>", fragment);
    }
}
