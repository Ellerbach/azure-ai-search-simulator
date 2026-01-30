namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Main configuration settings for authentication in the Azure AI Search Simulator.
/// Supports multiple authentication modes: API Key, Entra ID, and Simulated tokens.
/// </summary>
public class AuthenticationSettings
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// List of enabled authentication modes. Options: "ApiKey", "EntraId", "Simulated"
    /// </summary>
    public List<string> EnabledModes { get; set; } = new() { "ApiKey" };

    /// <summary>
    /// Default authentication mode to use when multiple are enabled.
    /// </summary>
    public string DefaultMode { get; set; } = "ApiKey";

    /// <summary>
    /// If true, API key takes precedence when both api-key header and Bearer token are present.
    /// This matches real Azure AI Search behavior.
    /// </summary>
    public bool ApiKeyTakesPrecedence { get; set; } = true;

    /// <summary>
    /// API key authentication settings.
    /// </summary>
    public ApiKeySettings ApiKey { get; set; } = new();

    /// <summary>
    /// Entra ID (Azure AD) authentication settings.
    /// </summary>
    public EntraIdSettings EntraId { get; set; } = new();

    /// <summary>
    /// Simulated token authentication settings for local development.
    /// </summary>
    public SimulatedAuthSettings Simulated { get; set; } = new();

    /// <summary>
    /// Role mapping settings for RBAC.
    /// </summary>
    public RoleMappingSettings RoleMapping { get; set; } = new();
}

/// <summary>
/// API key authentication settings.
/// </summary>
public class ApiKeySettings
{
    /// <summary>
    /// Admin API key for full read/write access.
    /// If not set, falls back to SimulatorSettings.AdminApiKey for backward compatibility.
    /// </summary>
    public string? AdminApiKey { get; set; }

    /// <summary>
    /// Query API key for read-only search operations.
    /// If not set, falls back to SimulatorSettings.QueryApiKey for backward compatibility.
    /// </summary>
    public string? QueryApiKey { get; set; }
}

/// <summary>
/// Entra ID (Azure AD) authentication settings.
/// </summary>
public class EntraIdSettings
{
    /// <summary>
    /// Azure AD instance URL (e.g., https://login.microsoftonline.com/)
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Azure AD tenant ID.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    /// Application (client) ID for the search service.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Expected audience for tokens. Default matches Azure AI Search.
    /// </summary>
    public string Audience { get; set; } = "https://search.azure.com";

    /// <summary>
    /// List of valid token issuers. If empty, derived from Instance + TenantId.
    /// </summary>
    public List<string> ValidIssuers { get; set; } = new();

    /// <summary>
    /// Whether to require HTTPS for metadata endpoints.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Whether to allow multi-tenant tokens.
    /// </summary>
    public bool AllowMultipleTenants { get; set; } = false;
}

/// <summary>
/// Simulated token authentication settings for local development without Azure.
/// </summary>
public class SimulatedAuthSettings
{
    /// <summary>
    /// Whether simulated authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Issuer for simulated tokens.
    /// </summary>
    public string Issuer { get; set; } = "https://simulator.local/";

    /// <summary>
    /// Audience for simulated tokens.
    /// </summary>
    public string Audience { get; set; } = "https://search.azure.com";

    /// <summary>
    /// Secret key for signing simulated tokens. Should be at least 32 characters.
    /// </summary>
    public string SigningKey { get; set; } = "SimulatorSigningKey-Change-This-In-Production-12345678";

    /// <summary>
    /// Default token lifetime in minutes.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// List of allowed roles that can be assigned to simulated tokens.
    /// </summary>
    public List<string> AllowedRoles { get; set; } = new()
    {
        "Owner",
        "Contributor", 
        "Reader",
        "Search Service Contributor",
        "Search Index Data Contributor",
        "Search Index Data Reader"
    };

    /// <summary>
    /// List of allowed application IDs for simulated tokens.
    /// </summary>
    public List<string> AllowedAppIds { get; set; } = new() { "test-app-1", "test-app-2" };
}

/// <summary>
/// Role mapping settings for RBAC matching Azure AI Search built-in roles.
/// </summary>
public class RoleMappingSettings
{
    /// <summary>
    /// Owner role - full control plane + Search Service Contributor data access
    /// Role ID: 8e3af657-a8ff-443c-a75c-2fe8c4bcb635
    /// </summary>
    public List<string> OwnerRoles { get; set; } = new()
    {
        "Owner",
        "8e3af657-a8ff-443c-a75c-2fe8c4bcb635"
    };

    /// <summary>
    /// Contributor role - same as Owner minus role assignment
    /// Role ID: b24988ac-6180-42a0-ab88-20f7382dd24c
    /// </summary>
    public List<string> ContributorRoles { get; set; } = new()
    {
        "Contributor",
        "b24988ac-6180-42a0-ab88-20f7382dd24c"
    };

    /// <summary>
    /// Reader role - read service info, metrics, object definitions (no content access)
    /// Role ID: acdd72a7-3385-48ef-bd42-f606fba81ae7
    /// </summary>
    public List<string> ReaderRoles { get; set; } = new()
    {
        "Reader",
        "acdd72a7-3385-48ef-bd42-f606fba81ae7"
    };

    /// <summary>
    /// Search Service Contributor - manage indexes, indexers, skillsets, etc. (no query/index data)
    /// Role ID: 7ca78c08-252a-4471-8644-bb5ff32d4ba0
    /// </summary>
    public List<string> ServiceContributorRoles { get; set; } = new()
    {
        "Search Service Contributor",
        "7ca78c08-252a-4471-8644-bb5ff32d4ba0"
    };

    /// <summary>
    /// Search Index Data Contributor - load/index documents, run indexing jobs
    /// Role ID: 8ebe5a00-799e-43f5-93ac-243d3dce84a7
    /// </summary>
    public List<string> IndexDataContributorRoles { get; set; } = new()
    {
        "Search Index Data Contributor",
        "8ebe5a00-799e-43f5-93ac-243d3dce84a7"
    };

    /// <summary>
    /// Search Index Data Reader - query indexes only (search, autocomplete, suggest)
    /// Role ID: 1407120a-92aa-4202-b7e9-c0e197c71c8f
    /// </summary>
    public List<string> IndexDataReaderRoles { get; set; } = new()
    {
        "Search Index Data Reader",
        "1407120a-92aa-4202-b7e9-c0e197c71c8f"
    };
}
