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
/// Tests that the key field mapping function (e.g., base64Encode) is applied
/// before document key validation in JSON parsing mode.
/// </summary>
public class IndexerServiceKeyMappingTests
{
    private readonly Mock<IIndexerRepository> _repositoryMock;
    private readonly Mock<IDataSourceService> _dataSourceServiceMock;
    private readonly Mock<ISkillsetService> _skillsetServiceMock;
    private readonly Mock<IIndexService> _indexServiceMock;
    private readonly Mock<IDataSourceConnectorFactory> _connectorFactoryMock;
    private readonly Mock<IDocumentCrackerFactory> _documentCrackerFactoryMock;
    private readonly Mock<ISkillPipeline> _skillPipelineMock;
    private readonly Mock<IDocumentService> _documentServiceMock;
    private readonly Mock<ILogger<IndexerService>> _loggerMock;
    private readonly Mock<IDataSourceConnector> _connectorMock;
    private readonly IndexerService _sut;

    public IndexerServiceKeyMappingTests()
    {
        _repositoryMock = new Mock<IIndexerRepository>();
        _dataSourceServiceMock = new Mock<IDataSourceService>();
        _skillsetServiceMock = new Mock<ISkillsetService>();
        _indexServiceMock = new Mock<IIndexService>();
        _connectorFactoryMock = new Mock<IDataSourceConnectorFactory>();
        _documentCrackerFactoryMock = new Mock<IDocumentCrackerFactory>();
        _skillPipelineMock = new Mock<ISkillPipeline>();
        _documentServiceMock = new Mock<IDocumentService>();
        _loggerMock = new Mock<ILogger<IndexerService>>();
        _connectorMock = new Mock<IDataSourceConnector>();

        var diagnosticOptions = Options.Create(new DiagnosticLoggingSettings());

        _sut = new IndexerService(
            _repositoryMock.Object,
            _dataSourceServiceMock.Object,
            _skillsetServiceMock.Object,
            _indexServiceMock.Object,
            _connectorFactoryMock.Object,
            _documentCrackerFactoryMock.Object,
            _skillPipelineMock.Object,
            _documentServiceMock.Object,
            _loggerMock.Object,
            diagnosticOptions);
    }

