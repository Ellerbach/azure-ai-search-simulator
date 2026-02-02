namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for outbound authentication to Azure services.
/// Used when the simulator needs to authenticate to external services like
/// Azure Blob Storage, ADLS Gen2, Azure OpenAI, or custom skill endpoints.
/// </summary>
public class OutboundAuthenticationSettings
{
    public const string SectionName = "OutboundAuthentication";

    /// <summary>
    /// Default credential type to use when not specified by the resource.
    /// Options: "DefaultAzureCredential", "ServicePrincipal", "ManagedIdentity", "ConnectionString"
    /// </summary>
    public string DefaultCredentialType { get; set; } = "DefaultAzureCredential";

    /// <summary>
    /// Service principal settings for service-to-service authentication.
    /// </summary>
    public ServicePrincipalSettings ServicePrincipal { get; set; } = new();

    /// <summary>
    /// Managed identity settings.
    /// </summary>
    public ManagedIdentitySettings ManagedIdentity { get; set; } = new();

    /// <summary>
    /// Token caching settings.
    /// </summary>
    public TokenCacheSettings TokenCache { get; set; } = new();

    /// <summary>
    /// DefaultAzureCredential settings.
    /// </summary>
    public DefaultCredentialSettings DefaultCredential { get; set; } = new();

    /// <summary>
    /// Whether to enable detailed credential logging (useful for debugging).
    /// Warning: May log sensitive information in development.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}

/// <summary>
/// Service principal (client credentials) authentication settings.
/// </summary>
public class ServicePrincipalSettings
{
    /// <summary>
    /// Azure AD tenant ID.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    /// Application (client) ID.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Client secret (for client secret authentication).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Path to client certificate (for certificate authentication).
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Certificate password (if certificate is password-protected).
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// Certificate thumbprint (for certificate store lookup on Windows).
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Whether to send certificate chain with requests.
    /// </summary>
    public bool SendCertificateChain { get; set; } = false;
}

/// <summary>
/// Managed identity settings.
/// </summary>
public class ManagedIdentitySettings
{
    /// <summary>
    /// Whether to use managed identity. When true and running in Azure,
    /// the system will attempt to use managed identity for authentication.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Client ID of the user-assigned managed identity.
    /// If null, system-assigned managed identity is used.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Resource ID of the user-assigned managed identity.
    /// Alternative to ClientId for specifying user-assigned identity.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}
    /// </summary>
    public string? ResourceId { get; set; }
}

/// <summary>
/// Token caching settings for improved performance.
/// </summary>
public class TokenCacheSettings
{
    /// <summary>
    /// Whether to cache tokens.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of minutes before token expiration to proactively refresh.
    /// </summary>
    public int RefreshBeforeExpirationMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of cached tokens.
    /// </summary>
    public int MaxCacheSize { get; set; } = 100;
}

/// <summary>
/// Settings for DefaultAzureCredential behavior.
/// </summary>
public class DefaultCredentialSettings
{
    /// <summary>
    /// Whether to exclude Azure CLI credential from the chain.
    /// </summary>
    public bool ExcludeAzureCliCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude Azure PowerShell credential from the chain.
    /// </summary>
    public bool ExcludeAzurePowerShellCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude environment variables credential from the chain.
    /// </summary>
    public bool ExcludeEnvironmentCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude managed identity credential from the chain.
    /// </summary>
    public bool ExcludeManagedIdentityCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude Visual Studio credential from the chain.
    /// </summary>
    public bool ExcludeVisualStudioCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude Visual Studio Code credential from the chain.
    /// </summary>
    public bool ExcludeVisualStudioCodeCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude shared token cache credential from the chain.
    /// </summary>
    public bool ExcludeSharedTokenCacheCredential { get; set; } = false;

    /// <summary>
    /// Whether to exclude interactive browser credential from the chain.
    /// </summary>
    public bool ExcludeInteractiveBrowserCredential { get; set; } = true;

    /// <summary>
    /// Tenant ID to use for all credentials in the chain.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Managed identity client ID to use (for user-assigned identities).
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }
}
