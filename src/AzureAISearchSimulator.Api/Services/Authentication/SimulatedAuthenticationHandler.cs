using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Services.Authentication;

/// <summary>
/// Authentication handler for simulated JWT tokens.
/// Validates Bearer tokens signed with the configured signing key.
/// </summary>
public class SimulatedAuthenticationHandler : IAuthenticationHandler
{
    private readonly ILogger<SimulatedAuthenticationHandler> _logger;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly ISimulatedTokenService _tokenService;

    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    public SimulatedAuthenticationHandler(
        ILogger<SimulatedAuthenticationHandler> logger,
        IOptionsMonitor<AuthenticationSettings> authSettings,
        ISimulatedTokenService tokenService)
    {
        _logger = logger;
        _authSettings = authSettings;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public string AuthenticationMode => "Simulated";

    /// <inheritdoc />
    public int Priority => 10; // After API key (0) but before real Entra ID

    /// <inheritdoc />
    public bool CanHandle(HttpContext context)
    {
        var settings = _authSettings.CurrentValue;
        
        // Check if simulated auth is enabled
        if (!settings.Simulated.Enabled)
        {
            return false;
        }

        // Check for Bearer token in Authorization header
        if (!context.Request.Headers.TryGetValue(AuthorizationHeader, out var authHeader))
        {
            return false;
        }

        var headerValue = authHeader.ToString();
        return headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var settings = _authSettings.CurrentValue;

        if (!settings.Simulated.Enabled)
        {
            return Task.FromResult(AuthenticationResult.Failure(
                "SimulatedAuthDisabled",
                "Simulated authentication is not enabled.",
                AuthenticationMode));
        }

        // Extract the token
        if (!context.Request.Headers.TryGetValue(AuthorizationHeader, out var authHeader))
        {
            return Task.FromResult(AuthenticationResult.Failure(
                "MissingToken",
                "Authorization header is missing.",
                AuthenticationMode));
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticationResult.Failure(
                "InvalidScheme",
                "Authorization header must use Bearer scheme.",
                AuthenticationMode));
        }

        var token = headerValue.Substring(BearerPrefix.Length).Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticationResult.Failure(
                "MissingToken",
                "Bearer token is empty.",
                AuthenticationMode));
        }

        // Validate the token
        var validationResult = _tokenService.ValidateToken(token);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Simulated token validation failed: {Error} from {RemoteIp}", 
                validationResult.Error, context.Connection.RemoteIpAddress);

            return Task.FromResult(AuthenticationResult.Failure(
                validationResult.ErrorCode ?? "InvalidToken",
                validationResult.Error ?? "Token validation failed.",
                AuthenticationMode));
        }

        // Map roles to access level
        var accessLevel = MapRolesToAccessLevel(validationResult.Roles, settings.RoleMapping);

        _logger.LogDebug("Authenticated via simulated token: {IdentityType} {ObjectId} with access level {AccessLevel}",
            validationResult.IdentityType, validationResult.ObjectId, accessLevel);

        var result = AuthenticationResult.Success(
            AuthenticationMode,
            identityType: validationResult.IdentityType ?? "ServicePrincipal",
            identityId: validationResult.ObjectId ?? "unknown",
            identityName: validationResult.Name ?? validationResult.AppId ?? "Simulated Identity",
            accessLevel: accessLevel,
            roles: validationResult.Roles);

        result.TenantId = validationResult.TenantId;
        result.ApplicationId = validationResult.AppId;
        result.Scopes = validationResult.Scopes;

        return Task.FromResult(result);
    }

    private static AccessLevel MapRolesToAccessLevel(List<string> roles, RoleMappingSettings roleMapping)
    {
        // Check from highest to lowest privilege
        // Owner = Full Access
        if (roles.Any(r => roleMapping.OwnerRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            return AccessLevel.FullAccess;
        }

        // Contributor
        if (roles.Any(r => roleMapping.ContributorRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            return AccessLevel.Contributor;
        }

        // Check for combined permissions
        var hasServiceContributor = roles.Any(r => roleMapping.ServiceContributorRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        var hasIndexDataContributor = roles.Any(r => roleMapping.IndexDataContributorRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        var hasIndexDataReader = roles.Any(r => roleMapping.IndexDataReaderRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
        var hasReader = roles.Any(r => roleMapping.ReaderRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

        // Service Contributor can manage indexes, indexers, etc.
        if (hasServiceContributor)
        {
            return AccessLevel.ServiceContributor;
        }

        // Index Data Contributor can upload documents
        if (hasIndexDataContributor)
        {
            return AccessLevel.IndexDataContributor;
        }

        // Reader can read service info
        if (hasReader)
        {
            return AccessLevel.Reader;
        }

        // Index Data Reader can query
        if (hasIndexDataReader)
        {
            return AccessLevel.IndexDataReader;
        }

        return AccessLevel.None;
    }
}
