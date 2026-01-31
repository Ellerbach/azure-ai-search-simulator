using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AzureAISearchSimulator.Api.Services.Authentication;

/// <summary>
/// Authentication handler for real Azure Entra ID (Azure AD) tokens.
/// Validates Bearer tokens against Azure AD metadata endpoint.
/// </summary>
public class EntraIdAuthenticationHandler : IAuthenticationHandler
{
    private readonly ILogger<EntraIdAuthenticationHandler> _logger;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly IEntraIdTokenValidator _tokenValidator;

    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    public EntraIdAuthenticationHandler(
        ILogger<EntraIdAuthenticationHandler> logger,
        IOptionsMonitor<AuthenticationSettings> authSettings,
        IEntraIdTokenValidator tokenValidator)
    {
        _logger = logger;
        _authSettings = authSettings;
        _tokenValidator = tokenValidator;
    }

    /// <inheritdoc />
    public string AuthenticationMode => "EntraId";

    /// <inheritdoc />
    public int Priority => 20; // After API key (0) and Simulated (10)

    /// <inheritdoc />
    public bool CanHandle(HttpContext context)
    {
        var settings = _authSettings.CurrentValue;

        // Check if Entra ID auth is enabled
        if (!settings.EnabledModes.Contains("EntraId", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check if tenant ID is configured
        if (string.IsNullOrEmpty(settings.EntraId.TenantId))
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
    public async Task<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var settings = _authSettings.CurrentValue;

        // Extract the token
        if (!context.Request.Headers.TryGetValue(AuthorizationHeader, out var authHeader))
        {
            return AuthenticationResult.Failure(
                "MissingToken",
                "Authorization header is missing.",
                AuthenticationMode);
        }

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationResult.Failure(
                "InvalidScheme",
                "Authorization header must use Bearer scheme.",
                AuthenticationMode);
        }

        var token = headerValue.Substring(BearerPrefix.Length).Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticationResult.Failure(
                "MissingToken",
                "Bearer token is empty.",
                AuthenticationMode);
        }

        // Validate the token
        var validationResult = await _tokenValidator.ValidateTokenAsync(token, settings.EntraId, cancellationToken);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Entra ID token validation failed: {Error} from {RemoteIp}",
                validationResult.Error, context.Connection.RemoteIpAddress);

            return AuthenticationResult.Failure(
                validationResult.ErrorCode ?? "InvalidToken",
                validationResult.Error ?? "Token validation failed.",
                AuthenticationMode);
        }

        // Get roles from token, or use default roles if none present
        var roles = validationResult.Roles;
        if (roles == null || roles.Count == 0)
        {
            // Apply default roles for development/testing
            if (settings.EntraId.DefaultRoles != null && settings.EntraId.DefaultRoles.Count > 0)
            {
                roles = settings.EntraId.DefaultRoles;
                _logger.LogDebug("No roles in token, applying default roles: {Roles}", string.Join(", ", roles));
            }
            else
            {
                roles = new List<string>();
            }
        }

        // Map roles to access level
        var accessLevel = MapRolesToAccessLevel(roles, settings.RoleMapping);

        _logger.LogDebug("Authenticated via Entra ID: {IdentityType} {ObjectId} with access level {AccessLevel}",
            validationResult.IdentityType, validationResult.ObjectId, accessLevel);

        var result = AuthenticationResult.Success(
            AuthenticationMode,
            identityType: validationResult.IdentityType ?? "ServicePrincipal",
            identityId: validationResult.ObjectId ?? "unknown",
            identityName: validationResult.Name ?? validationResult.AppId ?? "Entra ID Identity",
            accessLevel: accessLevel,
            roles: roles);

        result.TenantId = validationResult.TenantId;
        result.ApplicationId = validationResult.AppId;
        result.Scopes = validationResult.Scopes;

        return result;
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

        // Check for individual roles
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
}

/// <summary>
/// Interface for Entra ID token validation service.
/// </summary>
public interface IEntraIdTokenValidator
{
    /// <summary>
    /// Validates an Entra ID token asynchronously.
    /// </summary>
    Task<EntraIdValidationResult> ValidateTokenAsync(string token, EntraIdSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of Entra ID token validation.
/// </summary>
public class EntraIdValidationResult
{
    /// <summary>
    /// Whether the token is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Error code if validation failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// The validated claims principal.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Object ID (oid claim).
    /// </summary>
    public string? ObjectId { get; set; }

    /// <summary>
    /// Tenant ID (tid claim).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Application ID (appid or azp claim).
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Identity type (idtyp claim or derived).
    /// </summary>
    public string? IdentityType { get; set; }

    /// <summary>
    /// Name or preferred username claim.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// List of roles from the roles claim.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// List of scopes from the scp claim.
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static EntraIdValidationResult Success(ClaimsPrincipal principal)
    {
        return new EntraIdValidationResult { IsValid = true, Principal = principal };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static EntraIdValidationResult Failure(string errorCode, string error)
    {
        return new EntraIdValidationResult
        {
            IsValid = false,
            ErrorCode = errorCode,
            Error = error
        };
    }
}
