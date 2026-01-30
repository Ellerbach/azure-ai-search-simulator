namespace AzureAISearchSimulator.Core.Services.Authentication;

/// <summary>
/// Represents the result of an authentication attempt.
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// Whether authentication was successful.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// The type of identity that was authenticated.
    /// Values: "User", "ServicePrincipal", "ManagedIdentity", "ApiKey"
    /// </summary>
    public string? IdentityType { get; set; }

    /// <summary>
    /// The unique identifier for the identity (e.g., object ID for Entra ID, key type for API keys).
    /// </summary>
    public string? IdentityId { get; set; }

    /// <summary>
    /// Display name for the identity (e.g., username, app name, or "Admin Key").
    /// </summary>
    public string? IdentityName { get; set; }

    /// <summary>
    /// The tenant ID for Entra ID authentication.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The application (client) ID for service principal authentication.
    /// </summary>
    public string? ApplicationId { get; set; }

    /// <summary>
    /// The determined access level based on authentication and authorization.
    /// </summary>
    public AccessLevel AccessLevel { get; set; } = AccessLevel.None;

    /// <summary>
    /// The list of roles assigned to the identity.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// The list of scopes assigned to the identity (for delegated tokens).
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// The authentication mode that was used.
    /// </summary>
    public string? AuthenticationMode { get; set; }

    /// <summary>
    /// Error message if authentication failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code if authentication failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    public static AuthenticationResult Success(
        string authenticationMode,
        string identityType,
        string identityId,
        string identityName,
        AccessLevel accessLevel,
        List<string>? roles = null)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = true,
            AuthenticationMode = authenticationMode,
            IdentityType = identityType,
            IdentityId = identityId,
            IdentityName = identityName,
            AccessLevel = accessLevel,
            Roles = roles ?? new List<string>()
        };
    }

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    public static AuthenticationResult Failure(string errorCode, string errorMessage, string? authenticationMode = null)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = false,
            AuthenticationMode = authenticationMode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            AccessLevel = AccessLevel.None
        };
    }

    /// <summary>
    /// Creates a result indicating no authentication was attempted (no credentials provided).
    /// </summary>
    public static AuthenticationResult NoCredentials()
    {
        return new AuthenticationResult
        {
            IsAuthenticated = false,
            ErrorCode = "NoCredentials",
            ErrorMessage = "No authentication credentials were provided.",
            AccessLevel = AccessLevel.None
        };
    }
}
