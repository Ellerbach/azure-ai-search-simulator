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
/// Test fixture for HNSW integration tests. Creates shared resources once per test collection.
/// </summary>
public class HnswTestFixture : IDisposable
{
    public string TestDir { get; }
    public LuceneIndexManager LuceneManager { get; }
    public VectorStore VectorStore { get; }
    public IHnswIndexManager HnswManager { get; }
    public IVectorSearchService VectorSearchService { get; }
    public Mock<IIndexService> IndexServiceMock { get; }
    public DocumentService DocumentService { get; }
    public SearchService SearchService { get; }
    public SearchIndex TestIndex { get; }

    public HnswTestFixture()
    {
        TestDir = Path.Combine(Path.GetTempPath(), "hnsw-integration-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestDir);

        // Set up Lucene
        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = TestDir });
        LuceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        // Set up VectorStore (brute-force fallback)
        VectorStore = new VectorStore();

        // Set up HNSW
        var vectorSettings = Options.Create(new VectorSearchSettings
        {
            UseHnsw = true,
            MaxVectorsPerIndex = 10000,
            HnswSettings = new HnswSettings
            {
                M = 16,
                EfConstruction = 100,
                EfSearch = 50,
                OversampleMultiplier = 3
            },
            HybridSearchSettings = new HybridSearchSettings
            {
                DefaultFusionMethod = "RRF",
                RrfK = 60
            }
        });

        HnswManager = new HnswIndexManager(
            Mock.Of<ILogger<HnswIndexManager>>(),
            vectorSettings,
            luceneSettings);

        VectorSearchService = new HnswVectorSearchService(
            Mock.Of<ILogger<HnswVectorSearchService>>(),
            vectorSettings,
            HnswManager,
            VectorStore);

        // Set up test index schema
        TestIndex = new SearchIndex
        {
            Name = "test-index",
            Fields = new List<SearchField>
            {
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "content", Type = "Edm.String", Searchable = true },
                new SearchField { Name = "embedding", Type = "Collection(Edm.Single)", Dimensions = 3 }
            }
        };

        // Mock index service
        IndexServiceMock = new Mock<IIndexService>();
        IndexServiceMock.Setup(x => x.GetIndexAsync("test-index"))
            .ReturnsAsync(TestIndex);

        // Create services
        DocumentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            LuceneManager,
            VectorSearchService,
            IndexServiceMock.Object);

        SearchService = new SearchService(
            Mock.Of<ILogger<SearchService>>(),
            LuceneManager,
            VectorSearchService,
            IndexServiceMock.Object,
            Mock.Of<ISynonymMapResolver>(),
            Mock.Of<IScoringProfileService>());

        // Initialize Lucene index
        LuceneManager.GetWriter("test-index");
    }

    public void Dispose()
    {
        (HnswManager as IDisposable)?.Dispose();
        (VectorSearchService as IDisposable)?.Dispose();
        LuceneManager?.Dispose();

        if (Directory.Exists(TestDir))
        {
            try { Directory.Delete(TestDir, true); } catch { }
        }
    }
}

/// <summary>
/// Collection definition for HNSW integration tests.
/// Tests in this collection share the same fixture instance.
/// </summary>
[CollectionDefinition("HNSW Integration Tests")]
public class HnswTestCollection : ICollectionFixture<HnswTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
