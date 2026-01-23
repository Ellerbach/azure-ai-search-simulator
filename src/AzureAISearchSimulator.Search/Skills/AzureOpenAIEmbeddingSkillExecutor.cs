using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// AzureOpenAIEmbeddingSkill - Generates vector embeddings using Azure OpenAI.
/// This skill calls the Azure OpenAI embeddings API to convert text into vectors.
/// </summary>
public class AzureOpenAIEmbeddingSkillExecutor : ISkillExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureOpenAIEmbeddingSkillExecutor> _logger;

    public string ODataType => "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill";

    public AzureOpenAIEmbeddingSkillExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<AzureOpenAIEmbeddingSkillExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SkillExecutionResult> ExecuteAsync(
        Skill skill, 
        EnrichedDocument document, 
        CancellationToken cancellationToken = default)
    {
        // Validate required configuration
        if (string.IsNullOrEmpty(skill.ResourceUri))
        {
            return SkillExecutionResult.Failed("AzureOpenAIEmbeddingSkill requires 'resourceUri' property");
        }

        if (string.IsNullOrEmpty(skill.DeploymentId))
        {
            return SkillExecutionResult.Failed("AzureOpenAIEmbeddingSkill requires 'deploymentId' property");
        }

        try
        {
            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();
            var warnings = new List<string>();

            // Get the text input configuration
            var textInput = skill.Inputs.FirstOrDefault(i => i.Name == "text");
            if (textInput?.Source == null)
            {
                return SkillExecutionResult.Failed("AzureOpenAIEmbeddingSkill requires 'text' input");
            }

            var client = _httpClientFactory.CreateClient("AzureOpenAI");
            client.Timeout = TimeSpan.FromSeconds(60);

            foreach (var ctx in contexts)
            {
                // Get input text
                var sourcePath = ResolveSourcePath(ctx, textInput.Source);
                var text = document.GetValue<string>(sourcePath);

                if (string.IsNullOrEmpty(text))
                {
                    warnings.Add($"Empty text input at {sourcePath}, skipping embedding generation");
                    continue;
                }

                // Truncate text if too long (typical max is 8191 tokens for text-embedding-ada-002)
                // Using a simple character-based approximation
                const int maxChars = 30000; // ~8000 tokens
                if (text.Length > maxChars)
                {
                    text = text[..maxChars];
                    warnings.Add($"Text truncated to {maxChars} characters for embedding generation");
                }

                // Build the embeddings API request
                var apiUrl = BuildApiUrl(skill.ResourceUri, skill.DeploymentId);
                var requestBody = new
                {
                    input = text,
                    model = skill.ModelName ?? "text-embedding-ada-002",
                    dimensions = skill.Dimensions
                };

                _logger.LogDebug("Calling Azure OpenAI embeddings API at {Url}", apiUrl);

                // Note: The API key should be configured in the HTTP client factory
                // or passed through the skill configuration
                var response = await client.PostAsJsonAsync(apiUrl, requestBody, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Azure OpenAI API returned {StatusCode}: {Error}", 
                        response.StatusCode, errorBody);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        warnings.Add("Rate limited by Azure OpenAI, consider reducing batch size");
                    }
                    else
                    {
                        return SkillExecutionResult.Failed($"Azure OpenAI API error: {response.StatusCode}");
                    }
                    continue;
                }

                // Parse the embedding response
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (embeddingResponse?.Data?.Count > 0)
                {
                    var embedding = embeddingResponse.Data[0].Embedding;

                    // Set the output
                    var embeddingOutput = skill.Outputs.FirstOrDefault(o => o.Name == "embedding");
                    var targetName = embeddingOutput?.TargetName ?? "embedding";
                    var outputPath = $"{ctx}/{targetName}";

                    document.SetValue(outputPath, embedding);

                    _logger.LogDebug("Generated embedding with {Dimensions} dimensions", embedding?.Length ?? 0);
                }
                else
                {
                    warnings.Add("No embedding returned from Azure OpenAI API");
                }
            }

            return warnings.Count > 0
                ? SkillExecutionResult.SucceededWithWarnings(warnings.ToArray())
                : SkillExecutionResult.Succeeded();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Azure OpenAI");
            return SkillExecutionResult.Failed($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return SkillExecutionResult.Failed("Azure OpenAI request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Azure OpenAI embedding skill");
            return SkillExecutionResult.Failed($"AzureOpenAIEmbeddingSkill error: {ex.Message}");
        }
    }

    private static string BuildApiUrl(string resourceUri, string deploymentId)
    {
        // Format: https://{resource-name}.openai.azure.com/openai/deployments/{deployment-id}/embeddings?api-version=2024-02-01
        var baseUri = resourceUri.TrimEnd('/');
        return $"{baseUri}/openai/deployments/{deploymentId}/embeddings?api-version=2024-02-01";
    }

    private static string ResolveSourcePath(string context, string source)
    {
        if (source.StartsWith("/"))
        {
            return source;
        }
        return $"{context}/{source}";
    }
}

/// <summary>
/// Response from Azure OpenAI embeddings API.
/// </summary>
internal class EmbeddingResponse
{
    public string? Object { get; set; }
    public List<EmbeddingData> Data { get; set; } = new();
    public string? Model { get; set; }
    public EmbeddingUsage? Usage { get; set; }
}

internal class EmbeddingData
{
    public string? Object { get; set; }
    public int Index { get; set; }
    public float[]? Embedding { get; set; }
}

internal class EmbeddingUsage
{
    public int PromptTokens { get; set; }
    public int TotalTokens { get; set; }
}
