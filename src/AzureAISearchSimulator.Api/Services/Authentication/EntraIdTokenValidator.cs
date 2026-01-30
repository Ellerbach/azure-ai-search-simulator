using AzureAISearchSimulator.Core.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AzureAISearchSimulator.Api.Services.Authentication;

/// <summary>
/// Service for validating real Azure Entra ID (Azure AD) JWT tokens.
/// Retrieves signing keys from Azure AD metadata endpoint and validates tokens.
/// </summary>
public class EntraIdTokenValidator : IEntraIdTokenValidator
{
    private readonly ILogger<EntraIdTokenValidator> _logger;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    // Azure AD well-known endpoints
    private const string AzureADMetadataEndpointFormat = "{0}{1}/v2.0/.well-known/openid-configuration";
    private const string AzureADCommonMetadataEndpoint = "{0}common/v2.0/.well-known/openid-configuration";

    // Cache keys
    private const string ConfigurationCacheKeyFormat = "entra_id_config_{0}_{1}";

    // Standard claim types
    private const string ObjectIdClaim = "oid";
    private const string TenantIdClaim = "tid";
    private const string AppIdClaim = "appid";
    private const string AzpClaim = "azp";
    private const string IdentityTypeClaim = "idtyp";
    private const string RolesClaim = "roles";
    private const string ScopesClaim = "scp";
    private const string NameClaim = "name";
    private const string PreferredUsernameClaim = "preferred_username";
    private const string UpnClaim = "upn";
    private const string SubjectClaim = "sub";

    public EntraIdTokenValidator(
        ILogger<EntraIdTokenValidator> logger,
        IMemoryCache cache,
        HttpClient httpClient)
    {
        _logger = logger;
        _cache = cache;
        _httpClient = httpClient;
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <inheritdoc />
    public async Task<EntraIdValidationResult> ValidateTokenAsync(
        string token,
        EntraIdSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return EntraIdValidationResult.Failure("MissingToken", "Token is required.");
        }

        try
        {
            // Get OpenID Connect configuration
            var configuration = await GetOpenIdConfigurationAsync(settings, cancellationToken);
            if (configuration == null)
            {
                return EntraIdValidationResult.Failure("ConfigurationError", "Failed to retrieve Azure AD configuration.");
            }

            // Build validation parameters
            var validationParameters = BuildValidationParameters(settings, configuration);

            // Validate the token
            var principal = await _tokenHandler.ValidateTokenAsync(token, validationParameters);

            if (!principal.IsValid)
            {
                var errorMessage = principal.Exception?.Message ?? "Token validation failed.";
                _logger.LogWarning("Token validation failed: {Error}", errorMessage);
                return EntraIdValidationResult.Failure("InvalidToken", errorMessage);
            }

            // Extract claims
            var result = ExtractClaims(principal.ClaimsIdentity, settings);
            result.IsValid = true;
            result.Principal = new ClaimsPrincipal(principal.ClaimsIdentity);

            _logger.LogDebug("Token validated successfully for {IdentityType} {ObjectId}",
                result.IdentityType, result.ObjectId);

            return result;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Token has expired");
            return EntraIdValidationResult.Failure("TokenExpired", "The token has expired.");
        }
        catch (SecurityTokenNotYetValidException)
        {
            _logger.LogWarning("Token is not yet valid");
            return EntraIdValidationResult.Failure("TokenNotYetValid", "The token is not yet valid.");
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            _logger.LogWarning("Invalid token audience: {Audience}", ex.InvalidAudience);
            return EntraIdValidationResult.Failure("InvalidAudience", $"Invalid token audience: {ex.InvalidAudience}");
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            _logger.LogWarning("Invalid token issuer: {Issuer}", ex.InvalidIssuer);
            return EntraIdValidationResult.Failure("InvalidIssuer", $"Invalid token issuer: {ex.InvalidIssuer}");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("Invalid token signature");
            return EntraIdValidationResult.Failure("InvalidSignature", "The token signature is invalid.");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Security token exception: {Message}", ex.Message);
            return EntraIdValidationResult.Failure("InvalidToken", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation");
            return EntraIdValidationResult.Failure("ValidationError", "An unexpected error occurred during token validation.");
        }
    }

    /// <summary>
    /// Gets the OpenID Connect configuration from cache or Azure AD.
    /// </summary>
    private async Task<OpenIdConnectConfiguration?> GetOpenIdConfigurationAsync(
        EntraIdSettings settings,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Format(ConfigurationCacheKeyFormat, settings.Instance, settings.TenantId);

        if (_cache.TryGetValue(cacheKey, out OpenIdConnectConfiguration? cachedConfig))
        {
            return cachedConfig;
        }

        try
        {
            var metadataAddress = GetMetadataAddress(settings);
            _logger.LogDebug("Fetching OpenID Connect configuration from: {MetadataAddress}", metadataAddress);

            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_httpClient) { RequireHttps = settings.RequireHttpsMetadata });

            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken);

