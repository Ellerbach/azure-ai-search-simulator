using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AzureAISearchSimulator.Core.Tests;

public class EmbeddingSkillTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<AzureOpenAIEmbeddingSkillExecutor>> _loggerMock;
    private readonly AzureOpenAIEmbeddingSkillExecutor _executor;

    public EmbeddingSkillTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<AzureOpenAIEmbeddingSkillExecutor>>();
        _executor = new AzureOpenAIEmbeddingSkillExecutor(_httpClientFactoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void ODataType_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill", _executor.ODataType);
    }

    [Fact]
    public async Task ExecuteAsync_MissingResourceUri_ShouldFail()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = null,
            DeploymentId = "embedding-model"
        };
        var document = new EnrichedDocument();

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("resourceUri"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingDeploymentId_ShouldFail()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = null
        };
        var document = new EnrichedDocument();

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("deploymentId"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingTextInput_ShouldFail()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = "embedding-model",
            Inputs = new List<SkillInput>() // No text input
        };
        var document = new EnrichedDocument();

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("text"));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInput_ShouldCallApi()
    {
        // Arrange
        var embeddingResponse = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    @object = "embedding",
                    index = 0,
                    embedding = new float[] { 0.1f, 0.2f, 0.3f }
                }
            },
            model = "text-embedding-ada-002",
            usage = new { prompt_tokens = 5, total_tokens = 5 }
        };

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(embeddingResponse))
            });

        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("AzureOpenAI")).Returns(client);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = "embedding-model",
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
            { "content", "This is some test content." }
        });

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        var embedding = document.GetValue<float[]>("/document/contentVector");
        Assert.NotNull(embedding);
        Assert.Equal(3, embedding!.Length);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyText_ShouldWarn()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("AzureOpenAI")).Returns(client);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = "embedding-model",
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
            { "content", "" } // Empty content
        });

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - should succeed with warnings
        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Empty text"));
    }

    [Fact]
    public async Task ExecuteAsync_ApiError_ShouldFail()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Server error")
            });

        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("AzureOpenAI")).Returns(client);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = "embedding-model",
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
            { "content", "Some text" }
        });

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("InternalServerError"));
    }

    [Fact]
    public async Task ExecuteAsync_RateLimited_ShouldWarn()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.TooManyRequests,
                Content = new StringContent("Rate limited")
            });

        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient("AzureOpenAI")).Returns(client);

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            ResourceUri = "https://test.openai.azure.com",
            DeploymentId = "embedding-model",
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
            { "content", "Some text" }
        });

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - should succeed with rate limit warning
        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Rate limited"));
    }

    [Fact]
    public void Skill_Configuration_ShouldHaveExpectedProperties()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            Name = "my-embedding-skill",
            Description = "Generate embeddings using Azure OpenAI",
            ResourceUri = "https://myopenai.openai.azure.com",
            DeploymentId = "text-embedding-ada-002",
            ModelName = "text-embedding-ada-002",
            Dimensions = 1536,
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

        // Assert
        Assert.Equal("#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill", skill.ODataType);
        Assert.Equal("my-embedding-skill", skill.Name);
        Assert.Equal("https://myopenai.openai.azure.com", skill.ResourceUri);
        Assert.Equal("text-embedding-ada-002", skill.DeploymentId);
        Assert.Equal("text-embedding-ada-002", skill.ModelName);
        Assert.Equal(1536, skill.Dimensions);
    }
}
