using Azure.Core;

namespace AzureAISearchSimulator.Core.Services.Credentials;

/// <summary>
/// Factory interface for creating Azure credentials.
/// Provides a unified way to create credentials for different Azure services
/// based on configuration and resource-specific requirements.
/// </summary>
public interface ICredentialFactory
{
    /// <summary>
    /// Gets the default credential configured for the simulator.
    /// </summary>
    /// <returns>A TokenCredential instance.</returns>
    TokenCredential GetDefaultCredential();

    /// <summary>
    /// Gets a credential for a specific resource, optionally using a specific identity.
    /// </summary>
    /// <param name="resourceUri">The resource URI (e.g., storage account URL).</param>
    /// <param name="identity">Optional identity configuration to use.</param>
    /// <returns>A TokenCredential instance.</returns>
    TokenCredential GetCredential(string? resourceUri = null, SearchIdentity? identity = null);

    /// <summary>
    /// Gets a token for a specific scope using the default credential.
    /// </summary>
    /// <param name="scope">The scope to request (e.g., "https://storage.azure.com/.default").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An access token.</returns>
    Task<AccessToken> GetTokenAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a token for a specific scope using a specific identity.
    /// </summary>
    /// <param name="scope">The scope to request.</param>
    /// <param name="identity">Optional identity configuration to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An access token.</returns>
    Task<AccessToken> GetTokenAsync(string scope, SearchIdentity? identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about the current credential configuration.
    /// Useful for diagnostics and debugging.
    /// </summary>
    /// <returns>Credential information.</returns>
    CredentialInfo GetCredentialInfo();

    /// <summary>
    /// Tests if the credential can successfully authenticate to Azure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authentication succeeds.</returns>
    Task<CredentialTestResult> TestCredentialAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an identity configuration for Azure resources.
/// Matches the Azure AI Search identity model.
/// </summary>
public class SearchIdentity
{
    /// <summary>
    /// The OData type of the identity.
    /// </summary>
    public string? ODataType { get; set; }

    /// <summary>
    /// The resource ID of a user-assigned managed identity.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}
    /// </summary>
    public string? UserAssignedIdentity { get; set; }

    /// <summary>
    /// Indicates no identity should be used (use connection string or anonymous).
    /// </summary>
    public bool IsNone => ODataType == SearchIdentityTypes.None;

    /// <summary>
    /// Indicates a user-assigned managed identity should be used.
    /// </summary>
    public bool IsUserAssigned => ODataType == SearchIdentityTypes.UserAssignedIdentity;

    /// <summary>
    /// Indicates the system-assigned managed identity should be used.
    /// </summary>
    public bool IsSystemAssigned => ODataType == null && string.IsNullOrEmpty(UserAssignedIdentity);
}

/// <summary>
/// OData type constants for search identities.
/// </summary>
public static class SearchIdentityTypes
{
    /// <summary>
    /// No identity - use connection string or anonymous access.
    /// </summary>
    public const string None = "#Microsoft.Azure.Search.DataNone";

    /// <summary>
    /// User-assigned managed identity.
    /// </summary>
    public const string UserAssignedIdentity = "#Microsoft.Azure.Search.DataUserAssignedIdentity";
}

/// <summary>
/// Information about the configured credential.
/// </summary>
public class CredentialInfo
{
    /// <summary>
    /// The type of credential being used.
    /// </summary>
    public string CredentialType { get; set; } = "Unknown";

    /// <summary>
    /// The tenant ID if applicable.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The client ID if applicable.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Managed identity resource ID if using user-assigned identity.
    /// </summary>
    public string? ManagedIdentityResourceId { get; set; }

    /// <summary>
    /// Additional details about the credential configuration.
    /// </summary>
    public Dictionary<string, string> Details { get; set; } = new();
}

/// <summary>
/// Result of testing a credential.
/// </summary>
public class CredentialTestResult
{
    /// <summary>
    /// Whether the credential test succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The type of credential that was used.
    /// </summary>
    public string? CredentialType { get; set; }

    /// <summary>
    /// The scope that was tested.
    /// </summary>
    public string? TestedScope { get; set; }

    /// <summary>
    /// When the token expires.
    /// </summary>
    public DateTimeOffset? TokenExpiration { get; set; }

    /// <summary>
    /// Error message if the test failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Time taken to acquire the token.
    /// </summary>
    public TimeSpan? ElapsedTime { get; set; }

    /// <summary>
    /// Creates a successful test result.
    /// </summary>
    public static CredentialTestResult Succeeded(string credentialType, DateTimeOffset expiration, TimeSpan elapsed, string scope)
    {
        return new CredentialTestResult
        {
            Success = true,
            CredentialType = credentialType,
            TokenExpiration = expiration,
            ElapsedTime = elapsed,
            TestedScope = scope
        };
    }

    /// <summary>
    /// Creates a failed test result.
    /// </summary>
    public static CredentialTestResult Failed(string error, TimeSpan elapsed)
    {
        return new CredentialTestResult
        {
            Success = false,
            Error = error,
            ElapsedTime = elapsed
        };
    }
}
