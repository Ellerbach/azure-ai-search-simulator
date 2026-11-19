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
/// Integration tests for the status of uploads: 201 when the upload creates the document, 200 when it replaces one,
/// including a document created or deleted earlier in the same batch.
/// </summary>
public class UploadStatusIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly DocumentService _documentService;

    public UploadStatusIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "upload-status-tests", Guid.NewGuid().ToString());
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
    }

    [Fact]
    public async Task IndexDocumentsAsync_SameKeyTwiceInABatch_CreatesThenReplaces()
    {
        // Arrange
        var indexName = SeedIndex();

        // Act
        var statuses = await IndexAsync(indexName, ("upload", "1"), ("upload", "1"));

        // Assert
        Assert.Equal([201, 200], statuses);
    }

    [Fact]
    public async Task IndexDocumentsAsync_UploadOfAnExistingDocument_Replaces()
    {
        // Arrange
        var indexName = SeedIndex();
        await IndexAsync(indexName, ("upload", "1"));

        // Act
        var statuses = await IndexAsync(indexName, ("upload", "1"), ("upload", "2"));

        // Assert
        Assert.Equal([200, 201], statuses);
    }

    [Fact]
    public async Task IndexDocumentsAsync_UploadAfterADeleteInTheSameBatch_Creates()
    {
        // Arrange
        var indexName = SeedIndex();
        await IndexAsync(indexName, ("upload", "1"));

        // Act
        var statuses = await IndexAsync(indexName, ("delete", "1"), ("upload", "1"));

        // Assert
        Assert.Equal([200, 201], statuses);
    }

    private string SeedIndex()
    {
        var indexName = $"upload-status-{Guid.NewGuid():N}";
        var index = new SearchIndex
        {
            Name = indexName,
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "title", Type = "Edm.String", Searchable = true },
            ],
        };
        _indexServiceMock.Setup(x => x.GetIndexAsync(indexName, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _luceneManager.GetWriter(indexName);
        return indexName;
    }

    private async Task<int[]> IndexAsync(string indexName, params (string Type, string Key)[] actions)
    {
        var response = await _documentService.IndexDocumentsAsync(indexName, new IndexDocumentsRequest
        {
            Value = actions.Select(a => new IndexAction
            {
                ["@search.action"] = a.Type,
                ["id"] = a.Key,
                ["title"] = "t",
            }).ToList(),
        });

        return response.Value.Select(r => r.StatusCode).ToArray();
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
