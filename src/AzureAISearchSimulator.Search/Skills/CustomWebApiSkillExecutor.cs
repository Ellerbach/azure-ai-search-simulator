using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services.Credentials;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// CustomWebApiSkill - Calls an external web API for processing.
/// Supports authentication via authResourceId and authIdentity properties.
/// </summary>
public class CustomWebApiSkillExecutor : ISkillExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialFactory? _credentialFactory;
    private readonly ILogger<CustomWebApiSkillExecutor> _logger;

    public string ODataType => "#Microsoft.Skills.Custom.WebApiSkill";

    public CustomWebApiSkillExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<CustomWebApiSkillExecutor> logger,
        ICredentialFactory? credentialFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _credentialFactory = credentialFactory;
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

            // Add authentication if authResourceId is specified
            if (!string.IsNullOrEmpty(skill.AuthResourceId) && _credentialFactory != null)
            {
                try
                {
                    var token = await AcquireTokenAsync(skill.AuthResourceId, skill.AuthIdentity, cancellationToken);
                    if (!string.IsNullOrEmpty(token))
                    {
                        client.DefaultRequestHeaders.Authorization = 
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        _logger.LogDebug("Added Bearer token for authResourceId: {ResourceId}", skill.AuthResourceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to acquire token for authResourceId: {ResourceId}", skill.AuthResourceId);
                    warnings.Add($"Failed to acquire authentication token: {ex.Message}");
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
                    var resultValue = responseData.Values[0];

                    // Collect warnings and errors from response first
                    if (resultValue.Warnings != null)
                    {
                        warnings.AddRange(resultValue.Warnings.Select(w => w.Message ?? string.Empty));
                    }
                    if (resultValue.Errors != null && resultValue.Errors.Count > 0)
                    {
                        return SkillExecutionResult.Failed(resultValue.Errors.Select(e => e.Message ?? string.Empty).ToArray());
                    }

                    // Map outputs only if Data is available
                    var resultData = resultValue.Data;
                    if (resultData != null)
                    {
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

    /// <summary>
    /// Acquires a token for the specified resource using the credential factory.
    /// </summary>
    private async Task<string?> AcquireTokenAsync(
        string authResourceId, 
        ResourceIdentity? authIdentity, 
        CancellationToken cancellationToken)
    {
        if (_credentialFactory == null)
        {
            _logger.LogWarning("CredentialFactory not available, cannot acquire token");
            return null;
        }

        // Convert authResourceId to a scope
        var scope = NormalizeToScope(authResourceId);
        
        // Convert ResourceIdentity to SearchIdentity
        SearchIdentity? searchIdentity = null;
        if (authIdentity != null)
        {
            searchIdentity = new SearchIdentity
            {
                ODataType = authIdentity.ODataType,
                UserAssignedIdentity = authIdentity.UserAssignedIdentity
            };
        }

        var token = await _credentialFactory.GetTokenAsync(scope, searchIdentity, cancellationToken);
        return token.Token;
    }

    /// <summary>
    /// Converts an Azure resource ID or URI to a scope for token acquisition.
    /// </summary>
    private static string NormalizeToScope(string resourceId)
    {
        // If it already ends with /.default, return as-is
        if (resourceId.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
        {
            return resourceId;
        }

        // If it's a URI without /.default, add it
        if (Uri.TryCreate(resourceId, UriKind.Absolute, out var uri))
        {
            var baseUri = $"{uri.Scheme}://{uri.Host}";
            return $"{baseUri}/.default";
        }

        // If it's an Azure resource ID, convert to scope
        // Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}
        if (resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            // For Cognitive Services, use the cognitive services scope
            if (resourceId.Contains("/Microsoft.CognitiveServices/", StringComparison.OrdinalIgnoreCase))
            {
                return "https://cognitiveservices.azure.com/.default";
            }
            
            // For other resources, try to infer the scope
            // This is a simplified implementation
            return "https://management.azure.com/.default";
        }

        // Otherwise, assume it's a scope and add /.default
        return $"{resourceId.TrimEnd('/')}/.default";
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
    public List<CustomSkillMessage>? Warnings { get; set; }
    public List<CustomSkillMessage>? Errors { get; set; }
}

internal class CustomSkillMessage
{
    public string? Message { get; set; }
}
