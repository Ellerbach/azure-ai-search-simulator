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

namespace AzureAISearchSimulator.Api.Tests.Services;

/// <summary>
/// Tests for index projections support in the indexer pipeline.
/// Validates that skillsets with indexProjections correctly fan out
/// enriched documents into child documents and route them to the right index.
/// </summary>
public class IndexerServiceProjectionTests
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

    public IndexerServiceProjectionTests()
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
    /// With skipIndexingParentDocuments, only child (projected) documents should be indexed.
    /// Parent document should NOT be sent to any index.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_SkipParent_OnlyChildDocsIndexed()
    {
        // Arrange
        var indexerName = "proj-indexer";
        var skillset = CreateSkillsetWithProjections(
            targetIndex: "chunks-index",
            projectionMode: "skipIndexingParentDocuments");

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Hello world");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                // Simulate skill pipeline producing 3 chunks
                enrichedDoc.SetValue("/document/chunks", new[]
                {
                    "chunk zero content",
                    "chunk one content",
                    "chunk two content"
                });
                enrichedDoc.SetValue("/document/title", "My Document");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert
        Assert.Single(capturedRequests);
        var (indexName, request) = capturedRequests[0];
        Assert.Equal("chunks-index", indexName);
        Assert.Equal(3, request.Value.Count);

        // Verify child documents have correct projected keys
        Assert.Equal("doc1_chunks_0", request.Value[0]["chunk_id"]);
        Assert.Equal("doc1_chunks_1", request.Value[1]["chunk_id"]);
        Assert.Equal("doc1_chunks_2", request.Value[2]["chunk_id"]);

        // Verify parent key field is set on each child
        foreach (var action in request.Value)
        {
            Assert.Equal("doc1", action["parent_id"]);
        }
    }

    /// <summary>
    /// With includeIndexingParentDocuments, both parent and child documents should be indexed.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_IncludeParent_ParentAndChildDocsIndexed()
    {
        // Arrange
        var indexerName = "proj-include-parent";
        var skillset = CreateSkillsetWithProjections(
            targetIndex: "chunks-index",
            projectionMode: "includeIndexingParentDocuments");

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Hello world");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/chunks", new[] { "chunk A", "chunk B" });
                enrichedDoc.SetValue("/document/title", "My Doc");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — should have uploads to both the child index and parent index
        Assert.True(capturedRequests.Count >= 1);

        // Collect all actions across all requests
        var allActions = capturedRequests.SelectMany(r => r.Request.Value
            .Select(a => (r.IndexName, Action: a))).ToList();

        // 2 child docs + 1 parent doc = 3 total
        Assert.Equal(3, allActions.Count);

        // Verify child docs go to chunks-index
        var childActions = allActions.Where(a => a.IndexName == "chunks-index").ToList();
        Assert.Equal(2, childActions.Count);

        // Verify parent doc goes to default index
        var parentActions = allActions.Where(a => a.IndexName == "parent-index").ToList();
        Assert.Single(parentActions);
    }

    /// <summary>
    /// When projectionMode is null/not specified, it defaults to includeIndexingParentDocuments.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_NullMode_DefaultsToIncludeParent()
    {
        // Arrange
        var indexerName = "proj-default-mode";
        var skillset = CreateSkillsetWithProjections(
            targetIndex: "chunks-index",
            projectionMode: null); // Not specified

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Hello world");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/chunks", new[] { "chunk" });
                enrichedDoc.SetValue("/document/title", "Doc");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — parent should be included (default behavior)
        var allActions = capturedRequests.SelectMany(r => r.Request.Value
            .Select(a => (r.IndexName, Action: a))).ToList();

        // 1 child + 1 parent = 2
        Assert.Equal(2, allActions.Count);
    }

    /// <summary>
    /// Mappings with parent-level source paths (e.g., /document/title) should 
    /// repeat the parent value in each child document.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_ParentFieldsMappedToEachChild()
    {
        // Arrange
        var indexerName = "proj-parent-fields";
        var skillset = CreateSkillsetWithProjections(
            targetIndex: "chunks-index",
            projectionMode: "skipIndexingParentDocuments");

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Some content");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/chunks", new[] { "chunk 0", "chunk 1" });
                enrichedDoc.SetValue("/document/title", "My Report Title");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — title (parent-level) should appear in each child
        var actions = capturedRequests.SelectMany(r => r.Request.Value).ToList();
        Assert.Equal(2, actions.Count);

        foreach (var action in actions)
        {
            Assert.Equal("My Report Title", action["title"]?.ToString());
        }
    }

    /// <summary>
    /// Child-level mapping source paths with wildcards should resolve to each child's value.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_ChildFieldsResolvedPerChunk()
    {
        // Arrange
        var indexerName = "proj-child-fields";

        // Skillset with a mapping that uses /document/chunks/* (child's own value) as "content"
        var skillset = new Skillset
        {
            Name = "child-field-skillset",
            Skills = new List<Skill>(),
            IndexProjections = new IndexProjections
            {
                Selectors = new List<IndexProjectionSelector>
                {
                    new()
                    {
                        TargetIndexName = "chunks-index",
                        ParentKeyFieldName = "parent_id",
                        SourceContext = "/document/chunks/*",
                        Mappings = new List<IndexProjectionMapping>
                        {
                            new() { Name = "content", Source = "/document/chunks/*" },
                            new() { Name = "title", Source = "/document/title" }
                        }
                    }
                },
                Parameters = new IndexProjectionParameters
                {
                    ProjectionMode = "skipIndexingParentDocuments"
                }
            }
        };

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Some content");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/chunks", new[] { "Alpha text", "Beta text", "Gamma text" });
                enrichedDoc.SetValue("/document/title", "Parent Title");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert
        var actions = capturedRequests.SelectMany(r => r.Request.Value).ToList();
        Assert.Equal(3, actions.Count);

        // Each child should get its own content value
        Assert.Equal("Alpha text", actions[0]["content"]?.ToString());
        Assert.Equal("Beta text", actions[1]["content"]?.ToString());
        Assert.Equal("Gamma text", actions[2]["content"]?.ToString());

        // Title (parent field) should repeat
        Assert.All(actions, a => Assert.Equal("Parent Title", a["title"]?.ToString()));
    }

    /// <summary>
    /// Verifies that projected key format is {parentKey}_{contextSegment}_{index}.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_ProjectedKeyFormatCorrect()
    {
        // Arrange
        var indexerName = "proj-key-format";
        var skillset = new Skillset
        {
            Name = "key-format-skillset",
            Skills = new List<Skill>(),
            IndexProjections = new IndexProjections
            {
                Selectors = new List<IndexProjectionSelector>
                {
                    new()
                    {
                        TargetIndexName = "chunks-index",
                        ParentKeyFieldName = "parent_id",
                        SourceContext = "/document/extracted_chunks/*",
                        Mappings = new List<IndexProjectionMapping>
                        {
                            new() { Name = "content", Source = "/document/extracted_chunks/*" }
                        }
                    }
                },
                Parameters = new IndexProjectionParameters
                {
                    ProjectionMode = "skipIndexingParentDocuments"
                }
            }
        };

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("myparent", "Content");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/extracted_chunks", new[] { "c0", "c1" });
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — key format: {parentKey}_{contextSegment}_{index}
        var actions = capturedRequests.SelectMany(r => r.Request.Value).ToList();
        Assert.Equal("myparent_extracted_chunks_0", actions[0]["chunk_id"]);
        Assert.Equal("myparent_extracted_chunks_1", actions[1]["chunk_id"]);
    }

    /// <summary>
    /// When a skillset has NO indexProjections, the standard single-document behavior
    /// should be preserved (backward compatibility).
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutProjections_StandardBehaviorPreserved()
    {
        // Arrange
        var indexerName = "no-proj-indexer";
        var skillset = new Skillset
        {
            Name = "no-proj-skillset",
            Skills = new List<Skill>()
            // No IndexProjections
        };

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Hello");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/title", "Plain doc");
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — standard behavior: 1 document, to parent-index
        Assert.Single(capturedRequests);
        Assert.Equal("parent-index", capturedRequests[0].IndexName);
        Assert.Single(capturedRequests[0].Request.Value);
    }

    /// <summary>
    /// Multiple selectors targeting different indexes should route actions correctly.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithMultipleSelectors_ActionsRoutedToCorrectIndexes()
    {
        // Arrange
        var indexerName = "proj-multi-selector";
        var skillset = new Skillset
        {
            Name = "multi-selector-skillset",
            Skills = new List<Skill>(),
            IndexProjections = new IndexProjections
            {
                Selectors = new List<IndexProjectionSelector>
                {
                    new()
                    {
                        TargetIndexName = "index-a",
                        ParentKeyFieldName = "parent_id",
                        SourceContext = "/document/chunks/*",
                        Mappings = new List<IndexProjectionMapping>
                        {
                            new() { Name = "content", Source = "/document/chunks/*" }
                        }
                    },
                    new()
                    {
                        TargetIndexName = "index-b",
                        ParentKeyFieldName = "parent_id",
                        SourceContext = "/document/summaries/*",
                        Mappings = new List<IndexProjectionMapping>
                        {
                            new() { Name = "summary", Source = "/document/summaries/*" }
                        }
                    }
                },
                Parameters = new IndexProjectionParameters
                {
                    ProjectionMode = "skipIndexingParentDocuments"
                }
            }
        };

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Content");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                enrichedDoc.SetValue("/document/chunks", new[] { "chunk0", "chunk1" });
                enrichedDoc.SetValue("/document/summaries", new[] { "summary0" });
            });

        var capturedRequests = new List<(string IndexName, IndexDocumentsRequest Request)>();
        _documentServiceMock
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()))
            .Callback<string, IndexDocumentsRequest>((idx, req) => capturedRequests.Add((idx, req)))
            .ReturnsAsync(new IndexDocumentsResponse());

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — actions grouped by target index
        var indexARequests = capturedRequests.Where(r => r.IndexName == "index-a").ToList();
        var indexBRequests = capturedRequests.Where(r => r.IndexName == "index-b").ToList();

        Assert.Single(indexARequests);
        Assert.Equal(2, indexARequests[0].Request.Value.Count); // 2 chunks

        Assert.Single(indexBRequests);
        Assert.Single(indexBRequests[0].Request.Value); // 1 summary
    }

    /// <summary>
    /// When sourceContext yields 0 children (empty array), no child documents should be produced.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithProjections_EmptySourceContext_NoChildDocs()
    {
        // Arrange
        var indexerName = "proj-empty-context";
        var skillset = CreateSkillsetWithProjections(
            targetIndex: "chunks-index",
            projectionMode: "skipIndexingParentDocuments");

        var indexer = CreateIndexerWithSkillset(indexerName, skillset.Name);
        var dataSource = CreateDataSource();
        var doc = CreateTextDocument("doc1", "Empty chunking");

        SetupMocksWithSkillset(indexerName, indexer, dataSource, [doc], skillset,
            enrichAction: enrichedDoc =>
            {
                // Set an empty array for chunks
                enrichedDoc.SetValue("/document/chunks", Array.Empty<string>());
                enrichedDoc.SetValue("/document/title", "Empty");
            });

        // Act
        await _sut.RunAsync(indexerName);

        // Assert — no documents should be indexed
        _documentServiceMock.Verify(
            x => x.IndexDocumentsAsync(It.IsAny<string>(), It.IsAny<IndexDocumentsRequest>()),
            Times.Never);
    }

    #region Helpers

    private static Skillset CreateSkillsetWithProjections(
        string targetIndex, string? projectionMode)
    {
        return new Skillset
        {
            Name = "test-skillset",
            Skills = new List<Skill>(),
            IndexProjections = new IndexProjections
            {
                Selectors = new List<IndexProjectionSelector>
                {
                    new()
                    {
                        TargetIndexName = targetIndex,
                        ParentKeyFieldName = "parent_id",
                        SourceContext = "/document/chunks/*",
                        Mappings = new List<IndexProjectionMapping>
                        {
                            new() { Name = "content", Source = "/document/chunks/*" },
                            new() { Name = "title", Source = "/document/title" }
                        }
                    }
                },
                Parameters = projectionMode != null
                    ? new IndexProjectionParameters { ProjectionMode = projectionMode }
                    : null
            }
        };
    }

    private static Indexer CreateIndexerWithSkillset(string name, string skillsetName) => new()
    {
        Name = name,
        DataSourceName = "test-ds",
        TargetIndexName = "parent-index",
        SkillsetName = skillsetName,
        FieldMappings = new List<FieldMapping>
        {
            new() { SourceFieldName = "metadata_storage_path", TargetFieldName = "metadata_storage_path" }
        }
    };

    private static DataSource CreateDataSource() => new()
    {
        Name = "test-ds",
        Type = "filesystem",
        Container = new DataSourceContainer { Name = "data" }
    };

    private DataSourceDocument CreateTextDocument(string key, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new DataSourceDocument
        {
            Key = key,
            Name = $"{key}.txt",
            ContentType = "text/plain",
            Content = bytes,
            Size = bytes.Length
        };
    }

    private void SetupMocksWithSkillset(
        string indexerName, Indexer indexer, DataSource dataSource,
        DataSourceDocument[] documents, Skillset skillset,
        Action<EnrichedDocument> enrichAction)
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

        // Document cracker: return content as-is for text/plain
        _documentCrackerFactoryMock
            .Setup(x => x.CanCrack(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false); // Let the fallback text extraction handle it

        // Skillset service returns our skillset
        _skillsetServiceMock
            .Setup(x => x.GetAsync(skillset.Name))
            .ReturnsAsync(skillset);

        // Index service: return SearchIndex schemas for target indexes
        // so BuildProjectedDocumentsAsync can resolve key field names
        _indexServiceMock
            .Setup(x => x.GetIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => new SearchIndex
            {
                Name = name,
                Fields = new List<SearchField>
                {
                    new() { Name = "chunk_id", Type = "Edm.String", Key = true }
                }
            });

        // Skill pipeline: simulate enrichment by calling the enrichAction
        _skillPipelineMock
            .Setup(x => x.ExecuteAsync(skillset, It.IsAny<EnrichedDocument>(), It.IsAny<CancellationToken>()))
            .Callback<Skillset, EnrichedDocument, CancellationToken>((_, doc, _) => enrichAction(doc))
            .ReturnsAsync(new SkillPipelineResult { Success = true });
    }

    #endregion
}
