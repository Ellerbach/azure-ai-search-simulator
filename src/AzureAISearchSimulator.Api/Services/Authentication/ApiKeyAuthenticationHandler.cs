using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Services.Authentication;

/// <summary>
/// Authentication handler for API key-based authentication.
/// Validates requests using the api-key header, compatible with Azure AI Search.
/// </summary>
public class ApiKeyAuthenticationHandler : IAuthenticationHandler
{
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly IOptionsMonitor<SimulatorSettings> _simulatorSettings;
    
    private const string ApiKeyHeaderName = "api-key";
    private const string ApiKeyQueryParameter = "api-key";

    public ApiKeyAuthenticationHandler(
        ILogger<ApiKeyAuthenticationHandler> logger,
        IOptionsMonitor<AuthenticationSettings> authSettings,
        IOptionsMonitor<SimulatorSettings> simulatorSettings)
    {
        _logger = logger;
        _authSettings = authSettings;
        _simulatorSettings = simulatorSettings;
    }

    /// <inheritdoc />
    public string AuthenticationMode => "ApiKey";

    /// <inheritdoc />
    public int Priority => 0; // Highest priority to match Azure behavior

    /// <inheritdoc />
    public bool CanHandle(HttpContext context)
    {
        // Check for api-key header
        if (context.Request.Headers.ContainsKey(ApiKeyHeaderName))
        {
            return true;
        }

        // Also check query parameter for compatibility
        if (context.Request.Query.ContainsKey(ApiKeyQueryParameter))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        // Get the API key from header or query parameter
        string? apiKey = null;

        if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerValue))
        {
            apiKey = headerValue.ToString();
        }
        else if (context.Request.Query.TryGetValue(ApiKeyQueryParameter, out var queryValue))
        {
            apiKey = queryValue.ToString();
            _logger.LogDebug("API key provided via query parameter");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticationResult.Failure(
                "MissingApiKey",
                "API key header or query parameter is empty.",
                AuthenticationMode));
        }

        // Get the configured keys (prefer new Authentication section, fall back to SimulatorSettings)
        var authSettings = _authSettings.CurrentValue;
        var simulatorSettings = _simulatorSettings.CurrentValue;

        var adminKey = authSettings.ApiKey.AdminApiKey ?? simulatorSettings.AdminApiKey;
        var queryKey = authSettings.ApiKey.QueryApiKey ?? simulatorSettings.QueryApiKey;

        // Check against admin key
        if (apiKey == adminKey)
        {
            _logger.LogDebug("Authenticated with admin API key");
            return Task.FromResult(AuthenticationResult.Success(
                AuthenticationMode,
                identityType: "ApiKey",
                identityId: "admin",
                identityName: "Admin API Key",
                accessLevel: AccessLevel.FullAccess,
                roles: new List<string> { "Owner", "Search Service Contributor", "Search Index Data Contributor", "Search Index Data Reader" }
            ));
        }

        // Check against query key
        if (apiKey == queryKey)
        {
            _logger.LogDebug("Authenticated with query API key");
            return Task.FromResult(AuthenticationResult.Success(
                AuthenticationMode,
                identityType: "ApiKey",
                identityId: "query",
                identityName: "Query API Key",
                accessLevel: AccessLevel.IndexDataReader,
                roles: new List<string> { "Search Index Data Reader" }
            ));
        }

        // Invalid API key
        _logger.LogWarning("Invalid API key attempt from {RemoteIp}", 
            context.Connection.RemoteIpAddress);

        return Task.FromResult(AuthenticationResult.Failure(
            "InvalidApiKey",
            "The provided API key is not valid.",
            AuthenticationMode));
    }
}
