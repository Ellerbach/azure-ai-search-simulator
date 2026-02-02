using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;

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

        // Check if this token is intended for the simulated handler
        // by examining the issuer claim before full validation
        if (!IsSimulatedToken(token, settings.Simulated.Issuer))
        {
            // Not a simulated token - let other handlers (EntraId) try
            _logger.LogDebug("Token is not a simulated token, passing to next handler");
            return Task.FromResult(AuthenticationResult.Failure(
                "NoCredentials",
                "Token is not a simulated token.",
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

        // Combined roles: If user has ServiceContributor + any data role, they can do everything
        // This matches the Azure "full development access" pattern
        if (hasServiceContributor && (hasIndexDataContributor || hasIndexDataReader))
        {
            return AccessLevel.FullAccess;
        }

        // Service Contributor alone can manage indexes, indexers, etc. but NOT query data
        if (hasServiceContributor)
        {
            return AccessLevel.ServiceContributor;
        }

        // Index Data Contributor can upload documents AND query
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

    /// <summary>
    /// Checks if a token is a simulated token by examining its issuer claim.
    /// This allows real Entra ID tokens to pass through to the EntraId handler.
    /// Returns true for tokens that can't be parsed (let the token service validate them).
    /// </summary>
    private bool IsSimulatedToken(string token, string expectedIssuer)
    {
        try
        {
            // Parse the JWT without validation to check the issuer
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                // Can't parse as JWT - could be an invalid token or test data
                // Let it through so the token service can validate/reject it
                return true;
            }

            var jwtToken = handler.ReadJwtToken(token);
            var issuer = jwtToken.Issuer;

            // Check if the issuer matches our simulated issuer
            if (string.IsNullOrEmpty(issuer))
            {
                // No issuer claim - could be a test token, let it through
                return true;
            }

            // Match against the configured simulated issuer
            var isSimulated = issuer.Equals(expectedIssuer, StringComparison.OrdinalIgnoreCase);
            
            if (!isSimulated)
            {
                _logger.LogDebug("Token issuer '{Issuer}' does not match simulated issuer '{ExpectedIssuer}', passing to next handler", 
                    issuer, expectedIssuer);
            }

            return isSimulated;
        }
        catch (Exception ex)
        {
            // Parse error - could be malformed or test data, let token service decide
            _logger.LogDebug(ex, "Failed to parse token to check issuer, attempting validation anyway");
            return true;
        }
    }
}
