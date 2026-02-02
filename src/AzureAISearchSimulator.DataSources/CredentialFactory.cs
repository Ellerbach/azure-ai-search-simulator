using Azure.Core;
using Azure.Identity;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Credentials;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace AzureAISearchSimulator.DataSources;

/// <summary>
/// Factory for creating Azure credentials based on configuration.
/// Supports DefaultAzureCredential, Service Principal, and Managed Identity.
/// </summary>
public class CredentialFactory : ICredentialFactory
{
    private readonly ILogger<CredentialFactory> _logger;
    private readonly IOptionsMonitor<OutboundAuthenticationSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly object _credentialLock = new();

    // Default scope for Azure management
    private const string DefaultTestScope = "https://management.azure.com/.default";

    // Common Azure scopes
    public static class Scopes
    {
        public const string Storage = "https://storage.azure.com/.default";
        public const string Management = "https://management.azure.com/.default";
        public const string KeyVault = "https://vault.azure.net/.default";
        public const string CognitiveServices = "https://cognitiveservices.azure.com/.default";
        public const string Search = "https://search.azure.com/.default";
    }

    private TokenCredential? _defaultCredential;

    public CredentialFactory(
        ILogger<CredentialFactory> logger,
        IOptionsMonitor<OutboundAuthenticationSettings> settings,
        IMemoryCache cache)
    {
        _logger = logger;
        _settings = settings;
        _cache = cache;
    }

    /// <inheritdoc />
    public TokenCredential GetDefaultCredential()
    {
        if (_defaultCredential != null)
        {
            return _defaultCredential;
        }

        lock (_credentialLock)
        {
            if (_defaultCredential != null)
            {
                return _defaultCredential;
            }

            var settings = _settings.CurrentValue;
            _defaultCredential = CreateCredential(settings.DefaultCredentialType, settings);

            _logger.LogInformation("Created default credential of type: {CredentialType}", settings.DefaultCredentialType);

            return _defaultCredential;
        }
    }

    /// <inheritdoc />
    public TokenCredential GetCredential(string? resourceUri = null, SearchIdentity? identity = null)
    {
        var settings = _settings.CurrentValue;

        // If identity specifies "None", return a null credential indicator
        // The caller should use connection string instead
        if (identity?.IsNone == true)
        {
            _logger.LogDebug("Identity is None - caller should use connection string");
            // Return a credential that will fail - the caller should check IsNone first
            throw new InvalidOperationException("Identity is set to None. Use connection string authentication instead.");
        }

        // If using user-assigned managed identity
        if (identity?.IsUserAssigned == true && !string.IsNullOrEmpty(identity.UserAssignedIdentity))
        {
            var clientId = ExtractClientIdFromResourceId(identity.UserAssignedIdentity);
            _logger.LogDebug("Using user-assigned managed identity: {Identity}", identity.UserAssignedIdentity);

            return new ManagedIdentityCredential(clientId);
        }

        // If identity is system-assigned (null or empty)
        if (identity != null && identity.IsSystemAssigned)
        {
            _logger.LogDebug("Using system-assigned managed identity");
            return new ManagedIdentityCredential();
        }

        // Otherwise, use the default credential
        return GetDefaultCredential();
    }

