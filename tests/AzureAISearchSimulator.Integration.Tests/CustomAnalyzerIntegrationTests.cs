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
/// Integration tests for custom analyzer support:
/// - Custom analyzers with stemming are applied during indexing and search
/// - CustomTokenFilter preserves type-specific properties (e.g., language) through JSON round-trip
/// </summary>
public class CustomAnalyzerIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;
    private readonly SearchService _searchService;

    public CustomAnalyzerIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "custom-analyzer-tests", Guid.NewGuid().ToString());
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
        _luceneManager.ConfigureAnalyzers(index.Name, index);
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

    // ─── Custom Analyzer Stemming ────────────────────────────────────

    [Fact]
    public async Task Search_WithEnglishStemmerAnalyzer_FindsStemmedTerms()
    {
        var indexName = $"stem-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "commentEnUS", Type = "Edm.String", Searchable = true, Analyzer = "commentEnUSAnalyzer" }
            },
            Analyzers = new List<CustomAnalyzer>
            {
                new()
                {
                    ODataType = "#Microsoft.Azure.Search.CustomAnalyzer",
                    Name = "commentEnUSAnalyzer",
                    Tokenizer = "whitespace",
                    TokenFilters = new List<string> { "lowercase", "englishStemmer" }
                }
            },
            TokenFilters = new List<CustomTokenFilter>
            {
                new()
                {
                    ODataType = "#Microsoft.Azure.Search.StemmerTokenFilter",
                    Name = "englishStemmer",
                    AdditionalProperties = new Dictionary<string, JsonElement>
                    {
                        ["language"] = JsonDocument.Parse("\"english\"").RootElement
                    }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["commentEnUS"] = "The running club is fantastic"
            });

        // "run" should match "running" via English stemmer
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "run",
            SearchFields = "commentEnUS",
            QueryType = "full"
        });

        Assert.True(response.Value.Count >= 1,
            "Expected at least 1 result: English stemmer should reduce 'running' → 'run'");
    }

    [Fact]
    public async Task Search_WithEnglishStemmerAnalyzer_FindsPluralForms()
    {
        var indexName = $"plural-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true, Analyzer = "enAnalyzer" }
            },
            Analyzers = new List<CustomAnalyzer>
            {
                new()
                {
                    ODataType = "#Microsoft.Azure.Search.CustomAnalyzer",
                    Name = "enAnalyzer",
                    Tokenizer = "standard",
                    TokenFilters = new List<string> { "lowercase", "enStemmer" }
                }
            },
            TokenFilters = new List<CustomTokenFilter>
            {
                new()
                {
                    ODataType = "#Microsoft.Azure.Search.StemmerTokenFilter",
                    Name = "enStemmer",
                    AdditionalProperties = new Dictionary<string, JsonElement>
                    {
                        ["language"] = JsonDocument.Parse("\"english\"").RootElement
                    }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["content"] = "The cats are playing in the gardens"
            });

        // "cat" should match "cats", "garden" should match "gardens"
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "cat garden",
            SearchFields = "content",
            QueryType = "full"
        });

        Assert.True(response.Value.Count >= 1,
            "Expected at least 1 result: English stemmer should handle plurals");
    }

    [Fact]
    public async Task Search_WithWordDelimiterAndLowercaseFilters_SplitsAndNormalizes()
    {
        var indexName = $"wd-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true, Analyzer = "wordDelimAnalyzer" }
            },
            Analyzers = new List<CustomAnalyzer>
            {
                new()
                {
                    ODataType = "#Microsoft.Azure.Search.CustomAnalyzer",
                    Name = "wordDelimAnalyzer",
                    Tokenizer = "whitespace",
                    TokenFilters = new List<string> { "lowercase", "word_delimiter" }
                }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["content"] = "WiFi-Enabled PowerShell SuperFast"
            });

        // word_delimiter should split "WiFi-Enabled" into "Wi", "Fi", "Enabled" etc.
        // lowercase should normalize to lower case
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "wifi",
            SearchFields = "content",
            QueryType = "full"
        });

        Assert.True(response.Value.Count >= 1,
            "Expected at least 1 result: word_delimiter + lowercase should match 'wifi' from 'WiFi-Enabled'");
    }

    [Fact]
    public async Task Search_WithBuiltInAnalyzerOnField_UsesCorrectAnalyzer()
    {
        var indexName = $"builtin-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true, Analyzer = "en.lucene" }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["content"] = "The runners are running quickly"
            });

        // English analyzer should stem "running" and "runners" to match "run"
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "run",
            SearchFields = "content",
            QueryType = "full"
        });

        Assert.True(response.Value.Count >= 1,
            "Expected at least 1 result: en.lucene analyzer should stem 'running'/'runners' to 'run'");
    }

    // ─── JSON Serialization Round-trip ───────────────────────────────

    [Fact]
    public void CustomTokenFilter_PreservesLanguageProperty_InJsonRoundTrip()
    {
        var json = """
        {
            "@odata.type": "#Microsoft.Azure.Search.StemmerTokenFilter",
            "name": "englishStemmer",
            "language": "english"
        }
        """;

        var filter = JsonSerializer.Deserialize<CustomTokenFilter>(json);

        Assert.NotNull(filter);
        Assert.Equal("#Microsoft.Azure.Search.StemmerTokenFilter", filter.ODataType);
        Assert.Equal("englishStemmer", filter.Name);
        Assert.NotNull(filter.AdditionalProperties);
        Assert.True(filter.AdditionalProperties.ContainsKey("language"),
            "language property should be preserved via JsonExtensionData");
        Assert.Equal("english", filter.AdditionalProperties["language"].GetString());

        // Round-trip: serialize back and verify language is in the output
        var serialized = JsonSerializer.Serialize(filter);
        var roundTripped = JsonSerializer.Deserialize<CustomTokenFilter>(serialized);

        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped.AdditionalProperties);
        Assert.True(roundTripped.AdditionalProperties.ContainsKey("language"));
        Assert.Equal("english", roundTripped.AdditionalProperties["language"].GetString());
    }

    [Fact]
    public void CustomTokenFilter_PreservesMultipleProperties_InJsonRoundTrip()
    {
        var json = """
        {
            "@odata.type": "#Microsoft.Azure.Search.NGramTokenFilterV2",
            "name": "myNGram",
            "minGram": 2,
            "maxGram": 5
        }
        """;

        var filter = JsonSerializer.Deserialize<CustomTokenFilter>(json);

        Assert.NotNull(filter);
        Assert.Equal("myNGram", filter.Name);
        Assert.NotNull(filter.AdditionalProperties);
        Assert.True(filter.AdditionalProperties.ContainsKey("minGram"));
        Assert.True(filter.AdditionalProperties.ContainsKey("maxGram"));
        Assert.Equal(2, filter.AdditionalProperties["minGram"].GetInt32());
        Assert.Equal(5, filter.AdditionalProperties["maxGram"].GetInt32());

        // Verify round-trip preserves the properties
        var serialized = JsonSerializer.Serialize(filter);
        Assert.Contains("\"minGram\"", serialized);
        Assert.Contains("\"maxGram\"", serialized);
    }

    [Fact]
    public void CustomTokenizer_PreservesProperties_InJsonRoundTrip()
    {
        var json = """
        {
            "@odata.type": "#Microsoft.Azure.Search.StandardTokenizerV2",
            "name": "myTokenizer",
            "maxTokenLength": 512
        }
        """;

        var tokenizer = JsonSerializer.Deserialize<CustomTokenizer>(json);

        Assert.NotNull(tokenizer);
        Assert.Equal("myTokenizer", tokenizer.Name);
        Assert.NotNull(tokenizer.AdditionalProperties);
        Assert.True(tokenizer.AdditionalProperties.ContainsKey("maxTokenLength"));
        Assert.Equal(512, tokenizer.AdditionalProperties["maxTokenLength"].GetInt32());

        var serialized = JsonSerializer.Serialize(tokenizer);
        Assert.Contains("\"maxTokenLength\"", serialized);
    }

    [Fact]
    public void SearchIndex_WithCustomAnalyzers_RoundTripsCorrectly()
    {
        var json = """
        {
            "name": "test-analyzer",
            "fields": [
                {"name": "id", "type": "Edm.String", "key": true},
                {"name": "commentEnUS", "type": "Edm.String", "searchable": true, "analyzer": "commentEnUSAnalyzer"}
            ],
            "analyzers": [
                {
                    "@odata.type": "#Microsoft.Azure.Search.CustomAnalyzer",
                    "name": "commentEnUSAnalyzer",
                    "tokenizer": "whitespace",
                    "tokenFilters": ["lowercase", "word_delimiter", "englishStemmer"]
                }
            ],
            "tokenFilters": [
                {
                    "@odata.type": "#Microsoft.Azure.Search.StemmerTokenFilter",
                    "name": "englishStemmer",
                    "language": "english"
                }
            ]
        }
        """;

        var index = JsonSerializer.Deserialize<SearchIndex>(json);
        Assert.NotNull(index);
        Assert.NotNull(index.TokenFilters);
        Assert.Single(index.TokenFilters);

        var stemmerFilter = index.TokenFilters[0];
        Assert.Equal("#Microsoft.Azure.Search.StemmerTokenFilter", stemmerFilter.ODataType);
        Assert.Equal("englishStemmer", stemmerFilter.Name);
        Assert.NotNull(stemmerFilter.AdditionalProperties);
        Assert.Equal("english", stemmerFilter.AdditionalProperties["language"].GetString());

        // Serialize back and verify language is present
        var serialized = JsonSerializer.Serialize(index);
        Assert.Contains("\"language\"", serialized);
        Assert.Contains("\"english\"", serialized);

        // Verify the full round-trip
        var roundTripped = JsonSerializer.Deserialize<SearchIndex>(serialized);
        Assert.NotNull(roundTripped?.TokenFilters);
        Assert.Equal("english",
            roundTripped.TokenFilters[0].AdditionalProperties?["language"].GetString());
    }

    // ─── Negative test: no analyzer means StandardAnalyzer ───────────

    [Fact]
    public async Task Search_WithoutCustomAnalyzer_UsesStandardAnalyzer()
    {
        var indexName = $"std-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true }
            }
        };
        RegisterIndex(index);

        await UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "1",
                ["content"] = "The running club is fantastic"
            });

        // With StandardAnalyzer, "run" should NOT match "running" (no stemming)
        var response = await _searchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "run",
            SearchFields = "content",
            QueryType = "full"
        });

        Assert.Equal(0, response.Value.Count);
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
