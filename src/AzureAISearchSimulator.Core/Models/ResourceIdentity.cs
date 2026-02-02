using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents an identity configuration for Azure resources.
/// Matches the Azure AI Search identity model for data sources, indexers, and skills.
/// </summary>
public class ResourceIdentity
{
    /// <summary>
    /// The OData type of the identity.
    /// </summary>
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }

    /// <summary>
    /// The resource ID of a user-assigned managed identity.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}
    /// </summary>
    [JsonPropertyName("userAssignedIdentity")]
    public string? UserAssignedIdentity { get; set; }

    /// <summary>
    /// Indicates no identity should be used (use connection string or anonymous).
    /// </summary>
    [JsonIgnore]
    public bool IsNone => ODataType == ResourceIdentityTypes.None;

    /// <summary>
    /// Indicates a user-assigned managed identity should be used.
    /// </summary>
    [JsonIgnore]
    public bool IsUserAssigned => ODataType == ResourceIdentityTypes.UserAssignedIdentity;

    /// <summary>
    /// Indicates the system-assigned managed identity should be used.
    /// When ODataType is null and UserAssignedIdentity is null, system-assigned is assumed.
    /// </summary>
    [JsonIgnore]
    public bool IsSystemAssigned => string.IsNullOrEmpty(ODataType) && string.IsNullOrEmpty(UserAssignedIdentity);

    /// <summary>
    /// Creates a resource identity for no identity (use connection string).
    /// </summary>
    public static ResourceIdentity None() => new() { ODataType = ResourceIdentityTypes.None };

    /// <summary>
    /// Creates a resource identity for system-assigned managed identity.
    /// </summary>
    public static ResourceIdentity SystemAssigned() => new();

    /// <summary>
    /// Creates a resource identity for user-assigned managed identity.
    /// </summary>
    /// <param name="resourceId">The resource ID of the user-assigned identity.</param>
    public static ResourceIdentity UserAssigned(string resourceId) => new()
    {
        ODataType = ResourceIdentityTypes.UserAssignedIdentity,
        UserAssignedIdentity = resourceId
    };
}

/// <summary>
/// OData type constants for resource identities.
/// </summary>
public static class ResourceIdentityTypes
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
/// OData type constants for cognitive services accounts.
/// </summary>
public static class CognitiveServicesAccountTypes
{
    /// <summary>
    /// Cognitive Services using API key.
    /// </summary>
    public const string ByKey = "#Microsoft.Azure.Search.CognitiveServicesByKey";

    /// <summary>
    /// AI Services using API key.
    /// </summary>
    public const string AIServicesByKey = "#Microsoft.Azure.Search.AIServicesByKey";

    /// <summary>
    /// AI Services using managed identity.
    /// </summary>
    public const string AIServicesByIdentity = "#Microsoft.Azure.Search.AIServicesByIdentity";
}
