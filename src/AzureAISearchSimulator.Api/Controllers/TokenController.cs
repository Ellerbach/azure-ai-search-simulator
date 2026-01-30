using System.Text.Json.Serialization;
using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for generating and validating simulated tokens.
/// These endpoints are for development/testing purposes only.
/// </summary>
[ApiController]
[Route("admin/token")]
public class TokenController : ControllerBase
{
    private readonly ISimulatedTokenService _tokenService;
    private readonly ILogger<TokenController> _logger;
    private readonly IOptionsSnapshot<AuthenticationSettings> _authSettings;

    public TokenController(
        ISimulatedTokenService tokenService,
        ILogger<TokenController> logger,
        IOptionsSnapshot<AuthenticationSettings> authSettings)
    {
        _tokenService = tokenService;
        _logger = logger;
        _authSettings = authSettings;
    }

    /// <summary>
    /// Generates a simulated JWT token for testing.
    /// </summary>
    /// <remarks>
    /// This endpoint generates tokens that mimic Azure AD tokens for local development.
    /// The tokens are signed with the configured signing key and can be used with the
    /// simulator's Bearer token authentication.
    /// 
    /// Example request:
    /// ```json
    /// {
    ///   "identityType": "ServicePrincipal",
    ///   "appId": "test-app-1",
    ///   "roles": ["Search Index Data Contributor", "Search Index Data Reader"],
    ///   "expiresInMinutes": 60
    /// }
    /// ```
    /// </remarks>
    /// <param name="request">Token generation request</param>
    /// <returns>Generated token with expiration information</returns>
    [HttpPost]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status403Forbidden)]
    public IActionResult GenerateToken([FromBody] TokenGenerationRequest request)
    {
        // Check if simulated auth is enabled
        if (!_authSettings.Value.Simulated.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ODataError.Create(
                "Forbidden",
                "Simulated authentication is not enabled. Set Authentication:Simulated:Enabled to true in configuration."));
        }

        // Validate request
        if (request.Roles == null || request.Roles.Count == 0)
        {
            return BadRequest(ODataError.Create(
                "InvalidRequest",
                "At least one role must be specified."));
        }

        // Map to internal request
        var tokenRequest = new SimulatedTokenRequest
        {
            IdentityType = request.IdentityType ?? "ServicePrincipal",
            ObjectId = request.ObjectId,
            AppId = request.AppId,
            Name = request.Name,
            PreferredUsername = request.PreferredUsername,
            TenantId = request.TenantId,
            Roles = request.Roles,
            Scopes = request.Scopes ?? new List<string>(),
            ExpiresInMinutes = request.ExpiresInMinutes ?? _authSettings.Value.Simulated.TokenLifetimeMinutes
        };

        var result = _tokenService.GenerateToken(tokenRequest);

        if (!result.Success)
        {
            return BadRequest(ODataError.Create("TokenGenerationFailed", result.Error ?? "Failed to generate token."));
        }

        _logger.LogInformation("Generated simulated token for {IdentityType} with roles: {Roles}",
            request.IdentityType, string.Join(", ", request.Roles));

        return Ok(new TokenResponse
        {
            Token = result.Token!,
            ExpiresAt = result.ExpiresAt!.Value,
            TokenType = result.TokenType
        });
    }

    /// <summary>
    /// Validates a token and returns its claims.
    /// </summary>
    /// <remarks>
    /// This endpoint validates a simulated token and returns the extracted claims.
    /// Useful for debugging token issues.
    /// </remarks>
    /// <param name="request">Token to validate</param>
    /// <returns>Validation result with extracted claims</returns>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(TokenValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status403Forbidden)]
    public IActionResult ValidateToken([FromBody] TokenValidationRequest request)
    {
        // Check if simulated auth is enabled
        if (!_authSettings.Value.Simulated.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ODataError.Create(
                "Forbidden",
                "Simulated authentication is not enabled."));
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(ODataError.Create("InvalidRequest", "Token is required."));
        }

        // Remove "Bearer " prefix if present
        var token = request.Token;
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(7);
        }

        var result = _tokenService.ValidateToken(token);

        return Ok(new TokenValidationResponse
        {
            IsValid = result.IsValid,
            Error = result.Error,
            ErrorCode = result.ErrorCode,
            ObjectId = result.ObjectId,
            TenantId = result.TenantId,
            AppId = result.AppId,
            Name = result.Name,
            IdentityType = result.IdentityType,
            Roles = result.Roles,
            Scopes = result.Scopes
        });
    }

    /// <summary>
    /// Gets information about the current authentication configuration.
    /// </summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(AuthInfoResponse), StatusCodes.Status200OK)]
    public IActionResult GetAuthInfo()
    {
        var settings = _authSettings.Value;

        return Ok(new AuthInfoResponse
        {
            EnabledModes = settings.EnabledModes,
            SimulatedEnabled = settings.Simulated.Enabled,
            SimulatedIssuer = settings.Simulated.Issuer,
            SimulatedAudience = settings.Simulated.Audience,
            AllowedRoles = settings.Simulated.AllowedRoles,
            AllowedAppIds = settings.Simulated.AllowedAppIds,
            TokenLifetimeMinutes = settings.Simulated.TokenLifetimeMinutes
        });
    }

    /// <summary>
    /// Generates a quick test token with a specific role.
    /// </summary>
    /// <param name="role">The role to assign (url-encoded)</param>
    /// <returns>Generated token</returns>
    [HttpGet("quick/{role}")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status403Forbidden)]
    public IActionResult QuickToken(string role)
    {
        if (!_authSettings.Value.Simulated.Enabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ODataError.Create(
                "Forbidden",
                "Simulated authentication is not enabled."));
        }

        // Decode role name (handles URL encoding)
        var decodedRole = Uri.UnescapeDataString(role);

        // Map common short names to full role names
        var roleMapping = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = new() { "Owner", "Search Service Contributor", "Search Index Data Contributor", "Search Index Data Reader" },
            ["owner"] = new() { "Owner" },
            ["contributor"] = new() { "Contributor" },
            ["reader"] = new() { "Reader" },
            ["service-contributor"] = new() { "Search Service Contributor" },
            ["data-contributor"] = new() { "Search Index Data Contributor" },
            ["data-reader"] = new() { "Search Index Data Reader" },
            ["query"] = new() { "Search Index Data Reader" },
            ["index"] = new() { "Search Index Data Contributor", "Search Index Data Reader" }
        };

        var roles = roleMapping.TryGetValue(decodedRole, out var mappedRoles)
            ? mappedRoles
            : new List<string> { decodedRole };

        var result = _tokenService.GenerateToken(new SimulatedTokenRequest
        {
            IdentityType = "ServicePrincipal",
            AppId = "quick-test-app",
            Roles = roles,
            ExpiresInMinutes = 60
        });

        if (!result.Success)
        {
            return BadRequest(ODataError.Create("TokenGenerationFailed", result.Error ?? "Failed to generate token."));
        }

        return Ok(new TokenResponse
        {
            Token = result.Token!,
            ExpiresAt = result.ExpiresAt!.Value,
            TokenType = result.TokenType
        });
    }
}

