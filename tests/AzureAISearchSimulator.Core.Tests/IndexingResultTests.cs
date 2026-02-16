using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Tests for document indexing result parity with Azure AI Search:
/// - Upload new doc returns statusCode 201
/// - Upload existing doc (overwrite) returns statusCode 200
/// - Merge returns statusCode 200
/// - MergeOrUpload returns 201 (new) or 200 (existing)
/// - Delete returns statusCode 200
/// - errorMessage is always serialized (even as null)
/// </summary>
public class IndexingResultTests : IDisposable
{
    private readonly string _testDir;
    private readonly LuceneIndexManager _luceneManager;
    private readonly DocumentService _documentService;
    private readonly SearchIndex _testIndex;

    public IndexingResultTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "indexing-result-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        var luceneSettings = Options.Create(new LuceneSettings { IndexPath = _testDir });
        _luceneManager = new LuceneIndexManager(
            Mock.Of<ILogger<LuceneIndexManager>>(),
            luceneSettings);

        var vectorSearchService = Mock.Of<IVectorSearchService>();

        _testIndex = new SearchIndex
        {
            Name = "status-test",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            }
        };

        var indexServiceMock = new Mock<IIndexService>();
        indexServiceMock.Setup(x => x.GetIndexAsync("status-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testIndex);

        _documentService = new DocumentService(
            Mock.Of<ILogger<DocumentService>>(),
            _luceneManager,
            vectorSearchService,
            indexServiceMock.Object);
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

    // ─── StatusCode tests ─────────────────────────────────────────────

    [Fact]
    public async Task Upload_NewDocument_ReturnsStatusCode201()
    {
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "upload-1",
                    ["title"] = "Test"
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", request);

        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        Assert.Equal(201, response.Value[0].StatusCode);
    }

    [Fact]
    public async Task Upload_ExistingDocument_ReturnsStatusCode200()
    {
        // First upload (new doc → 201)
        var firstUpload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "upload-overwrite",
                    ["title"] = "Original"
                })
            }
        };
        var firstResponse = await _documentService.IndexDocumentsAsync("status-test", firstUpload);
        Assert.Equal(201, firstResponse.Value[0].StatusCode);

        // Re-upload same doc (overwrite → 200)
        var secondUpload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "upload-overwrite",
                    ["title"] = "Updated"
                })
            }
        };
        var secondResponse = await _documentService.IndexDocumentsAsync("status-test", secondUpload);

        Assert.Single(secondResponse.Value);
        Assert.True(secondResponse.Value[0].Status);
        Assert.Equal(200, secondResponse.Value[0].StatusCode);
    }

    [Fact]
    public async Task Upload_MultipleDocuments_AllReturn201()
    {
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?> { ["id"] = "batch-1", ["title"] = "A" }),
                CreateAction("upload", new Dictionary<string, object?> { ["id"] = "batch-2", ["title"] = "B" }),
                CreateAction("upload", new Dictionary<string, object?> { ["id"] = "batch-3", ["title"] = "C" })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", request);

        Assert.Equal(3, response.Value.Count);
        Assert.All(response.Value, r =>
        {
            Assert.True(r.Status);
            Assert.Equal(201, r.StatusCode);
        });
    }

    [Fact]
    public async Task Merge_ExistingDocument_ReturnsStatusCode200()
    {
        // First upload
        var upload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "merge-1", ["title"] = "Original", ["rating"] = 3.0
                })
            }
        };
        await _documentService.IndexDocumentsAsync("status-test", upload);

        // Then merge
        var merge = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("merge", new Dictionary<string, object?>
                {
                    ["id"] = "merge-1", ["rating"] = 4.5
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", merge);

        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        Assert.Equal(200, response.Value[0].StatusCode);
    }

    [Fact]
    public async Task Merge_NonExistentDocument_ReturnsStatusCode404()
    {
        var merge = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("merge", new Dictionary<string, object?>
                {
                    ["id"] = "nonexistent", ["rating"] = 4.5
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", merge);

        Assert.Single(response.Value);
        Assert.False(response.Value[0].Status);
        Assert.Equal(404, response.Value[0].StatusCode);
    }

    [Fact]
    public async Task MergeOrUpload_NewDocument_ReturnsStatusCode201()
    {
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("mergeOrUpload", new Dictionary<string, object?>
                {
                    ["id"] = "mou-new", ["title"] = "New Doc"
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", request);

        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        Assert.Equal(201, response.Value[0].StatusCode);
    }

    [Fact]
    public async Task MergeOrUpload_ExistingDocument_ReturnsStatusCode200()
    {
        // First upload
        var upload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "mou-existing", ["title"] = "Original"
                })
            }
        };
        await _documentService.IndexDocumentsAsync("status-test", upload);

        // Then mergeOrUpload
        var mergeOrUpload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("mergeOrUpload", new Dictionary<string, object?>
                {
                    ["id"] = "mou-existing", ["title"] = "Updated"
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", mergeOrUpload);

        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        Assert.Equal(200, response.Value[0].StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsStatusCode200()
    {
        // Upload first
        var upload = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "del-1", ["title"] = "To Delete"
                })
            }
        };
        await _documentService.IndexDocumentsAsync("status-test", upload);

        // Then delete
        var delete = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("delete", new Dictionary<string, object?>
                {
                    ["id"] = "del-1"
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", delete);

        Assert.Single(response.Value);
        Assert.True(response.Value[0].Status);
        Assert.Equal(200, response.Value[0].StatusCode);
    }

    // ─── errorMessage serialization tests ─────────────────────────────

    [Fact]
    public async Task SuccessfulUpload_ErrorMessageIsNull_ButSerialized()
    {
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("upload", new Dictionary<string, object?>
                {
                    ["id"] = "err-test-1", ["title"] = "Test"
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", request);
        var result = response.Value[0];

        Assert.True(result.Status);
        Assert.Null(result.ErrorMessage);

        // Verify that errorMessage is present in JSON even when null
        // (using WhenWritingNull globally, but overridden on ErrorMessage)
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(result, options);
        Assert.Contains("\"errorMessage\"", json);
        Assert.Contains("\"errorMessage\":null", json);
    }

    [Fact]
    public async Task FailedMerge_ErrorMessageIsPopulated()
    {
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction>
            {
                CreateAction("merge", new Dictionary<string, object?>
                {
                    ["id"] = "nonexistent-err", ["rating"] = 5.0
                })
            }
        };

        var response = await _documentService.IndexDocumentsAsync("status-test", request);
        var result = response.Value[0];

        Assert.False(result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage);

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(result, options);
        Assert.Contains("\"errorMessage\"", json);
        Assert.DoesNotContain("\"errorMessage\":null", json);
    }
}
