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
/// Test fixture for scoring profile integration tests.
/// Creates shared Lucene + search infrastructure with a real ScoringProfileService.
/// </summary>
public class ScoringProfileTestFixture : IDisposable
{
    public string TestDir { get; }
    public LuceneIndexManager LuceneManager { get; }
    public Mock<IIndexService> IndexServiceMock { get; }
    public DocumentService DocumentService { get; }
    public SearchService SearchService { get; }
    public ScoringProfileService ScoringProfileService { get; }

    public ScoringProfileTestFixture()
    {
        TestDir = Path.Combine(Path.GetTempPath(), "scoring-profile-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestDir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = TestDir });
        LuceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var vectorSearchService = Mock.Of<IVectorSearchService>();

        IndexServiceMock = new Mock<IIndexService>();

        ScoringProfileService = new ScoringProfileService(
            Mock.Of<ILogger<ScoringProfileService>>());

        DocumentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            LuceneManager,
            vectorSearchService,
            IndexServiceMock.Object);

        SearchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            LuceneManager,
            vectorSearchService,
            IndexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            ScoringProfileService);
    }

    /// <summary>
    /// Registers an index definition with the mock index service and initializes its Lucene writer.
    /// </summary>
    public void RegisterIndex(SearchIndex index)
    {
        IndexServiceMock.Setup(x => x.GetIndexAsync(index.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(index);
        LuceneManager.GetWriter(index.Name);
    }

    /// <summary>
    /// Uploads documents to the given index.
    /// </summary>
    public async Task UploadDocuments(string indexName, params Dictionary<string, object?>[] documents)
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
        await DocumentService.IndexDocumentsAsync(indexName, request);
    }

    public void Dispose()
    {
        LuceneManager?.Dispose();
        if (Directory.Exists(TestDir))
        {
            try { Directory.Delete(TestDir, true); } catch { }
        }
    }
}

/// <summary>
/// Collection definition for scoring profile integration tests.
/// </summary>
[CollectionDefinition("Scoring Profile Integration Tests")]
public class ScoringProfileTestCollection : ICollectionFixture<ScoringProfileTestFixture>
{
}