#region Request/Response Models

/// <summary>
/// Request model for generating a token.
/// </summary>
public class TokenGenerationRequest
{
    /// <summary>
    /// Type of identity: "ServicePrincipal", "User", or "ManagedIdentity"
    /// </summary>
    [JsonPropertyName("identityType")]
    public string? IdentityType { get; set; } = "ServicePrincipal";

    /// <summary>
    /// Object ID for the identity (auto-generated if not provided).
    /// </summary>
    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }

    /// <summary>
    /// Application (client) ID for service principals.
    /// </summary>
    [JsonPropertyName("appId")]
    public string? AppId { get; set; }

    /// <summary>
    /// Display name for the identity.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Email/username for user identities.
    /// </summary>
    [JsonPropertyName("preferredUsername")]
    public string? PreferredUsername { get; set; }

    /// <summary>
    /// Tenant ID (defaults to simulator tenant).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>
    /// Roles to assign to the token.
    /// </summary>
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// Scopes for delegated tokens (user tokens).
    /// </summary>
    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    /// <summary>
    /// Token expiration in minutes (defaults to configured value).
    /// </summary>
    [JsonPropertyName("expiresInMinutes")]
    public int? ExpiresInMinutes { get; set; }
}

/// <summary>
/// Response model for token generation.
/// </summary>
public class TokenResponse
{
    /// <summary>
    /// The generated JWT token.
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Token expiration time.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Token type (always "Bearer").
    /// </summary>
    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>
/// Request model for token validation.
/// </summary>
public class TokenValidationRequest
{
    /// <summary>
    /// The token to validate (with or without "Bearer " prefix).
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Response model for token validation.
/// </summary>
public class TokenValidationResponse
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("objectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObjectId { get; set; }

    [JsonPropertyName("tenantId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TenantId { get; set; }

    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("identityType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdentityType { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// Response model for auth info endpoint.
/// </summary>
public class AuthInfoResponse
{
    [JsonPropertyName("enabledModes")]
    public List<string> EnabledModes { get; set; } = new();

    [JsonPropertyName("simulatedEnabled")]
    public bool SimulatedEnabled { get; set; }

    [JsonPropertyName("simulatedIssuer")]
    public string SimulatedIssuer { get; set; } = string.Empty;

    [JsonPropertyName("simulatedAudience")]
    public string SimulatedAudience { get; set; } = string.Empty;

    [JsonPropertyName("allowedRoles")]
    public List<string> AllowedRoles { get; set; } = new();

    [JsonPropertyName("allowedAppIds")]
    public List<string> AllowedAppIds { get; set; } = new();

    [JsonPropertyName("tokenLifetimeMinutes")]
    public int TokenLifetimeMinutes { get; set; }
}

#endregion
