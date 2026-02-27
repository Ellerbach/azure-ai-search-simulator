using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for CustomWebApiSkillExecutor, focusing on correct deserialization
/// of the Azure AI Search custom skill response format.
/// </summary>
public class CustomWebApiSkillExecutorTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<CustomWebApiSkillExecutor>> _loggerMock;
    private readonly CustomWebApiSkillExecutor _executor;

    public CustomWebApiSkillExecutorTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<CustomWebApiSkillExecutor>>();
        _executor = new CustomWebApiSkillExecutor(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void ODataType_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Skills.Custom.WebApiSkill", _executor.ODataType);
    }

    [Fact]
    public async Task ExecuteAsync_MissingUri_ShouldFail()
    {
        var skill = CreateSkill(uri: null);
        var document = new EnrichedDocument();

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("uri"));
    }

    [Fact]
    public async Task ExecuteAsync_WithObjectErrors_ShouldDeserializeCorrectly()
    {
        // Arrange - response with errors as objects (the real Azure format)
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object>(),
                    errors = new[] { new { message = "Something went wrong" } },
                    warnings = (object[]?)null
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Something went wrong"));
    }

    [Fact]
    public async Task ExecuteAsync_WithObjectWarnings_ShouldDeserializeCorrectly()
    {
        // Arrange - response with warnings as objects (the real Azure format)
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object> { ["clean_content"] = "cleaned" },
                    errors = Array.Empty<object>(),
                    warnings = new[] { new { message = "Field was truncated" } }
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "clean_content", TargetName = "clean_content" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("Field was truncated"));
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleErrors_ShouldDeserializeAll()
    {
        // Arrange
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object>(),
                    errors = new[]
                    {
                        new { message = "Error one" },
                        new { message = "Error two" }
                    },
                    warnings = Array.Empty<object>()
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Error one"));
        Assert.Contains(result.Errors, e => e.Contains("Error two"));
    }

    [Fact]
    public async Task ExecuteAsync_WithSuccessfulResponse_ShouldMapOutputs()
    {
        // Arrange
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object>
                    {
                        ["perm_id"] = 12345,
                        ["title"] = "Test Document"
                    },
                    errors = Array.Empty<object>(),
                    warnings = Array.Empty<object>()
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "perm_id", TargetName = "perm_id" },
                new() { Name = "title", TargetName = "title" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Warnings.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoErrorsNoWarnings_ShouldSucceed()
    {
        // Arrange - response with null errors and warnings
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object> { ["output"] = "value" }
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "output" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorsAndNullData_ShouldReturnErrorsWithoutCrashing()
    {
        // Arrange - API returns errors with null data (no outputs)
        var responseJson = """
        {
            "values": [
                {
                    "recordId": "1",
                    "data": null,
                    "errors": [{ "message": "Processing failed" }],
                    "warnings": []
                }
            ]
        }
        """;

        SetupHttpClient(responseJson);

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "output" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - should fail with the error, not throw NullReferenceException
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Processing failed"));
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorsAndMissingData_ShouldReturnErrorsWithoutCrashing()
    {
        // Arrange - API returns errors with data property omitted entirely
        var responseJson = """
        {
            "values": [
                {
                    "recordId": "1",
                    "errors": [{ "message": "Input validation failed" }]
                }
            ]
        }
        """;

        SetupHttpClient(responseJson);

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Input validation failed"));
    }

    [Fact]
    public async Task ExecuteAsync_WithWarningsAndNullData_ShouldSucceedWithWarnings()
    {
        // Arrange - API returns warnings with null data
        var responseJson = """
        {
            "values": [
                {
                    "recordId": "1",
                    "data": null,
                    "errors": [],
                    "warnings": [{ "message": "Partial result" }]
                }
            ]
        }
        """;

        SetupHttpClient(responseJson);

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "output" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - should succeed with warnings, not crash on null data
        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("Partial result"));
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyDataDictionary_ShouldSucceedWithoutMappingOutputs()
    {
        // Arrange - API returns an empty data dictionary (no matching output keys)
        var response = new
        {
            values = new[]
            {
                new
                {
                    recordId = "1",
                    data = new Dictionary<string, object>(),
                    errors = Array.Empty<object>(),
                    warnings = Array.Empty<object>()
                }
            }
        };

        SetupHttpClient(JsonSerializer.Serialize(response));

        var skill = CreateSkill(
            outputs: new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "output" }
            });
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - should succeed, output just won't be mapped
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithErrorsAndWarningsCombined_ShouldReturnErrors()
    {
        // Arrange - API returns both errors and warnings with null data
        var responseJson = """
        {
            "values": [
                {
                    "recordId": "1",
                    "data": null,
                    "errors": [{ "message": "Fatal error occurred" }],
                    "warnings": [{ "message": "Something was off" }]
                }
            ]
        }
        """;

        SetupHttpClient(responseJson);

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/content", "test content");

        // Act
        var result = await _executor.ExecuteAsync(skill, document);

        // Assert - errors take priority, result should be failed
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Fatal error occurred"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private Skill CreateSkill(
        string? uri = "http://localhost:8000/v1/test-skill",
        List<SkillOutput>? outputs = null)
    {
        return new Skill
        {
            ODataType = "#Microsoft.Skills.Custom.WebApiSkill",
            Name = "test-skill",
            Context = "/document",
            Uri = uri,
            HttpMethod = "POST",
            Inputs = new List<SkillInput>
            {
                new() { Name = "content", Source = "/document/content" }
            },
            Outputs = outputs ?? new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "output" }
            }
        };
    }

    private void SetupHttpClient(string responseJson)
    {
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(mockHandler.Object);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
    }
}
