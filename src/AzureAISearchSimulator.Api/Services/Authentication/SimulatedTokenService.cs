using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AzureAISearchSimulator.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AzureAISearchSimulator.Api.Services.Authentication;

/// <summary>
/// Service for generating and validating simulated JWT tokens for local development.
/// These tokens mimic the structure of real Azure AD tokens.
/// </summary>
public interface ISimulatedTokenService
{
    /// <summary>
    /// Generates a simulated JWT token.
    /// </summary>
    SimulatedTokenResult GenerateToken(SimulatedTokenRequest request);

    /// <summary>
    /// Validates a simulated JWT token and extracts claims.
    /// </summary>
    TokenValidationResult ValidateToken(string token);

    /// <summary>
    /// Gets the token validation parameters for this service.
    /// </summary>
    TokenValidationParameters GetValidationParameters();
}

/// <summary>
/// Request model for generating a simulated token.
/// </summary>
public class SimulatedTokenRequest
{
    /// <summary>
    /// Type of identity: "ServicePrincipal", "User", or "ManagedIdentity"
    /// </summary>
    public string IdentityType { get; set; } = "ServicePrincipal";

    /// <summary>
    /// Object ID of the identity.
    /// </summary>
    public string? ObjectId { get; set; }

    /// <summary>
    /// Application (client) ID for service principals.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Display name for the identity.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Email/username for user identities.
    /// </summary>
    public string? PreferredUsername { get; set; }

    /// <summary>
    /// Tenant ID.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Roles to assign to the token.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// Scopes for delegated tokens.
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Token expiration in minutes.
    /// </summary>
    public int ExpiresInMinutes { get; set; } = 60;
}

/// <summary>
/// Result of token generation.
/// </summary>
public class SimulatedTokenResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public string? Error { get; set; }
}

