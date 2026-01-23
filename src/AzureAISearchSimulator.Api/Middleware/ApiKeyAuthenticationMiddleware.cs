using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AzureAISearchSimulator.Api.Middleware;

/// <summary>
/// Middleware for API key authentication compatible with Azure AI Search.
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private const string ApiKeyHeaderName = "api-key";

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptionsSnapshot<SimulatorSettings> settings)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Skip authentication for health checks and swagger
        if (path.StartsWith("/health") || 
            path.StartsWith("/swagger") || 
            path.StartsWith("/favicon"))
        {
            await _next(context);
            return;
        }

        // Check for api-key header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            _logger.LogWarning("Request missing api-key header from {RemoteIp}", 
                context.Connection.RemoteIpAddress);
            await WriteUnauthorizedResponse(context, "Missing api-key header");
            return;
        }

        var apiKey = apiKeyHeader.ToString();
        var simulatorSettings = settings.Value;

        // Determine required access level based on HTTP method and path
        var requiresAdminKey = RequiresAdminKey(context.Request.Method, path);

        if (requiresAdminKey)
        {
            if (apiKey != simulatorSettings.AdminApiKey)
            {
                _logger.LogWarning("Invalid admin key attempt from {RemoteIp}", 
                    context.Connection.RemoteIpAddress);
                await WriteForbiddenResponse(context, "Invalid admin key");
                return;
            }
            context.Items["ApiKeyType"] = "Admin";
        }
        else
        {
            // Query operations accept either admin or query key
            if (apiKey != simulatorSettings.AdminApiKey && 
                apiKey != simulatorSettings.QueryApiKey)
            {
                _logger.LogWarning("Invalid API key attempt from {RemoteIp}", 
                    context.Connection.RemoteIpAddress);
                await WriteForbiddenResponse(context, "Invalid API key");
                return;
            }
            context.Items["ApiKeyType"] = apiKey == simulatorSettings.AdminApiKey ? "Admin" : "Query";
        }

        _logger.LogDebug("Authenticated request with {KeyType} key", context.Items["ApiKeyType"]);
        await _next(context);
    }

    private static bool RequiresAdminKey(string method, string path)
    {
        // GET requests to search/docs endpoints only need query key
        if (method == "GET")
        {
            if (path.Contains("/docs/search") || 
                path.Contains("/docs/suggest") || 
                path.Contains("/docs/autocomplete") ||
                path.Contains("/docs/$count") ||
                (path.Contains("/docs/") && !path.Contains("/docs/index")))
            {
                return false;
            }
            // GET /indexes/{name}/docs also allowed with query key (list docs via search)
            if (path.EndsWith("/docs") || System.Text.RegularExpressions.Regex.IsMatch(path, @"/indexes/[^/]+/docs$"))
            {
                return false;
            }
        }

        // POST to search endpoints only needs query key
        if (method == "POST")
        {
            if (path.Contains("/docs/search") || 
                path.Contains("/docs/suggest") || 
                path.Contains("/docs/autocomplete"))
            {
                return false;
            }
        }

        // All other operations require admin key
        return true;
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var error = new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = "Unauthorized",
                Message = message
            }
        };

        await context.Response.WriteAsJsonAsync(error);
    }

    private static async Task WriteForbiddenResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var error = new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = "Forbidden",
                Message = message
            }
        };

        await context.Response.WriteAsJsonAsync(error);
    }
}

/// <summary>
/// Extension methods for API key authentication middleware.
/// </summary>
public static class ApiKeyAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
    }
}