    /// <inheritdoc />
    public async Task<AccessToken> GetTokenAsync(string scope, CancellationToken cancellationToken = default)
    {
        return await GetTokenAsync(scope, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccessToken> GetTokenAsync(string scope, SearchIdentity? identity, CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;
        var cacheKey = $"token_{scope}_{identity?.UserAssignedIdentity ?? "default"}";

        // Check cache if enabled
        if (settings.TokenCache.Enabled && _cache.TryGetValue(cacheKey, out AccessToken cachedToken))
        {
            // Check if token is still valid (with buffer)
            var refreshTime = cachedToken.ExpiresOn.AddMinutes(-settings.TokenCache.RefreshBeforeExpirationMinutes);
            if (DateTimeOffset.UtcNow < refreshTime)
            {
                _logger.LogDebug("Using cached token for scope: {Scope}", scope);
                return cachedToken;
            }
        }

        var credential = GetCredential(identity: identity);
        var context = new TokenRequestContext(new[] { scope });

        var token = await credential.GetTokenAsync(context, cancellationToken);

        // Cache the token if caching is enabled
        if (settings.TokenCache.Enabled)
        {
            var expiration = token.ExpiresOn.AddMinutes(-settings.TokenCache.RefreshBeforeExpirationMinutes);
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expiration);

            _cache.Set(cacheKey, token, cacheOptions);

            _logger.LogDebug("Cached token for scope: {Scope}, expires: {Expiration}", scope, token.ExpiresOn);
        }

        return token;
    }

    /// <inheritdoc />
    public CredentialInfo GetCredentialInfo()
    {
        var settings = _settings.CurrentValue;
        var info = new CredentialInfo
        {
            CredentialType = settings.DefaultCredentialType
        };

        switch (settings.DefaultCredentialType.ToLowerInvariant())
        {
            case "serviceprincipal":
                info.TenantId = MaskSensitive(settings.ServicePrincipal.TenantId);
                info.ClientId = MaskSensitive(settings.ServicePrincipal.ClientId);
                info.Details["AuthMethod"] = !string.IsNullOrEmpty(settings.ServicePrincipal.ClientSecret)
                    ? "ClientSecret"
                    : !string.IsNullOrEmpty(settings.ServicePrincipal.CertificatePath)
                        ? "Certificate"
                        : "Unknown";
                break;

            case "managedidentity":
                info.Details["IdentityType"] = string.IsNullOrEmpty(settings.ManagedIdentity.ClientId)
                    ? "SystemAssigned"
                    : "UserAssigned";
                info.ManagedIdentityResourceId = settings.ManagedIdentity.ResourceId;
                info.ClientId = MaskSensitive(settings.ManagedIdentity.ClientId);
                break;

            case "defaultazurecredential":
                info.TenantId = MaskSensitive(settings.DefaultCredential.TenantId);
                info.ClientId = MaskSensitive(settings.DefaultCredential.ManagedIdentityClientId);
                info.Details["ExcludedCredentials"] = GetExcludedCredentials(settings.DefaultCredential);
                break;
        }

        info.Details["TokenCacheEnabled"] = settings.TokenCache.Enabled.ToString();

        return info;
    }

    /// <inheritdoc />
    public async Task<CredentialTestResult> TestCredentialAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var credential = GetDefaultCredential();
            var scope = DefaultTestScope;

            var context = new TokenRequestContext(new[] { scope });
            var token = await credential.GetTokenAsync(context, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation("Credential test succeeded. Type: {Type}, Expires: {Expiration}",
                settings.DefaultCredentialType, token.ExpiresOn);

            return CredentialTestResult.Succeeded(
                settings.DefaultCredentialType,
                token.ExpiresOn,
                stopwatch.Elapsed,
                scope);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex, "Credential test failed: {Error}", ex.Message);

            return CredentialTestResult.Failed(ex.Message, stopwatch.Elapsed);
        }
    }

    private TokenCredential CreateCredential(string credentialType, OutboundAuthenticationSettings settings)
    {
        return credentialType.ToLowerInvariant() switch
        {
            "serviceprincipal" => CreateServicePrincipalCredential(settings.ServicePrincipal),
            "managedidentity" => CreateManagedIdentityCredential(settings.ManagedIdentity),
            "defaultazurecredential" => CreateDefaultAzureCredential(settings.DefaultCredential),
            _ => CreateDefaultAzureCredential(settings.DefaultCredential)
        };
    }

    private TokenCredential CreateServicePrincipalCredential(ServicePrincipalSettings settings)
    {
        if (string.IsNullOrEmpty(settings.TenantId) || string.IsNullOrEmpty(settings.ClientId))
        {
            throw new InvalidOperationException("ServicePrincipal requires TenantId and ClientId");
        }

        // Client secret authentication
        if (!string.IsNullOrEmpty(settings.ClientSecret))
        {
            _logger.LogDebug("Creating ClientSecretCredential for tenant: {TenantId}", settings.TenantId);
            return new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret);
        }

        // Certificate authentication
        if (!string.IsNullOrEmpty(settings.CertificatePath))
        {
            _logger.LogDebug("Creating ClientCertificateCredential from file for tenant: {TenantId}", settings.TenantId);

            var certificate = !string.IsNullOrEmpty(settings.CertificatePassword)
                ? new X509Certificate2(settings.CertificatePath, settings.CertificatePassword)
                : new X509Certificate2(settings.CertificatePath);

            return new ClientCertificateCredential(
                settings.TenantId,
                settings.ClientId,
                certificate,
                new ClientCertificateCredentialOptions { SendCertificateChain = settings.SendCertificateChain });
        }

        // Certificate from store (Windows only)
        if (!string.IsNullOrEmpty(settings.CertificateThumbprint))
        {
            _logger.LogDebug("Creating ClientCertificateCredential from store for tenant: {TenantId}", settings.TenantId);

            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                settings.CertificateThumbprint,
                validOnly: false);

            if (certificates.Count == 0)
            {
                throw new InvalidOperationException($"Certificate with thumbprint {settings.CertificateThumbprint} not found");
            }

            return new ClientCertificateCredential(
                settings.TenantId,
                settings.ClientId,
                certificates[0],
                new ClientCertificateCredentialOptions { SendCertificateChain = settings.SendCertificateChain });
        }

        throw new InvalidOperationException("ServicePrincipal requires either ClientSecret or Certificate configuration");
    }

    private TokenCredential CreateManagedIdentityCredential(ManagedIdentitySettings settings)
    {
        if (!string.IsNullOrEmpty(settings.ClientId))
        {
            _logger.LogDebug("Creating ManagedIdentityCredential with client ID: {ClientId}", settings.ClientId);
            return new ManagedIdentityCredential(settings.ClientId);
        }

        if (!string.IsNullOrEmpty(settings.ResourceId))
        {
            _logger.LogDebug("Creating ManagedIdentityCredential with resource ID: {ResourceId}", settings.ResourceId);
            return new ManagedIdentityCredential(new ResourceIdentifier(settings.ResourceId));
        }

        _logger.LogDebug("Creating system-assigned ManagedIdentityCredential");
        return new ManagedIdentityCredential();
    }

    private TokenCredential CreateDefaultAzureCredential(DefaultCredentialSettings settings)
    {
        var options = new DefaultAzureCredentialOptions
        {
            ExcludeAzureCliCredential = settings.ExcludeAzureCliCredential,
            ExcludeAzurePowerShellCredential = settings.ExcludeAzurePowerShellCredential,
            ExcludeEnvironmentCredential = settings.ExcludeEnvironmentCredential,
            ExcludeManagedIdentityCredential = settings.ExcludeManagedIdentityCredential,
            ExcludeVisualStudioCredential = settings.ExcludeVisualStudioCredential,
            ExcludeVisualStudioCodeCredential = settings.ExcludeVisualStudioCodeCredential,
            ExcludeSharedTokenCacheCredential = settings.ExcludeSharedTokenCacheCredential,
            ExcludeInteractiveBrowserCredential = settings.ExcludeInteractiveBrowserCredential
        };

        if (!string.IsNullOrEmpty(settings.TenantId))
        {
            options.TenantId = settings.TenantId;
        }

        if (!string.IsNullOrEmpty(settings.ManagedIdentityClientId))
        {
            options.ManagedIdentityClientId = settings.ManagedIdentityClientId;
        }

        _logger.LogDebug("Creating DefaultAzureCredential");
        return new DefaultAzureCredential(options);
    }

    /// <summary>
    /// Extracts the client ID from a managed identity resource ID.
    /// </summary>
    private static string? ExtractClientIdFromResourceId(string resourceId)
    {
        // Resource ID format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}
        // We need to look up the client ID from the resource, but for now we'll just use the resource ID
        // In a real implementation, you would call ARM to get the client ID
        // For the simulator, we'll accept the client ID being passed directly or derive it

        // If it looks like a GUID, it's probably the client ID directly
        if (Guid.TryParse(resourceId, out _))
        {
            return resourceId;
        }

        // Otherwise, return null and let ManagedIdentityCredential handle it
        return null;
    }

    private static string MaskSensitive(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(not set)";
        }

        if (value.Length <= 8)
        {
            return "****";
        }

        return $"{value[..4]}...{value[^4..]}";
    }

    private static string GetExcludedCredentials(DefaultCredentialSettings settings)
    {
        var excluded = new List<string>();

        if (settings.ExcludeAzureCliCredential) excluded.Add("AzureCli");
        if (settings.ExcludeAzurePowerShellCredential) excluded.Add("AzurePowerShell");
        if (settings.ExcludeEnvironmentCredential) excluded.Add("Environment");
        if (settings.ExcludeManagedIdentityCredential) excluded.Add("ManagedIdentity");
        if (settings.ExcludeVisualStudioCredential) excluded.Add("VisualStudio");
        if (settings.ExcludeVisualStudioCodeCredential) excluded.Add("VisualStudioCode");
        if (settings.ExcludeSharedTokenCacheCredential) excluded.Add("SharedTokenCache");
        if (settings.ExcludeInteractiveBrowserCredential) excluded.Add("InteractiveBrowser");

        return excluded.Count > 0 ? string.Join(", ", excluded) : "None";
    }
}
