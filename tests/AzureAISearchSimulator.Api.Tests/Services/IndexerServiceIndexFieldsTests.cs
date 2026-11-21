using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.DataSources;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Skills;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using System.Text.Json;

namespace AzureAISearchSimulator.Api.Tests.Services;

/// <summary>
/// Tests how an indexer's documents meet the target index: as in Azure, source and enrichment fields the index
/// doesn't define (such as <c>metadata_storage_path</c>) are ignored rather than failing the batch, values keep their
/// precision, and change detection reads the stored document rather than a lookup's response.
/// </summary>
public class IndexerServiceIndexFieldsTests
{
    private readonly Mock<IIndexerRepository> _repositoryMock = new();
    private readonly Mock<IDataSourceService> _dataSourceServiceMock = new();
    private readonly Mock<IIndexService> _indexServiceMock = new();
    private readonly Mock<IDataSourceConnectorFactory> _connectorFactoryMock = new();
    private readonly Mock<IDataSourceConnector> _connectorMock = new();
    private readonly Mock<IDocumentService> _documentServiceMock = new();
    private readonly IndexerService _sut;

    public IndexerServiceIndexFieldsTests()
    {
        _sut = new IndexerService(
            _repositoryMock.Object,
            _dataSourceServiceMock.Object,
            Mock.Of<ISkillsetService>(),
            _indexServiceMock.Object,
            _connectorFactoryMock.Object,
            Mock.Of<IDocumentCrackerFactory>(),
            Mock.Of<ISkillPipeline>(),
            _documentServiceMock.Object,
            Mock.Of<ILogger<IndexerService>>(),
            Options.Create(new DiagnosticLoggingSettings()));
    }

    [Fact]
    public async Task RunAsync_SourceHasFieldsTheIndexDoesNotDefine_UploadsOnlyIndexFields()
    {
        // Arrange
        SetupIndexer(new SearchIndex
        {
            Name = "products",
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "name", Type = "Edm.String", Searchable = true },
            ],
        });
        IndexDocumentsRequest? uploaded = null;
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync("products", It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((_, request) => uploaded = request)
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync("products-indexer");

        // Assert
        var document = Assert.Single(uploaded!.Value);
        Assert.Equal(["@search.action", "id", "name"], document.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Brick", document["name"]?.ToString());
    }

    [Fact]
    public async Task RunAsync_SourceHasTheLargestInt64_UploadsItExactly()
    {
        // Arrange
        SetupIndexer(new SearchIndex
        {
            Name = "products",
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "stock", Type = "Edm.Int64" },
            ],
        }, new { id = "p1", stock = long.MaxValue });
        IndexDocumentsRequest? uploaded = null;
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync("products", It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((_, request) => uploaded = request)
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync("products-indexer");

        // Assert
        Assert.Equal("9223372036854775807", JsonSerializer.Serialize(Assert.Single(uploaded!.Value)["stock"]));
    }

    [Fact]
    public async Task RunAsync_HighWaterMarkFieldNotRetrievable_SkipsUnchangedDocument()
    {
        // Arrange: the stored high-water mark has more precision than Azure returns, in a field lookups don't return.
        var lastModified = DateTimeOffset.Parse("2026-01-01T00:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture);
        SetupIndexer(new SearchIndex
        {
            Name = "products",
            Fields =
            [
                new SearchField { Name = "id", Type = "Edm.String", Key = true },
                new SearchField { Name = "metadata_storage_last_modified", Type = "Edm.DateTimeOffset", Retrievable = false },
            ],
        }, lastModified: lastModified);
        _documentServiceMock
            .Setup(x => x.GetDocumentAsync("products", "cDEuanNvbg", null))
            .ReturnsAsync(new Dictionary<string, object?> { ["id"] = "cDEuanNvbg" });
        _documentServiceMock
            .Setup(x => x.GetStoredDocumentAsync("products", "cDEuanNvbg"))
            .ReturnsAsync(new Dictionary<string, object?> { ["id"] = "cDEuanNvbg", ["metadata_storage_last_modified"] = lastModified.ToString("O") });

        // Act
        await _sut.RunAsync("products-indexer");

        // Assert
        _documentServiceMock.Verify(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()), Times.Never);
    }

    private void SetupIndexer(SearchIndex index, object? source = null, DateTimeOffset? lastModified = null)
    {
        var indexer = new Indexer
        {
            Name = "products-indexer",
            DataSourceName = "products-ds",
            TargetIndexName = index.Name,
            Parameters = new IndexerParameters { Configuration = new IndexerConfiguration { ParsingMode = "json" } },
        };
        var dataSource = new DataSource
        {
            Name = "products-ds",
            Type = "filesystem",
            Container = new DataSourceContainer { Name = "data" },
            DataChangeDetectionPolicy = lastModified is null ? null : new DataChangeDetectionPolicy { HighWaterMarkColumnName = "metadata_storage_last_modified" },
        };
        var content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source ?? new { id = "p1", name = "Brick", colour = "red" }));

        _repositoryMock.Setup(x => x.GetAsync(indexer.Name)).ReturnsAsync(indexer);
        _repositoryMock.Setup(x => x.GetStatusAsync(indexer.Name)).ReturnsAsync(new IndexerStatus());
        _repositoryMock.Setup(x => x.SaveStatusAsync(indexer.Name, It.IsAny<IndexerStatus>())).Returns(Task.CompletedTask);
        _dataSourceServiceMock.Setup(x => x.GetAsync(dataSource.Name)).ReturnsAsync(dataSource);
        _indexServiceMock.Setup(x => x.GetIndexAsync(index.Name, It.IsAny<CancellationToken>())).ReturnsAsync(index);
        _connectorFactoryMock.Setup(x => x.GetConnector(dataSource.Type)).Returns(_connectorMock.Object);
        _connectorMock
            .Setup(x => x.ListDocumentsAsync(dataSource, It.IsAny<string?>()))
            .ReturnsAsync(
            [
                new DataSourceDocument
                {
                    Key = "cDEuanNvbg",
                    Name = "p1.json",
                    ContentType = "application/json",
                    Content = content,
                    Size = content.Length,
                    LastModified = lastModified,
                },
            ]);
    }
}
