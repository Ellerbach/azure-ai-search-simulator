using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Middleware;

/// <summary>
/// Unified authentication middleware that supports multiple authentication modes.
/// Delegates to registered IAuthenticationHandler implementations.
/// </summary>
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly IEnumerable<IAuthenticationHandler> _handlers;

    public AuthenticationMiddleware(
        RequestDelegate next,
        ILogger<AuthenticationMiddleware> logger,
        IEnumerable<IAuthenticationHandler> handlers)
    {
        _next = next;
        _logger = logger;
        _handlers = handlers.OrderBy(h => h.Priority).ToList();
    }

    public async Task InvokeAsync(HttpContext context, IOptionsSnapshot<AuthenticationSettings> authSettings)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var settings = authSettings.Value;

        // Skip authentication for health checks, swagger, and other public endpoints
        if (ShouldSkipAuthentication(path))
        {
            await _next(context);
            return;
        }

        // Try to authenticate using enabled handlers
        AuthenticationResult? result = null;
        IAuthenticationHandler? usedHandler = null;

        // Get handlers ordered by priority, filtered by enabled modes
        var enabledHandlers = _handlers
            .Where(h => settings.EnabledModes.Contains(h.AuthenticationMode, StringComparer.OrdinalIgnoreCase))
            .OrderBy(h => h.Priority);

        // If API key takes precedence and both api-key and Bearer are present,
        // ensure API key handler runs first (matches Azure behavior)
        if (settings.ApiKeyTakesPrecedence)
        {
            var apiKeyHandler = enabledHandlers.FirstOrDefault(h => 
                h.AuthenticationMode.Equals("ApiKey", StringComparison.OrdinalIgnoreCase) &&
                h.CanHandle(context));

            if (apiKeyHandler != null)
            {
                result = await apiKeyHandler.AuthenticateAsync(context);
                usedHandler = apiKeyHandler;

                if (result.IsAuthenticated)
                {
                    SetAuthenticationContext(context, result);
                    await _next(context);
                    return;
                }
                else if (result.ErrorCode != "MissingApiKey")
                {
                    // API key was provided but invalid - don't try other handlers
                    await WriteUnauthorizedResponse(context, result);
                    return;
                }
            }
        }

        // Try each enabled handler that can handle this request
        foreach (var handler in enabledHandlers)
        {
            if (!handler.CanHandle(context))
            {
                continue;
            }

            // Skip API key handler if we already tried it with precedence
            if (settings.ApiKeyTakesPrecedence && 
                handler.AuthenticationMode.Equals("ApiKey", StringComparison.OrdinalIgnoreCase) &&
                usedHandler?.AuthenticationMode == "ApiKey")
            {
                continue;
            }

            _logger.LogDebug("Attempting authentication with {Handler}", handler.AuthenticationMode);
            result = await handler.AuthenticateAsync(context);
            usedHandler = handler;

            if (result.IsAuthenticated)
            {
                _logger.LogDebug("Authenticated successfully via {Handler} as {Identity}", 
                    handler.AuthenticationMode, result.IdentityName);
                
                SetAuthenticationContext(context, result);
                await _next(context);
                return;
            }

            // If this handler explicitly failed (not just "can't handle"), 
            // stop trying other handlers and return the error
            if (result.ErrorCode != null && result.ErrorCode != "NoCredentials")
            {
                _logger.LogDebug("Authentication failed via {Handler}: {Error}", 
                    handler.AuthenticationMode, result.ErrorMessage);
                await WriteUnauthorizedResponse(context, result);
                return;
            }
        }

        // No handler could authenticate the request
        _logger.LogWarning("No authentication credentials provided from {RemoteIp}", 
            context.Connection.RemoteIpAddress);

        await WriteUnauthorizedResponse(context, 
            result ?? AuthenticationResult.Failure(
                "Unauthorized",
                "No valid authentication credentials were provided. Include an 'api-key' header or 'Authorization: Bearer <token>' header."));
    }

    private static bool ShouldSkipAuthentication(string path)
    {
        // Public endpoints that don't require authentication
        return path.StartsWith("/health") ||
               path.StartsWith("/swagger") ||
               path.StartsWith("/scalar") ||
               path.StartsWith("/favicon") ||
               path.StartsWith("/openapi") ||
               path == "/";
    }

    private static void SetAuthenticationContext(HttpContext context, AuthenticationResult result)
    {
        // Store authentication info in HttpContext.Items for use by controllers
        context.Items["AuthResult"] = result;
        context.Items["IdentityType"] = result.IdentityType;
        context.Items["IdentityId"] = result.IdentityId;
        context.Items["IdentityName"] = result.IdentityName;
        context.Items["AccessLevel"] = result.AccessLevel;
        context.Items["AuthMode"] = result.AuthenticationMode;
        context.Items["Roles"] = result.Roles;

        // For backward compatibility with existing code
        context.Items["ApiKeyType"] = result.AccessLevel == AccessLevel.FullAccess ? "Admin" : "Query";
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, AuthenticationResult result)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var error = new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = result.ErrorCode ?? "Unauthorized",
                Message = result.ErrorMessage ?? "Authentication failed."
            }
        };

        // Add details about authentication mode if available
        if (result.AuthenticationMode != null)
        {
            error.Error.Details = new List<ODataErrorDetail>
            {
                new() { Code = "AuthMode", Message = $"Attempted: {result.AuthenticationMode}" }
            };
        }

        await context.Response.WriteAsJsonAsync(error);
    }
}

/// <summary>
/// Extension methods for unified authentication middleware.
/// </summary>
public static class AuthenticationMiddlewareExtensions
{
    /// <summary>
    /// Adds the unified authentication middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseUnifiedAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationMiddleware>();
    }
}