/// <summary>
/// Result of token validation.
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public ClaimsPrincipal? Principal { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    
    // Extracted claims
    public string? ObjectId { get; set; }
    public string? TenantId { get; set; }
    public string? AppId { get; set; }
    public string? Name { get; set; }
    public string? IdentityType { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// Implementation of the simulated token service.
/// </summary>
public class SimulatedTokenService : ISimulatedTokenService
{
    private readonly ILogger<SimulatedTokenService> _logger;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public SimulatedTokenService(
        ILogger<SimulatedTokenService> logger,
        IOptionsMonitor<AuthenticationSettings> authSettings)
    {
        _logger = logger;
        _authSettings = authSettings;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <inheritdoc />
    public SimulatedTokenResult GenerateToken(SimulatedTokenRequest request)
    {
        var settings = _authSettings.CurrentValue.Simulated;

        if (!settings.Enabled)
        {
            return new SimulatedTokenResult
            {
                Success = false,
                Error = "Simulated authentication is not enabled."
            };
        }

        // Validate roles
        foreach (var role in request.Roles)
        {
            if (!settings.AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return new SimulatedTokenResult
                {
                    Success = false,
                    Error = $"Role '{role}' is not in the allowed roles list."
                };
            }
        }

        // Validate app ID if provided
        if (!string.IsNullOrEmpty(request.AppId) && 
            settings.AllowedAppIds.Count > 0 &&
            !settings.AllowedAppIds.Contains(request.AppId, StringComparer.OrdinalIgnoreCase))
        {
            return new SimulatedTokenResult
            {
                Success = false,
                Error = $"AppId '{request.AppId}' is not in the allowed app IDs list."
            };
        }

        try
        {
            var now = DateTime.UtcNow;
            var expires = now.AddMinutes(request.ExpiresInMinutes > 0 ? request.ExpiresInMinutes : settings.TokenLifetimeMinutes);
            
            var claims = new List<Claim>
            {
                new Claim("aud", settings.Audience),
                new Claim("iss", settings.Issuer),
                new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("nbf", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("exp", new DateTimeOffset(expires).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("ver", "2.0")
            };

            // Object ID
            var oid = request.ObjectId ?? Guid.NewGuid().ToString();
            claims.Add(new Claim("oid", oid));

            // Tenant ID
            var tid = request.TenantId ?? "00000000-0000-0000-0000-000000000000";
            claims.Add(new Claim("tid", tid));

            // Identity type specific claims
            switch (request.IdentityType?.ToLowerInvariant())
            {
                case "user":
                    claims.Add(new Claim("idtyp", "user"));
                    if (!string.IsNullOrEmpty(request.Name))
                        claims.Add(new Claim("name", request.Name));
                    if (!string.IsNullOrEmpty(request.PreferredUsername))
                        claims.Add(new Claim("preferred_username", request.PreferredUsername));
                    // User tokens use scopes
                    foreach (var scope in request.Scopes)
                    {
                        claims.Add(new Claim("scp", scope));
                    }
                    break;

                case "managedidentity":
                    claims.Add(new Claim("idtyp", "app"));
                    claims.Add(new Claim("azp", request.AppId ?? oid));
                    break;

                case "serviceprincipal":
                default:
                    claims.Add(new Claim("idtyp", "app"));
                    if (!string.IsNullOrEmpty(request.AppId))
                    {
                        claims.Add(new Claim("appid", request.AppId));
                        claims.Add(new Claim("azp", request.AppId));
                    }
                    break;
            }

            // Add roles
            foreach (var role in request.Roles)
            {
                claims.Add(new Claim("roles", role));
            }

            // Create the token
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: settings.Issuer,
                audience: settings.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: creds);

            var tokenString = _tokenHandler.WriteToken(token);

            _logger.LogInformation("Generated simulated token for {IdentityType} with roles: {Roles}", 
                request.IdentityType, string.Join(", ", request.Roles));

            return new SimulatedTokenResult
            {
                Success = true,
                Token = tokenString,
                ExpiresAt = expires,
                TokenType = "Bearer"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate simulated token");
            return new SimulatedTokenResult
            {
                Success = false,
                Error = $"Failed to generate token: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public TokenValidationResult ValidateToken(string token)
    {
        var settings = _authSettings.CurrentValue.Simulated;

        if (!settings.Enabled)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Simulated authentication is not enabled.",
                ErrorCode = "SimulatedAuthDisabled"
            };
        }

        try
        {
            var validationParameters = GetValidationParameters();
            var principal = _tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            var result = new TokenValidationResult
            {
                IsValid = true,
                Principal = principal
            };

            // Extract claims
            var jwtToken = validatedToken as JwtSecurityToken;
            if (jwtToken != null)
            {
                result.ObjectId = jwtToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
                result.TenantId = jwtToken.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
                result.AppId = jwtToken.Claims.FirstOrDefault(c => c.Type == "appid")?.Value 
                            ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
                result.Name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                           ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
                result.IdentityType = jwtToken.Claims.FirstOrDefault(c => c.Type == "idtyp")?.Value == "user" 
                    ? "User" 
                    : "ServicePrincipal";
                result.Roles = jwtToken.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToList();
                result.Scopes = jwtToken.Claims.Where(c => c.Type == "scp").Select(c => c.Value).ToList();
            }

            return result;
        }
        catch (SecurityTokenExpiredException)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Token has expired.",
                ErrorCode = "TokenExpired"
            };
        }
        catch (SecurityTokenNotYetValidException)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Token is not yet valid.",
                ErrorCode = "TokenNotYetValid"
            };
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Token signature is invalid.",
                ErrorCode = "InvalidSignature"
            };
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Token audience is invalid.",
                ErrorCode = "InvalidAudience"
            };
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                Error = "Token issuer is invalid.",
                ErrorCode = "InvalidIssuer"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return new TokenValidationResult
            {
                IsValid = false,
                Error = $"Token validation failed: {ex.Message}",
                ErrorCode = "ValidationFailed"
            };
        }
    }

    /// <inheritdoc />
    public TokenValidationParameters GetValidationParameters()
    {
        var settings = _authSettings.CurrentValue.Simulated;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    }
}
