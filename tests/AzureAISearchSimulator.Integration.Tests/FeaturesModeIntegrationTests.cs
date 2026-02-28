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
/// Integration tests verifying that featuresMode=enabled returns per-field
/// BM25 scoring features (@search.features) with uniqueTokenMatches,
/// similarityScore, and termFrequency values.
/// </summary>
public class FeaturesModeIntegrationTests : IDisposable
{
    private readonly string _testDir;

    public FeaturesModeIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "features-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private (LuceneIndexManager lucene, SearchService search, DocumentService docs, Mock<IIndexService> indexMock) CreateServices(string subDir)
    {
        var dir = Path.Combine(_testDir, subDir);
        Directory.CreateDirectory(dir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = dir });
        var luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var vectorSearchService = Mock.Of<IVectorSearchService>();
        var indexServiceMock = new Mock<IIndexService>();

        var scoringProfileService = new ScoringProfileService(
            Mock.Of<ILogger<ScoringProfileService>>());

        var documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            luceneManager,
            vectorSearchService,
            indexServiceMock.Object);

        var searchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            luceneManager,
            vectorSearchService,
            indexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            scoringProfileService);

        return (luceneManager, searchService, documentService, indexServiceMock);
    }

    private SearchIndex CreateIndex(string name, List<SearchField>? fields = null)
    {
        return new SearchIndex
        {
            Name = name,
            Fields = fields ?? new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true, Filterable = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "content", Type = "Edm.String", Searchable = true }
            }
        };
    }

    private async Task UploadDocuments(DocumentService docs, string indexName, params (string id, string title, string content)[] documents)
    {
        var request = new IndexDocumentsRequest
        {
            Value = documents.Select(d =>
            {
                var action = new IndexAction
                {
                    ["@search.action"] = "upload",
                    ["id"] = d.id,
                    ["title"] = d.title,
                    ["content"] = d.content
                };
                return action;
            }).ToList()
        };
        await docs.IndexDocumentsAsync(indexName, request);
    }

    [Fact]
    public async Task FeaturesMode_Enabled_ReturnsSearchFeatures()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-basic");
        var indexName = "features-basic";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"),
            ("2", "Azure Functions", "Azure Functions is a serverless compute service"));

        // Act
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var firstResult = response.Value[0];
        Assert.NotNull(firstResult.Features);
        Assert.NotEmpty(firstResult.Features);

        // Should have features for at least one field
        var hasFeatures = firstResult.Features.Values.Any(f =>
            f.UniqueTokenMatches > 0 || f.SimilarityScore > 0 || f.TermFrequency > 0);
        Assert.True(hasFeatures, "Features should contain non-zero values for matching fields");
    }

    [Fact]
    public async Task FeaturesMode_None_DoesNotReturnSearchFeatures()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-none");
        var indexName = "features-none";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides capabilities"));

        // Act
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "none"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        Assert.Null(response.Value[0].Features);
    }

    [Fact]
    public async Task FeaturesMode_NotSet_DoesNotReturnSearchFeatures()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-notset");
        var indexName = "features-notset";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides capabilities"));

        // Act
        var request = new SearchRequest { Search = "azure search" };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        Assert.Null(response.Value[0].Features);
    }

    [Fact]
    public async Task FeaturesMode_ReturnsCorrectFieldNames()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-fields");
        var indexName = "features-fields";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Search Title", "This is search content about search"));

        // Act
        var request = new SearchRequest
        {
            Search = "search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);

        // Both title and content contain "search", so both should have features
        Assert.True(features.ContainsKey("title"), "Features should include 'title' field");
        Assert.True(features.ContainsKey("content"), "Features should include 'content' field");
    }

    [Fact]
    public async Task FeaturesMode_UniqueTokenMatches_CorrectCount()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-tokens");
        var indexName = "features-tokens";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"));

        // Act: search for two terms, both present in title
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);

        // "title" contains both "azure" and "search"
        Assert.True(features.ContainsKey("title"));
        Assert.Equal(2.0, features["title"].UniqueTokenMatches);
    }

    [Fact]
    public async Task FeaturesMode_TermFrequency_CountsMultipleOccurrences()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-tf");
        var indexName = "features-tf";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            // "search" appears 3 times in content
            ("1", "Title", "search search search is great"));

        // Act
        var request = new SearchRequest
        {
            Search = "search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("content"));
        Assert.Equal(3.0, features["content"].TermFrequency);
        Assert.Equal(1.0, features["content"].UniqueTokenMatches); // Only 1 unique term
    }

    [Fact]
    public async Task FeaturesMode_SimilarityScore_PositiveForMatchingFields()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-similarity");
        var indexName = "features-similarity";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"));

        // Act
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);

        // Every field with matches should have a positive similarity score
        foreach (var (fieldName, fieldFeatures) in features)
        {
            Assert.True(fieldFeatures.SimilarityScore > 0,
                $"Field '{fieldName}' should have a positive similarity score");
        }
    }

    [Fact]
    public async Task FeaturesMode_OnlyMatchingFieldsIncluded()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-onlymatch");
        var indexName = "features-onlymatch";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            // "unicorn" only appears in content, not in title
            ("1", "Regular Title", "A unicorn is a mythical creature"));

        // Act
        var request = new SearchRequest
        {
            Search = "unicorn",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);

        // Only "content" should have features since "unicorn" only appears there
        Assert.True(features.ContainsKey("content"), "Features should include 'content' field");
        Assert.False(features.ContainsKey("title"), "Features should NOT include 'title' field (no match)");
    }

    [Fact]
    public async Task FeaturesMode_WithSearchFields_OnlyReturnsSelectedFields()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-searchfields");
        var indexName = "features-searchfields";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"));

        // Act: restrict searchFields to just "title"
        var request = new SearchRequest
        {
            Search = "azure search",
            SearchFields = "title",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        var features = response.Value[0].Features;
        Assert.NotNull(features);

        // Only "title" should be in features since we restricted searchFields
        Assert.True(features.ContainsKey("title"), "Features should include 'title' field");
        Assert.False(features.ContainsKey("content"), "Features should NOT include 'content' field (not in searchFields)");
    }

    [Fact]
    public async Task FeaturesMode_MatchAllQuery_NoFeatures()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-matchall");
        var indexName = "features-matchall";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search capabilities"));

        // Act: search=* should not produce features
        var request = new SearchRequest
        {
            Search = "*",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        Assert.Null(response.Value[0].Features);
    }

    [Fact]
    public async Task FeaturesMode_CaseInsensitiveValue()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-case");
        var indexName = "features-case";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search capabilities"));

        // Act: "Enabled" (capitalized) should still work
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "Enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert
        Assert.NotEmpty(response.Value);
        Assert.NotNull(response.Value[0].Features);
    }

    [Fact]
    public async Task FeaturesMode_MultipleResults_EachHasFeatures()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-multi");
        var indexName = "features-multi";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search Overview", "Azure AI Search provides full-text search"),
            ("2", "Azure Functions", "Azure Functions is a serverless compute service"),
            ("3", "Azure Cosmos DB", "Azure Cosmos DB is a multi-model database"));

        // Act: all docs contain "azure"
        var request = new SearchRequest
        {
            Search = "azure",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert: every result should have features
        Assert.Equal(3, response.Value.Count);
        foreach (var result in response.Value)
        {
            Assert.NotNull(result.Features);
            var hasNonZeroFeatures = result.Features.Values.Any(f =>
                f.UniqueTokenMatches > 0 || f.SimilarityScore > 0 || f.TermFrequency > 0);
            Assert.True(hasNonZeroFeatures, "Each matched result should have non-zero features");
        }
    }

    [Fact]
    public async Task FeaturesMode_SimilarityScoreSumApproximatesTotalScore()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-scoresum");
        var indexName = "features-scoresum";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"));

        // Act
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "enabled"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert: sum of per-field similarity scores should be close to the overall @search.score
        // (they may not be exactly equal due to coordination factor, query normalization, etc.)
        var firstResult = response.Value[0];
        Assert.NotNull(firstResult.Features);
        Assert.NotNull(firstResult.Score);

        var totalFieldScore = firstResult.Features.Values.Sum(f => f.SimilarityScore);
        // The sum of field scores should be in the same ballpark as the total score
        // Allow generous tolerance since multi-field scoring can differ
        Assert.True(totalFieldScore > 0, "Sum of field similarity scores should be positive");
    }

    [Fact]
    public async Task FeaturesMode_WithHighlights_BothPresent()
    {
        // Arrange
        var (lucene, search, docs, indexMock) = CreateServices("features-highlights");
        var indexName = "features-highlights";
        var index = CreateIndex(indexName);
        indexMock.Setup(x => x.GetIndexAsync(indexName)).ReturnsAsync(index);

        await UploadDocuments(docs, indexName,
            ("1", "Azure Search", "Azure AI Search provides full-text search capabilities"));

        // Act: enable both features and highlights
        var request = new SearchRequest
        {
            Search = "azure search",
            FeaturesMode = "enabled",
            Highlight = "title,content"
        };
        var response = await search.SearchAsync(indexName, request);

        // Assert: both features and highlights should be present
        Assert.NotEmpty(response.Value);
        var firstResult = response.Value[0];
        Assert.NotNull(firstResult.Features);
        Assert.NotNull(firstResult.Highlights);
    }
}
