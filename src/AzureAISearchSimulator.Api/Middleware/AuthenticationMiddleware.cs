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
                // Log successful authentication with identity info (without sensitive data)
                _logger.LogInformation(
                    "Authentication succeeded. Mode: {AuthMode}, Identity: {IdentityName}, AccessLevel: {AccessLevel}, Path: {Path}",
                    handler.AuthenticationMode, 
                    result.IdentityName ?? result.IdentityId ?? "unknown",
                    result.AccessLevel,
                    context.Request.Path);
                
                SetAuthenticationContext(context, result);
                await _next(context);
                return;
            }

            // If this handler explicitly failed (not just "can't handle"), 
            // stop trying other handlers and return the error
            if (result.ErrorCode != null && result.ErrorCode != "NoCredentials")
            {
                _logger.LogWarning(
                    "Authentication failed. Mode: {AuthMode}, ErrorCode: {ErrorCode}, Path: {Path}, RemoteIP: {RemoteIP}",
                    handler.AuthenticationMode, 
                    result.ErrorCode,
                    context.Request.Path,
                    context.Connection.RemoteIpAddress);
                await WriteUnauthorizedResponse(context, result);
                return;
            }
        }

        // No handler could authenticate the request
        _logger.LogWarning(
            "No authentication credentials provided. Path: {Path}, RemoteIP: {RemoteIP}", 
            context.Request.Path,
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

        // Build helpful error message with troubleshooting hints
        var message = result.ErrorMessage ?? "Authentication failed.";
        var hints = GetTroubleshootingHints(result.ErrorCode);
        if (!string.IsNullOrEmpty(hints))
        {
            message = $"{message} {hints}";
        }

        var error = new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = result.ErrorCode ?? "Unauthorized",
                Message = message
            }
        };

        // Add details about authentication mode and configuration
        var details = new List<ODataErrorDetail>();
        
        if (result.AuthenticationMode != null)
        {
            details.Add(new ODataErrorDetail 
            { 
                Code = "AuthMode", 
                Message = $"Authentication method attempted: {result.AuthenticationMode}" 
            });
        }

        // Add specific guidance based on error type
        if (result.ErrorCode == "InvalidApiKey")
        {
            details.Add(new ODataErrorDetail
            {
                Code = "Hint",
                Message = "Verify the api-key header value matches the configured AdminApiKey or QueryApiKey."
            });
        }
        else if (result.ErrorCode == "TokenExpired")
        {
            details.Add(new ODataErrorDetail
            {
                Code = "Hint",
                Message = "Request a new token from /admin/token/quick/{role} or refresh your Entra ID token."
            });
        }
        else if (result.ErrorCode == "InvalidToken" || result.ErrorCode == "InvalidSignature")
        {
            details.Add(new ODataErrorDetail
            {
                Code = "Hint",
                Message = "For simulated tokens, ensure the signing key matches. For Entra ID, verify tenant and audience configuration."
            });
        }
        else if (result.ErrorCode == "MissingApiKey" || result.ErrorCode == "NoCredentials")
        {
            details.Add(new ODataErrorDetail
            {
                Code = "Hint",
                Message = "Provide authentication via 'api-key' header or 'Authorization: Bearer <token>' header."
            });
        }

        if (details.Count > 0)
        {
            error.Error.Details = details;
        }

        await context.Response.WriteAsJsonAsync(error);
    }

    private static string? GetTroubleshootingHints(string? errorCode)
    {
        return errorCode switch
        {
            "InvalidApiKey" => "Check that you are using the correct admin or query key.",
            "TokenExpired" => "Your authentication token has expired.",
            "InvalidAudience" => "Token audience does not match expected value (https://search.azure.com).",
            "InvalidIssuer" => "Token issuer is not trusted. Check Entra ID configuration.",
            "InvalidSignature" => "Token signature validation failed.",
            "MissingApiKey" => "No API key was provided in the request.",
            "NoCredentials" => "No authentication credentials were found.",
            _ => null
        };
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