    /// <summary>
    /// Verifies that a JSON document with an id containing dots (e.g., "10152.3stk02")
    /// is successfully indexed when a base64Encode field mapping is configured on the key field.
    /// This validates the fix: key mapping must be applied BEFORE ValidateDocumentKey.
    /// </summary>
    [Fact]
    public async Task RunAsync_JsonMode_WithBase64EncodeKeyMapping_AcceptsKeysWithDots()
    {
        // Arrange
        var indexerName = "lego-indexer";
        var jsonId = "10152.3stk02"; // Key with dots — invalid without encoding

        var indexer = CreateJsonIndexerWithBase64KeyMapping(indexerName);
        var dataSource = CreateDataSource();
        var jsonDoc = CreateJsonDocument(jsonId, "LEGO Brick 2x4");

        SetupMocks(indexerName, indexer, dataSource, new[] { jsonDoc });

        IndexDocumentsRequest? capturedRequest = null;
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(indexer.TargetIndexName, It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((_, req) => capturedRequest = req)
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — document was indexed (not rejected by key validation)
        _documentServiceMock.Verify(
            x => x.IndexDocumentsAsync(indexer.TargetIndexName, It.IsAny<IndexDocumentsRequest>()),
            Times.Once);

        Assert.NotNull(capturedRequest);
        Assert.Single(capturedRequest!.Value);

        // The key in the indexed document should be the base64-encoded value
        var expectedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonId))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var indexedDoc = capturedRequest.Value[0];
        Assert.Equal(expectedKey, indexedDoc["id"]);
    }

    /// <summary>
    /// Verifies that a JSON document with a key containing dots FAILS when
    /// no key mapping function is configured (proving the validation works).
    /// </summary>
    [Fact]
    public async Task RunAsync_JsonMode_WithoutKeyMapping_RejectsKeysWithDots()
    {
        // Arrange
        var indexerName = "lego-indexer-nomapping";
        var jsonId = "10152.3stk02"; // Key with dots — should fail without encoding

        var indexer = new Indexer
        {
            Name = indexerName,
            DataSourceName = "lego-ds",
            TargetIndexName = "lego-index",
            Parameters = new IndexerParameters
            {
                Configuration = new IndexerConfiguration { ParsingMode = "json" }
            }
            // No fieldMappings — key won't be encoded
        };

        var dataSource = CreateDataSource();
        var jsonDoc = CreateJsonDocument(jsonId, "LEGO Brick 2x4");

        SetupMocks(indexerName, indexer, dataSource, new[] { jsonDoc });

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — document was NOT indexed (key validation must have failed)
        _documentServiceMock.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()),
            Times.Never);

        // Verify the status was saved with a failure
        _repositoryMock.Verify(
            x => x.SaveStatusAsync(indexerName, It.Is<IndexerStatus>(s =>
                s.LastResult != null && s.LastResult.ItemsFailed > 0)),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that multiple JSON documents with various special-character keys
    /// are all accepted when base64Encode key mapping is configured.
    /// </summary>
    [Theory]
    [InlineData("10152.3stk02")]       // dot
    [InlineData("1076.1_10")]          // dot with underscore
    [InlineData("14728c21.6")]         // dot at end
    [InlineData("part/sub")]           // slash
    [InlineData("key with space")]     // space
    [InlineData("spécial+chars")]      // unicode + plus sign
    public async Task RunAsync_JsonMode_WithBase64EncodeKeyMapping_AcceptsVariousSpecialCharKeys(string jsonId)
    {
        // Arrange
        var indexerName = "special-chars-indexer";
        var indexer = CreateJsonIndexerWithBase64KeyMapping(indexerName);
        var dataSource = CreateDataSource();
        var jsonDoc = CreateJsonDocument(jsonId, "Test item");

        SetupMocks(indexerName, indexer, dataSource, new[] { jsonDoc });

        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(indexer.TargetIndexName, It.IsAny<IndexDocumentsRequest>()))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — document was indexed successfully
        _documentServiceMock.Verify(
            x => x.IndexDocumentsAsync(indexer.TargetIndexName, It.IsAny<IndexDocumentsRequest>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that keys without special characters work with or without base64Encode mapping.
    /// </summary>
    [Fact]
    public async Task RunAsync_JsonMode_ValidKeyWithBase64Encode_StillWorks()
    {
        // Arrange
        var indexerName = "valid-key-indexer";
        var jsonId = "simple_key-123"; // Already valid — no special chars

        var indexer = CreateJsonIndexerWithBase64KeyMapping(indexerName);
        var dataSource = CreateDataSource();
        var jsonDoc = CreateJsonDocument(jsonId, "Simple item");

        SetupMocks(indexerName, indexer, dataSource, new[] { jsonDoc });

        IndexDocumentsRequest? capturedRequest = null;
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(indexer.TargetIndexName, It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((_, req) => capturedRequest = req)
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — the key is base64-encoded (even though it was already valid)
        Assert.NotNull(capturedRequest);
        Assert.Single(capturedRequest!.Value);

        var expectedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonId))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        Assert.Equal(expectedKey, capturedRequest.Value[0]["id"]);
    }

    #region Helpers

    private static Indexer CreateJsonIndexerWithBase64KeyMapping(string name) => new()
    {
        Name = name,
        DataSourceName = "lego-ds",
        TargetIndexName = "lego-index",
        Parameters = new IndexerParameters
        {
            Configuration = new IndexerConfiguration { ParsingMode = "json" }
        },
        FieldMappings = new List<FieldMapping>
        {
            new()
            {
                SourceFieldName = "id",
                TargetFieldName = "id",
                MappingFunction = new FieldMappingFunction { Name = "base64Encode" }
            }
        }
    };

    private static DataSource CreateDataSource() => new()
    {
        Name = "lego-ds",
        Type = "filesystem",
        Container = new DataSourceContainer { Name = "data" }
    };

    private DataSourceDocument CreateJsonDocument(string id, string name)
    {
        var json = JsonSerializer.Serialize(new { id, name });
        var content = Encoding.UTF8.GetBytes(json);

        return new DataSourceDocument
        {
            // sourceDoc.Key is base64-encoded by the connector, so it's always valid
            Key = Convert.ToBase64String(Encoding.UTF8.GetBytes($"test/{id}.json"))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('='),
            Name = $"{id}.json",
            ContentType = "application/json",
            Content = content,
            Size = content.Length
        };
    }

    private void SetupMocks(string indexerName, Indexer indexer, DataSource dataSource,
        DataSourceDocument[] documents)
    {
        _repositoryMock
            .Setup(x => x.GetAsync(indexerName))
            .ReturnsAsync(indexer);

        _repositoryMock
            .Setup(x => x.GetStatusAsync(indexerName))
            .ReturnsAsync(new IndexerStatus());

        _repositoryMock
            .Setup(x => x.SaveStatusAsync(indexerName, It.IsAny<IndexerStatus>()))
            .Returns(Task.CompletedTask);

        _dataSourceServiceMock
            .Setup(x => x.GetAsync(indexer.DataSourceName))
            .ReturnsAsync(dataSource);

        _connectorMock
            .Setup(x => x.ListDocumentsAsync(dataSource, It.IsAny<string?>()))
            .ReturnsAsync(documents);

        _connectorFactoryMock
            .Setup(x => x.GetConnector(dataSource.Type))
            .Returns(_connectorMock.Object);
    }

    #endregion
}
