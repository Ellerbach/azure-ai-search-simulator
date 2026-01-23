using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for Indexer and DataSource models.
/// </summary>
public class IndexerModelTests
{
    [Fact]
    public void Indexer_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var indexer = new Indexer
        {
            Name = "test-indexer",
            DataSourceName = "test-datasource",
            TargetIndexName = "test-index"
        };

        // Assert
        Assert.Equal("test-indexer", indexer.Name);
        Assert.Equal("test-datasource", indexer.DataSourceName);
        Assert.Equal("test-index", indexer.TargetIndexName);
        Assert.Null(indexer.SkillsetName);
        Assert.Null(indexer.FieldMappings);
        Assert.Null(indexer.OutputFieldMappings);
    }

    [Fact]
    public void Indexer_WithFieldMappings_ShouldBeConfigured()
    {
        // Arrange
        var indexer = new Indexer
        {
            Name = "test-indexer",
            DataSourceName = "test-datasource",
            TargetIndexName = "test-index",
            FieldMappings = new List<FieldMapping>
            {
                new()
                {
                    SourceFieldName = "metadata_storage_path",
                    TargetFieldName = "id",
                    MappingFunction = new FieldMappingFunction { Name = "base64Encode" }
                },
                new()
                {
                    SourceFieldName = "content",
                    TargetFieldName = "body"
                }
            }
        };

        // Assert
        Assert.NotNull(indexer.FieldMappings);
        Assert.Equal(2, indexer.FieldMappings.Count);
        Assert.Equal("base64Encode", indexer.FieldMappings[0].MappingFunction?.Name);
    }

    [Fact]
    public void Indexer_WithOutputFieldMappings_ShouldBeConfigured()
    {
        // Arrange
        var indexer = new Indexer
        {
            Name = "enriched-indexer",
            DataSourceName = "test-datasource",
            TargetIndexName = "test-index",
            SkillsetName = "text-skillset",
            OutputFieldMappings = new List<FieldMapping>
            {
                new()
                {
                    SourceFieldName = "/document/merged_content",
                    TargetFieldName = "content"
                },
                new()
                {
                    SourceFieldName = "/document/pages",
                    TargetFieldName = "textChunks"
                }
            }
        };

        // Assert
        Assert.NotNull(indexer.OutputFieldMappings);
        Assert.Equal(2, indexer.OutputFieldMappings.Count);
        Assert.Equal("/document/merged_content", indexer.OutputFieldMappings[0].SourceFieldName);
    }

    [Fact]
    public void DataSource_FileSystem_ShouldBeConfigured()
    {
        // Arrange
        var dataSource = new DataSource
        {
            Name = "local-files",
            Type = "filesystem",
            Credentials = new DataSourceCredentials
            {
                ConnectionString = "c:\\data\\documents"
            },
            Container = new DataSourceContainer
            {
                Name = "pdfs",
                Query = "*.pdf"
            }
        };

        // Assert
        Assert.Equal("local-files", dataSource.Name);
        Assert.Equal("filesystem", dataSource.Type);
        Assert.Equal("c:\\data\\documents", dataSource.Credentials.ConnectionString);
        Assert.Equal("pdfs", dataSource.Container.Name);
        Assert.Equal("*.pdf", dataSource.Container.Query);
    }

    [Fact]
    public void IndexerStatus_ShouldTrackExecutionHistory()
    {
        // Arrange
        var status = new IndexerStatus
        {
            Status = "running",
            LastResult = new IndexerExecutionResult
            {
                Status = "success",
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                EndTime = DateTimeOffset.UtcNow,
                ItemsProcessed = 100,
                ItemsFailed = 2,
                Errors = new List<IndexerExecutionError>
                {
                    new() { Key = "doc1", ErrorMessage = "Parse error", StatusCode = 400 }
                }
            },
            ExecutionHistory = new List<IndexerExecutionResult>()
        };

        // Assert
        Assert.Equal("running", status.Status);
        Assert.Equal("success", status.LastResult.Status);
        Assert.Equal(100, status.LastResult.ItemsProcessed);
        Assert.Equal(2, status.LastResult.ItemsFailed);
        Assert.Single(status.LastResult.Errors);
    }

    [Fact]
    public void FieldMappingFunction_ExtractTokenAtPosition_ShouldHaveParameters()
    {
        // Arrange
        var mapping = new FieldMapping
        {
            SourceFieldName = "path",
            TargetFieldName = "filename",
            MappingFunction = new FieldMappingFunction
            {
                Name = "extractTokenAtPosition",
                Parameters = new Dictionary<string, object>
                {
                    ["delimiter"] = "/",
                    ["position"] = 2
                }
            }
        };

        // Assert
        Assert.NotNull(mapping.MappingFunction?.Parameters);
        Assert.Equal("/", mapping.MappingFunction.Parameters["delimiter"]);
        Assert.Equal(2, mapping.MappingFunction.Parameters["position"]);
    }
}
