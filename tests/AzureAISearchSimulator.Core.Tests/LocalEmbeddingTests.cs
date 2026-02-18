using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for local embedding mode (local:// URI) in AzureOpenAIEmbeddingSkillExecutor
/// and the LocalOnnxEmbeddingService.
/// </summary>
public class LocalEmbeddingTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<AzureOpenAIEmbeddingSkillExecutor>> _loggerMock;
    private readonly Mock<ILocalEmbeddingService> _localServiceMock;

    public LocalEmbeddingTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<AzureOpenAIEmbeddingSkillExecutor>>();
        _localServiceMock = new Mock<ILocalEmbeddingService>();
    }

    // ── local:// URI detection tests ──

    [Fact]
    public async Task ExecuteAsync_LocalUri_ShouldDelegateToLocalService()
    {
        // Arrange
        _localServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(
                "all-MiniLM-L6-v2",
                It.IsAny<Skill>(),
                It.IsAny<EnrichedDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Succeeded());

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = CreateSkillWithUri("local://all-MiniLM-L6-v2");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        _localServiceMock.Verify(s => s.GenerateEmbeddingAsync(
            "all-MiniLM-L6-v2",
            It.IsAny<Skill>(),
            It.IsAny<EnrichedDocument>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LocalUriCaseInsensitive_ShouldDelegateToLocalService()
    {
        // Arrange
        _localServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<Skill>(),
                It.IsAny<EnrichedDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Succeeded());

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = CreateSkillWithUri("LOCAL://some-model");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        _localServiceMock.Verify(s => s.GenerateEmbeddingAsync(
            "some-model",
            It.IsAny<Skill>(),
            It.IsAny<EnrichedDocument>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HttpsUri_ShouldNotDelegateToLocalService()
    {
        // Arrange: set up real HTTP path (will fail because no mock handler, but it shouldn't call local)
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    data = new[] { new { embedding = new float[] { 0.1f, 0.2f, 0.3f }, index = 0 } },
                    model = "test"
                }))
            });

        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("AzureOpenAI")).Returns(client);

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = CreateSkillWithUri("https://test.openai.azure.com");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        _localServiceMock.Verify(s => s.GenerateEmbeddingAsync(
            It.IsAny<string>(),
            It.IsAny<Skill>(),
            It.IsAny<EnrichedDocument>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_LocalUri_NoLocalService_ShouldFail()
    {
        // Arrange: no local service injected (null)
        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, localEmbeddingService: null);

        var skill = CreateSkillWithUri("local://all-MiniLM-L6-v2");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("not available"));
    }

    [Fact]
    public async Task ExecuteAsync_LocalUri_ServiceReturnsError_ShouldPropagateError()
    {
        // Arrange
        _localServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<Skill>(),
                It.IsAny<EnrichedDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Failed("Model not found"));

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = CreateSkillWithUri("local://nonexistent-model");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Model not found"));
    }

    // ── LocalEmbeddingSettings tests ──

    [Fact]
    public void LocalEmbeddingSettings_DefaultValues_ShouldBeCorrect()
    {
        var settings = new LocalEmbeddingSettings();

        Assert.Equal("./data/models", settings.ModelsDirectory);
        Assert.Equal("all-MiniLM-L6-v2", settings.DefaultModel);
        Assert.Equal(512, settings.MaximumTokens);
        Assert.True(settings.NormalizeEmbeddings);
        Assert.Equal("Mean", settings.PoolingMode);
        Assert.False(settings.AutoDownloadModels);
        Assert.False(settings.CaseSensitive);
    }

    [Fact]
    public void LocalEmbeddingSettings_SectionName_ShouldMatch()
    {
        Assert.Equal("LocalEmbeddingSettings", LocalEmbeddingSettings.SectionName);
    }

    // ── LocalOnnxEmbeddingService tests (without actual model files) ──

    [Fact]
    public void IsModelAvailable_NonexistentDirectory_ShouldReturnFalse()
    {
        var settings = Options.Create(new LocalEmbeddingSettings
        {
            ModelsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        });
        var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
        using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

        Assert.False(service.IsModelAvailable("nonexistent-model"));
    }

    [Fact]
    public void ListAvailableModels_EmptyDirectory_ShouldReturnEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            var models = service.ListAvailableModels();
            Assert.Empty(models);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ListAvailableModels_NonexistentDirectory_ShouldReturnEmpty()
    {
        var settings = Options.Create(new LocalEmbeddingSettings
        {
            ModelsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        });
        var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
        using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

        var models = service.ListAvailableModels();
        Assert.Empty(models);
    }

    [Fact]
    public void IsModelAvailable_WithBothFiles_ShouldReturnTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modelDir = Path.Combine(tempDir, "test-model");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "fake");
        File.WriteAllText(Path.Combine(modelDir, "vocab.txt"), "fake");

        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            Assert.True(service.IsModelAvailable("test-model"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelAvailable_MissingVocab_ShouldReturnFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var modelDir = Path.Combine(tempDir, "test-model");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "model.onnx"), "fake");
        // No vocab.txt

        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            Assert.False(service.IsModelAvailable("test-model"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ListAvailableModels_WithValidAndInvalidModels_ShouldFilterCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Valid model
        var validDir = Path.Combine(tempDir, "valid-model");
        Directory.CreateDirectory(validDir);
        File.WriteAllText(Path.Combine(validDir, "model.onnx"), "fake");
        File.WriteAllText(Path.Combine(validDir, "vocab.txt"), "fake");

        // Invalid model (missing vocab)
        var invalidDir = Path.Combine(tempDir, "invalid-model");
        Directory.CreateDirectory(invalidDir);
        File.WriteAllText(Path.Combine(invalidDir, "model.onnx"), "fake");

        // Empty directory
        Directory.CreateDirectory(Path.Combine(tempDir, "empty-dir"));

        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            var models = service.ListAvailableModels();
            Assert.Single(models);
            Assert.Equal("valid-model", models[0]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ModelNotAvailable_ShouldReturnHelpfulError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            var skill = CreateSkillWithUri("local://nonexistent-model");
            var document = new EnrichedDocument(new Dictionary<string, object?>
            {
                { "content", "Test text" }
            });

            var result = await service.GenerateEmbeddingAsync("nonexistent-model", skill, document);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("not found"));
            Assert.Contains(result.Errors, e => e.Contains("Download-EmbeddingModel"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyTextInput_ShouldWarn()
    {
        // Arrange: mock the local service to verify it handles empty text properly
        _localServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<Skill>(),
                It.IsAny<EnrichedDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.SucceededWithWarnings("Empty text input"));

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = CreateSkillWithUri("local://all-MiniLM-L6-v2");
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "" }
        });

        // Act
        var result = await executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void GenerateEmbedding_ModelNotLoaded_ShouldThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var settings = Options.Create(new LocalEmbeddingSettings
            {
                ModelsDirectory = tempDir
            });
            var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
            using var service = new LocalOnnxEmbeddingService(settings, logger.Object);

            Assert.Throws<FileNotFoundException>(() => service.GenerateEmbedding("nonexistent", "hello"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var settings = Options.Create(new LocalEmbeddingSettings
        {
            ModelsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
        });
        var logger = new Mock<ILogger<LocalOnnxEmbeddingService>>();
        var service = new LocalOnnxEmbeddingService(settings, logger.Object);

        // Should not throw
        service.Dispose();
        service.Dispose(); // Double dispose should be safe
    }

    // ── Existing skill behavior preserved ──

    [Fact]
    public async Task ExecuteAsync_MissingResourceUri_StillFails()
    {
        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = null,
            DeploymentId = "embedding-model"
        };
        var document = new EnrichedDocument();

        var result = await executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("resourceUri"));
    }

    [Fact]
    public async Task ExecuteAsync_LocalUri_MissingDeploymentId_StillSucceeds()
    {
        // In local mode, deploymentId is not required
        _localServiceMock
            .Setup(s => s.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<Skill>(),
                It.IsAny<EnrichedDocument>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SkillExecutionResult.Succeeded());

        var executor = new AzureOpenAIEmbeddingSkillExecutor(
            _httpClientFactoryMock.Object, _loggerMock.Object, _localServiceMock.Object);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "local://all-MiniLM-L6-v2",
            DeploymentId = null, // Not required for local mode
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new() { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "embedding", TargetName = "contentVector" }
            }
        };
        var document = new EnrichedDocument(new Dictionary<string, object?>
        {
            { "content", "Test text" }
        });

        var result = await executor.ExecuteAsync(skill, document);

        // Should succeed - local mode doesn't need deploymentId
        Assert.True(result.Success);
    }

    // ── Helper methods ──

    private static Skill CreateSkillWithUri(string resourceUri)
    {
        return new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = resourceUri,
            DeploymentId = "ignored-in-local-mode",
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new() { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "embedding", TargetName = "contentVector" }
            }
        };
    }
}
