using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// CustomWebApiSkill - Calls an external web API for processing.
/// </summary>
public class CustomWebApiSkillExecutor : ISkillExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CustomWebApiSkillExecutor> _logger;

    public string ODataType => "#Microsoft.Skills.Custom.WebApiSkill";

    public CustomWebApiSkillExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<CustomWebApiSkillExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SkillExecutionResult> ExecuteAsync(
        Skill skill, 
        EnrichedDocument document, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(skill.Uri))
        {
            return SkillExecutionResult.Failed("CustomWebApiSkill requires 'uri' property");
        }

        try
        {
            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();
            var warnings = new List<string>();

            var client = _httpClientFactory.CreateClient();
            
            // Set timeout
            if (!string.IsNullOrEmpty(skill.Timeout) && TimeSpan.TryParse(skill.Timeout, out var timeout))
            {
                client.Timeout = timeout;
            }
            else
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }

            // Add custom headers
            if (skill.HttpHeaders != null)
            {
                foreach (var header in skill.HttpHeaders)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            foreach (var ctx in contexts)
            {
                // Build request body
                var requestValues = new Dictionary<string, object?>();
                
                foreach (var input in skill.Inputs)
                {
                    if (input.Source != null)
                    {
                        var sourcePath = ResolveSourcePath(ctx, input.Source);
                        requestValues[input.Name] = document.GetValue(sourcePath);
                    }
                }

                // Azure AI Search custom skill request format
                var request = new
                {
                    values = new[]
                    {
                        new
                        {
                            recordId = Guid.NewGuid().ToString(),
                            data = requestValues
                        }
                    }
                };

                _logger.LogDebug("Calling custom skill at {Uri}", skill.Uri);

                HttpResponseMessage response;
                var method = skill.HttpMethod?.ToUpperInvariant() ?? "POST";

                if (method == "GET")
                {
                    response = await client.GetAsync(skill.Uri, cancellationToken);
                }
                else
                {
                    response = await client.PostAsJsonAsync(skill.Uri, request, cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    warnings.Add($"Custom skill returned {response.StatusCode}: {errorBody}");
                    continue;
                }

                // Parse response
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseData = JsonSerializer.Deserialize<CustomSkillResponse>(responseBody, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (responseData?.Values?.Count > 0)
                {
                    var resultData = responseData.Values[0].Data;
                    
                    // Map outputs
                    foreach (var output in skill.Outputs)
                    {
                        if (resultData.TryGetValue(output.Name, out var value))
                        {
                            var targetName = output.TargetName ?? output.Name;
                            var outputPath = $"{ctx}/{targetName}";
                            
                            // Handle JsonElement values
                            if (value is JsonElement element)
                            {
                                value = ConvertJsonElement(element);
                            }
                            
                            document.SetValue(outputPath, value);
                        }
                    }

                    // Collect warnings and errors from response
                    if (responseData.Values[0].Warnings != null)
                    {
                        warnings.AddRange(responseData.Values[0].Warnings);
                    }
                    if (responseData.Values[0].Errors != null && responseData.Values[0].Errors.Count > 0)
                    {
                        return SkillExecutionResult.Failed(responseData.Values[0].Errors.ToArray());
                    }
                }
            }

            return warnings.Count > 0 
                ? SkillExecutionResult.SucceededWithWarnings(warnings.ToArray())
                : SkillExecutionResult.Succeeded();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling custom skill");
            return SkillExecutionResult.Failed($"HTTP error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return SkillExecutionResult.Failed("Custom skill request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing custom skill");
            return SkillExecutionResult.Failed($"CustomWebApiSkill error: {ex.Message}");
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => ConvertJsonElement(e))
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
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
/// Response from a custom web API skill.
/// </summary>
internal class CustomSkillResponse
{
    public List<CustomSkillResponseValue> Values { get; set; } = new();
}

internal class CustomSkillResponseValue
{
    public string? RecordId { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public List<string>? Warnings { get; set; }
    public List<string>? Errors { get; set; }
}
