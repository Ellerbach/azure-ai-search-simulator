using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for SkillPipeline diagnostic logging functionality.
/// </summary>
public class SkillPipelineDiagnosticTests
{
    private readonly Mock<ILogger<SkillPipeline>> _loggerMock;
    private readonly Mock<ISkillExecutor> _skillExecutorMock;

    public SkillPipelineDiagnosticTests()
    {
        _loggerMock = new Mock<ILogger<SkillPipeline>>();
        _skillExecutorMock = new Mock<ISkillExecutor>();
        _skillExecutorMock.Setup(x => x.ODataType).Returns("#Microsoft.Skills.Text.SplitSkill");
        _skillExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<Skill>(), It.IsAny<EnrichedDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Succeeded());
    }

    private SkillPipeline CreatePipeline(DiagnosticLoggingSettings settings)
    {
        var options = Options.Create(settings);
        return new SkillPipeline(
            new[] { _skillExecutorMock.Object },
            _loggerMock.Object,
            options);
    }

    private static Skillset CreateTestSkillset()
    {
        return new Skillset
        {
            Name = "test-skillset",
            Skills = new List<Skill>
            {
                new()
                {
                    ODataType = "#Microsoft.Skills.Text.SplitSkill",
                    Name = "test-skill",
                    Context = "/document",
                    Inputs = new List<SkillInput>
                    {
                        new() { Name = "text", Source = "/document/content" }
                    },
                    Outputs = new List<SkillOutput>
                    {
                        new() { Name = "textItems", TargetName = "pages" }
                    }
                }
            }
        };
    }

    private static EnrichedDocument CreateTestDocument()
    {
        return new EnrichedDocument(new Dictionary<string, object?>
        {
            ["content"] = "Test content for processing"
        });
    }

    [Fact]
    public async Task ExecuteAsync_WhenDiagnosticsDisabled_ShouldNotLogDiagnosticMessages()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { Enabled = false };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify no [DIAGNOSTIC] log messages were created
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDiagnosticsEnabled_ShouldLogSkillExecution()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify [DIAGNOSTIC] skill execution messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Starting skill")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDiagnosticsEnabled_ShouldLogSkillCompletion()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify [DIAGNOSTIC] completion messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Completed skill")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSkillExecutionDisabled_ShouldNotLogDiagnosticSkillMessages()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = false 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify no [DIAGNOSTIC] skill execution messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Starting skill")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInputPayloadsEnabled_ShouldLogInputPayload()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillInputPayloads = true 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify [DIAGNOSTIC] INPUT payload messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("INPUT payload")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInputPayloadsDisabled_ShouldNotLogInputPayload()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillInputPayloads = false 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify no INPUT payload messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("INPUT payload")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOutputPayloadsEnabled_ShouldLogOutputPayload()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillOutputPayloads = true 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify [DIAGNOSTIC] OUTPUT payload messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("OUTPUT payload")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnrichedDocumentStateEnabled_ShouldLogDocumentState()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogEnrichedDocumentState = true 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify [DIAGNOSTIC] enriched document state messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Enriched document state")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnrichedDocumentStateDisabled_ShouldNotLogDocumentState()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogEnrichedDocumentState = false 
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify no enriched document state messages were logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Enriched document state")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleSkills_ShouldLogEachSkillWhenEnabled()
    {
        // Arrange
        var customSkillMock = new Mock<ISkillExecutor>();
        customSkillMock.Setup(x => x.ODataType).Returns("#Microsoft.Skills.Custom.WebApiSkill");
        customSkillMock
            .Setup(x => x.ExecuteAsync(It.IsAny<Skill>(), It.IsAny<EnrichedDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Succeeded());

        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true 
        };
        var options = Options.Create(settings);
        var pipeline = new SkillPipeline(
            new[] { _skillExecutorMock.Object, customSkillMock.Object },
            _loggerMock.Object,
            options);

        var skillset = new Skillset
        {
            Name = "multi-skill-test",
            Skills = new List<Skill>
            {
                new()
                {
                    ODataType = "#Microsoft.Skills.Text.SplitSkill",
                    Name = "skill-1",
                    Context = "/document",
                    Inputs = new List<SkillInput> { new() { Name = "text", Source = "/document/content" } },
                    Outputs = new List<SkillOutput> { new() { Name = "textItems", TargetName = "pages" } }
                },
                new()
                {
                    ODataType = "#Microsoft.Skills.Custom.WebApiSkill",
                    Name = "skill-2",
                    Context = "/document",
                    Inputs = new List<SkillInput> { new() { Name = "text", Source = "/document/content" } },
                    Outputs = new List<SkillOutput> { new() { Name = "result", TargetName = "output" } }
                }
            }
        };
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify 2 "Starting skill" messages (one for each skill)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Starting skill")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllDiagnosticsEnabled_ShouldLogAllTypes()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true,
            LogSkillInputPayloads = true,
            LogSkillOutputPayloads = true,
            LogEnrichedDocumentState = true,
            IncludeTimings = true
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify all types of diagnostic logs were created
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(4)); // Start, Input, Output, Document State, Complete
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStillReturnCorrectResult_WithDiagnosticsEnabled()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true,
            LogSkillInputPayloads = true,
            LogSkillOutputPayloads = true
        };
        var pipeline = CreatePipeline(settings);
        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        var result = await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify the pipeline still returns correct results
        Assert.True(result.Success);
        Assert.Single(result.SkillResults);
        Assert.True(result.SkillResults[0].Success);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSkillFails_ShouldStillLogDiagnostics()
    {
        // Arrange
        var failingSkillMock = new Mock<ISkillExecutor>();
        failingSkillMock.Setup(x => x.ODataType).Returns("#Microsoft.Skills.Text.SplitSkill");
        failingSkillMock
            .Setup(x => x.ExecuteAsync(It.IsAny<Skill>(), It.IsAny<EnrichedDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Failed("Test error"));

        var settings = new DiagnosticLoggingSettings 
        { 
            Enabled = true, 
            LogSkillExecution = true,
            LogSkillOutputPayloads = true
        };
        var options = Options.Create(settings);
        var pipeline = new SkillPipeline(
            new[] { failingSkillMock.Object },
            _loggerMock.Object,
            options);

        var skillset = CreateTestSkillset();
        var document = CreateTestDocument();

        // Act
        var result = await pipeline.ExecuteAsync(skillset, document);

        // Assert - Verify diagnostics were still logged despite failure
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Starting skill")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        // Verify completion was logged with Success: False
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[DIAGNOSTIC]") && v.ToString()!.Contains("Completed skill") && v.ToString()!.Contains("False")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        Assert.Contains("Test error", result.Errors);
    }
}
