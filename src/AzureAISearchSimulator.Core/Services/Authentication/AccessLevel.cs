namespace AzureAISearchSimulator.Core.Services.Authentication;

/// <summary>
/// Access levels matching Azure AI Search RBAC model.
/// These levels represent the effective permissions for an authenticated identity.
/// </summary>
public enum AccessLevel
{
    /// <summary>
    /// No access - authentication failed or no permissions assigned.
    /// </summary>
    None = 0,

    /// <summary>
    /// Search Index Data Reader - Query indexes only (search, suggest, autocomplete).
    /// Maps to role: 1407120a-92aa-4202-b7e9-c0e197c71c8f
    /// </summary>
    IndexDataReader = 10,

    /// <summary>
    /// Reader - Read service info, metrics, object definitions (no content access).
    /// Maps to role: acdd72a7-3385-48ef-bd42-f606fba81ae7
    /// </summary>
    Reader = 20,

    /// <summary>
    /// Search Index Data Contributor - Load documents + query.
    /// Maps to role: 8ebe5a00-799e-43f5-93ac-243d3dce84a7
    /// </summary>
    IndexDataContributor = 30,

    /// <summary>
    /// Search Service Contributor - Manage indexes, indexers, skillsets (no query/index data).
    /// Maps to role: 7ca78c08-252a-4471-8644-bb5ff32d4ba0
    /// </summary>
    ServiceContributor = 40,

    /// <summary>
    /// Contributor - Same as full access minus role assignment.
    /// Maps to role: b24988ac-6180-42a0-ab88-20f7382dd24c
    /// </summary>
    Contributor = 50,

    /// <summary>
    /// Full access (Owner + all data roles via API key Admin).
    /// Maps to role: 8e3af657-a8ff-443c-a75c-2fe8c4bcb635
    /// </summary>
    FullAccess = 100
}

/// <summary>
/// Extension methods for AccessLevel.
/// </summary>
public static class AccessLevelExtensions
{
    /// <summary>
    /// Checks if the access level can perform query operations.
    /// </summary>
    public static bool CanQuery(this AccessLevel level)
    {
        return level is AccessLevel.IndexDataReader or 
                        AccessLevel.IndexDataContributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can perform document operations (upload/merge/delete).
    /// </summary>
    public static bool CanModifyDocuments(this AccessLevel level)
    {
        return level is AccessLevel.IndexDataContributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can perform index management operations.
    /// </summary>
    public static bool CanManageIndexes(this AccessLevel level)
    {
        return level is AccessLevel.ServiceContributor or 
                        AccessLevel.Contributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can perform indexer operations.
    /// </summary>
    public static bool CanManageIndexers(this AccessLevel level)
    {
        return level is AccessLevel.ServiceContributor or 
                        AccessLevel.Contributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can perform skillset operations.
    /// </summary>
    public static bool CanManageSkillsets(this AccessLevel level)
    {
        return level is AccessLevel.ServiceContributor or 
                        AccessLevel.Contributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can perform data source operations.
    /// </summary>
    public static bool CanManageDataSources(this AccessLevel level)
    {
        return level is AccessLevel.ServiceContributor or 
                        AccessLevel.Contributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level can read service information.
    /// </summary>
    public static bool CanReadServiceInfo(this AccessLevel level)
    {
        return level is AccessLevel.Reader or 
                        AccessLevel.ServiceContributor or 
                        AccessLevel.Contributor or 
                        AccessLevel.FullAccess;
    }

    /// <summary>
    /// Checks if the access level has full admin capabilities.
    /// </summary>
    public static bool IsAdmin(this AccessLevel level)
    {
        return level == AccessLevel.FullAccess;
    }
}