            // Cache for 24 hours
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(24));

            _cache.Set(cacheKey, configuration, cacheOptions);

            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve OpenID Connect configuration from {Instance}", settings.Instance);
            return null;
        }
    }

    /// <summary>
    /// Builds token validation parameters from settings and configuration.
    /// </summary>
    private TokenValidationParameters BuildValidationParameters(
        EntraIdSettings settings,
        OpenIdConnectConfiguration configuration)
    {
        var validAudiences = GetValidAudiences(settings);
        var validIssuers = GetValidIssuers(settings);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5),
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };
    }

    /// <summary>
    /// Gets the list of valid audiences.
    /// </summary>
    private List<string> GetValidAudiences(EntraIdSettings settings)
    {
        var audiences = new List<string>();

        // Add configured audience
        if (!string.IsNullOrEmpty(settings.Audience))
        {
            audiences.Add(settings.Audience);
        }

        // Add client ID as valid audience (common pattern)
        if (!string.IsNullOrEmpty(settings.ClientId))
        {
            audiences.Add(settings.ClientId);
            audiences.Add($"api://{settings.ClientId}");
        }

        // Add Azure AI Search default audience
        if (!audiences.Contains("https://search.azure.com"))
        {
            audiences.Add("https://search.azure.com");
        }

        return audiences;
    }

    /// <summary>
    /// Gets the list of valid issuers.
    /// </summary>
    private List<string> GetValidIssuers(EntraIdSettings settings)
    {
        var issuers = new List<string>();

        // Use configured issuers first
        if (settings.ValidIssuers?.Count > 0)
        {
            issuers.AddRange(settings.ValidIssuers);
        }
        else if (!string.IsNullOrEmpty(settings.TenantId))
        {
            // Generate standard issuer URLs for the tenant
            var instance = settings.Instance.TrimEnd('/');

            // v2.0 issuer format
            issuers.Add($"{instance}/{settings.TenantId}/v2.0");

            // v1.0 issuer format (sts.windows.net)
            issuers.Add($"https://sts.windows.net/{settings.TenantId}/");

            // For multi-tenant apps, allow common issuer
            if (settings.AllowMultipleTenants)
            {
                issuers.Add($"{instance}/common/v2.0");
                issuers.Add("https://sts.windows.net/common/");
            }
        }

        return issuers;
    }

    /// <summary>
    /// Gets the metadata address for OpenID Connect configuration.
    /// </summary>
    private string GetMetadataAddress(EntraIdSettings settings)
    {
        var instance = settings.Instance.TrimEnd('/') + "/";

        if (settings.AllowMultipleTenants || string.IsNullOrEmpty(settings.TenantId))
        {
            return string.Format(AzureADCommonMetadataEndpoint, instance);
        }

        return string.Format(AzureADMetadataEndpointFormat, instance, settings.TenantId);
    }

    /// <summary>
    /// Extracts claims from the validated token.
    /// </summary>
    private EntraIdValidationResult ExtractClaims(ClaimsIdentity identity, EntraIdSettings settings)
    {
        var result = new EntraIdValidationResult();

        // Object ID (oid)
        result.ObjectId = GetClaimValue(identity, ObjectIdClaim)
            ?? GetClaimValue(identity, SubjectClaim);

        // Tenant ID (tid)
        result.TenantId = GetClaimValue(identity, TenantIdClaim);

        // Application ID (appid for v1 tokens, azp for v2 tokens)
        result.AppId = GetClaimValue(identity, AppIdClaim)
            ?? GetClaimValue(identity, AzpClaim);

        // Identity type (idtyp)
        var idtyp = GetClaimValue(identity, IdentityTypeClaim);
        result.IdentityType = DetermineIdentityType(idtyp, identity);

        // Name
        result.Name = GetClaimValue(identity, NameClaim)
            ?? GetClaimValue(identity, PreferredUsernameClaim)
            ?? GetClaimValue(identity, UpnClaim);

        // Roles
        result.Roles = GetClaimValues(identity, RolesClaim);

        // Scopes (for delegated tokens)
        var scpClaim = GetClaimValue(identity, ScopesClaim);
        if (!string.IsNullOrEmpty(scpClaim))
        {
            result.Scopes = scpClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // Validate tenant if not multi-tenant
        if (!settings.AllowMultipleTenants && !string.IsNullOrEmpty(settings.TenantId))
        {
            if (!string.IsNullOrEmpty(result.TenantId) &&
                !result.TenantId.Equals(settings.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                return EntraIdValidationResult.Failure(
                    "InvalidTenant",
                    $"Token tenant '{result.TenantId}' does not match expected tenant '{settings.TenantId}'.");
            }
        }

        return result;
    }

    /// <summary>
    /// Determines the identity type from the idtyp claim or token structure.
    /// </summary>
    private string DetermineIdentityType(string? idtyp, ClaimsIdentity identity)
    {
        // idtyp claim is the most reliable
        if (!string.IsNullOrEmpty(idtyp))
        {
            return idtyp.Equals("app", StringComparison.OrdinalIgnoreCase)
                ? "ServicePrincipal"
                : "User";
        }

        // If there's an appid claim without a name, it's likely a service principal
        var hasAppId = !string.IsNullOrEmpty(GetClaimValue(identity, AppIdClaim));
        var hasName = !string.IsNullOrEmpty(GetClaimValue(identity, NameClaim))
            || !string.IsNullOrEmpty(GetClaimValue(identity, PreferredUsernameClaim));

        if (hasAppId && !hasName)
        {
            return "ServicePrincipal";
        }

        // Default to User for delegated tokens
        return "User";
    }

    /// <summary>
    /// Gets a single claim value from the identity.
    /// </summary>
    private static string? GetClaimValue(ClaimsIdentity identity, string claimType)
    {
        return identity.FindFirst(claimType)?.Value;
    }

    /// <summary>
    /// Gets multiple claim values for claims that can appear multiple times.
    /// </summary>
    private static List<string> GetClaimValues(ClaimsIdentity identity, string claimType)
    {
        return identity.FindAll(claimType)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();
    }
}
