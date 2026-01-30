using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;

namespace AzureAISearchSimulator.Api.Services.Authorization;

/// <summary>
/// Service for checking authorization based on authentication result.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if the current request is authorized for the specified operation.
    /// </summary>
    /// <param name="context">HTTP context with authentication result.</param>
    /// <param name="operation">The operation being performed.</param>
    /// <returns>Authorization result.</returns>
    AuthorizationResult Authorize(HttpContext context, SearchOperation operation);

    /// <summary>
    /// Gets the authentication result from the HTTP context.
    /// </summary>
    AuthenticationResult? GetAuthenticationResult(HttpContext context);

    /// <summary>
    /// Checks if the access level is sufficient for the operation.
    /// </summary>
    bool IsAuthorized(AccessLevel accessLevel, SearchOperation operation);
}

/// <summary>
/// Operations that can be performed on Azure AI Search.
/// </summary>
public enum SearchOperation
{
    // Query operations (IndexDataReader)
    Search,
    Suggest,
    Autocomplete,
    Lookup,
    Count,

    // Document operations (IndexDataContributor)
    UploadDocuments,
    MergeDocuments,
    DeleteDocuments,

    // Index management (ServiceContributor)
    CreateIndex,
    UpdateIndex,
    DeleteIndex,
    ListIndexes,
    GetIndex,
    GetIndexStatistics,

    // Indexer management (ServiceContributor)
    CreateIndexer,
    UpdateIndexer,
    DeleteIndexer,
    ListIndexers,
    GetIndexer,
    RunIndexer,
    ResetIndexer,
    GetIndexerStatus,

    // Data source management (ServiceContributor)
    CreateDataSource,
    UpdateDataSource,
    DeleteDataSource,
    ListDataSources,
    GetDataSource,

    // Skillset management (ServiceContributor)
    CreateSkillset,
    UpdateSkillset,
    DeleteSkillset,
    ListSkillsets,
    GetSkillset,

    // Synonym map management (ServiceContributor)
    CreateSynonymMap,
    UpdateSynonymMap,
    DeleteSynonymMap,
    ListSynonymMaps,
    GetSynonymMap,

    // Service operations (Reader and above)
    GetServiceStatistics,
    GetServiceInfo,

    // Admin operations (FullAccess)
    ManageApiKeys,
    GenerateToken
}

/// <summary>
/// Result of an authorization check.
/// </summary>
public class AuthorizationResult
{
    public bool IsAuthorized { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public AccessLevel RequiredAccessLevel { get; set; }
    public AccessLevel ActualAccessLevel { get; set; }

    public static AuthorizationResult Allowed(AccessLevel actual)
    {
        return new AuthorizationResult
        {
            IsAuthorized = true,
            ActualAccessLevel = actual
        };
    }

    public static AuthorizationResult Denied(AccessLevel required, AccessLevel actual, string? message = null)
    {
        return new AuthorizationResult
        {
            IsAuthorized = false,
            ErrorCode = "Forbidden",
            ErrorMessage = message ?? $"Insufficient permissions. Required: {required}, Actual: {actual}",
            RequiredAccessLevel = required,
            ActualAccessLevel = actual
        };
    }

    public static AuthorizationResult Unauthenticated()
    {
        return new AuthorizationResult
        {
            IsAuthorized = false,
            ErrorCode = "Unauthorized",
            ErrorMessage = "Authentication required.",
            ActualAccessLevel = AccessLevel.None
        };
    }
}

/// <summary>
/// Implementation of the authorization service.
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(ILogger<AuthorizationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public AuthorizationResult Authorize(HttpContext context, SearchOperation operation)
    {
        var authResult = GetAuthenticationResult(context);
        
        if (authResult == null || !authResult.IsAuthenticated)
        {
            return AuthorizationResult.Unauthenticated();
        }

        var accessLevel = authResult.AccessLevel;
        var isAuthorized = IsAuthorized(accessLevel, operation);

        if (isAuthorized)
        {
            return AuthorizationResult.Allowed(accessLevel);
        }

        var requiredLevel = GetRequiredAccessLevel(operation);
        
        _logger.LogWarning(
            "Authorization denied for {Operation}. Identity: {Identity}, AccessLevel: {AccessLevel}, Required: {Required}",
            operation, authResult.IdentityId, accessLevel, requiredLevel);

        return AuthorizationResult.Denied(
            requiredLevel, 
            accessLevel,
            GetDeniedMessage(operation, requiredLevel));
    }

    /// <inheritdoc />
    public AuthenticationResult? GetAuthenticationResult(HttpContext context)
    {
        if (context.Items.TryGetValue("AuthResult", out var result) && result is AuthenticationResult authResult)
        {
            return authResult;
        }
        return null;
    }

    /// <inheritdoc />
    public bool IsAuthorized(AccessLevel accessLevel, SearchOperation operation)
    {
        // Full access can do anything
        if (accessLevel == AccessLevel.FullAccess)
        {
            return true;
        }

        var requiredLevel = GetRequiredAccessLevel(operation);

        return operation switch
        {
            // Query operations - need IndexDataReader or higher
            SearchOperation.Search or
            SearchOperation.Suggest or
            SearchOperation.Autocomplete or
            SearchOperation.Lookup or
            SearchOperation.Count => accessLevel.CanQuery(),

            // Document operations - need IndexDataContributor or higher
            SearchOperation.UploadDocuments or
            SearchOperation.MergeDocuments or
            SearchOperation.DeleteDocuments => accessLevel.CanModifyDocuments(),

            // Index management - need ServiceContributor or higher
            SearchOperation.CreateIndex or
            SearchOperation.UpdateIndex or
            SearchOperation.DeleteIndex or
            SearchOperation.ListIndexes or
            SearchOperation.GetIndex or
            SearchOperation.GetIndexStatistics => accessLevel.CanManageIndexes(),

            // Indexer management
            SearchOperation.CreateIndexer or
            SearchOperation.UpdateIndexer or
            SearchOperation.DeleteIndexer or
            SearchOperation.ListIndexers or
            SearchOperation.GetIndexer or
            SearchOperation.RunIndexer or
            SearchOperation.ResetIndexer or
            SearchOperation.GetIndexerStatus => accessLevel.CanManageIndexers(),

            // Data source management
            SearchOperation.CreateDataSource or
            SearchOperation.UpdateDataSource or
            SearchOperation.DeleteDataSource or
            SearchOperation.ListDataSources or
            SearchOperation.GetDataSource => accessLevel.CanManageDataSources(),

            // Skillset management
            SearchOperation.CreateSkillset or
            SearchOperation.UpdateSkillset or
            SearchOperation.DeleteSkillset or
            SearchOperation.ListSkillsets or
            SearchOperation.GetSkillset => accessLevel.CanManageSkillsets(),

            // Synonym map management
            SearchOperation.CreateSynonymMap or
            SearchOperation.UpdateSynonymMap or
            SearchOperation.DeleteSynonymMap or
            SearchOperation.ListSynonymMaps or
            SearchOperation.GetSynonymMap => accessLevel.CanManageIndexes(),

            // Service info - need Reader or higher
            SearchOperation.GetServiceStatistics or
            SearchOperation.GetServiceInfo => accessLevel.CanReadServiceInfo(),

            // Admin operations - need FullAccess
            SearchOperation.ManageApiKeys or
            SearchOperation.GenerateToken => accessLevel.IsAdmin(),

            _ => false
        };
    }

    private static AccessLevel GetRequiredAccessLevel(SearchOperation operation)
    {
        return operation switch
        {
            // Query operations
            SearchOperation.Search or
            SearchOperation.Suggest or
            SearchOperation.Autocomplete or
            SearchOperation.Lookup or
            SearchOperation.Count => AccessLevel.IndexDataReader,

            // Document operations
            SearchOperation.UploadDocuments or
            SearchOperation.MergeDocuments or
            SearchOperation.DeleteDocuments => AccessLevel.IndexDataContributor,

            // Management operations
            SearchOperation.CreateIndex or
            SearchOperation.UpdateIndex or
            SearchOperation.DeleteIndex or
            SearchOperation.ListIndexes or
            SearchOperation.GetIndex or
            SearchOperation.GetIndexStatistics or
            SearchOperation.CreateIndexer or
            SearchOperation.UpdateIndexer or
            SearchOperation.DeleteIndexer or
            SearchOperation.ListIndexers or
            SearchOperation.GetIndexer or
            SearchOperation.RunIndexer or
            SearchOperation.ResetIndexer or
            SearchOperation.GetIndexerStatus or
            SearchOperation.CreateDataSource or
            SearchOperation.UpdateDataSource or
            SearchOperation.DeleteDataSource or
            SearchOperation.ListDataSources or
            SearchOperation.GetDataSource or
            SearchOperation.CreateSkillset or
            SearchOperation.UpdateSkillset or
            SearchOperation.DeleteSkillset or
            SearchOperation.ListSkillsets or
            SearchOperation.GetSkillset or
            SearchOperation.CreateSynonymMap or
            SearchOperation.UpdateSynonymMap or
            SearchOperation.DeleteSynonymMap or
            SearchOperation.ListSynonymMaps or
            SearchOperation.GetSynonymMap => AccessLevel.ServiceContributor,

            // Service info
            SearchOperation.GetServiceStatistics or
            SearchOperation.GetServiceInfo => AccessLevel.Reader,

            // Admin operations
            SearchOperation.ManageApiKeys or
            SearchOperation.GenerateToken => AccessLevel.FullAccess,

            _ => AccessLevel.FullAccess
        };
    }

    private static string GetDeniedMessage(SearchOperation operation, AccessLevel required)
    {
        var roleNeeded = required switch
        {
            AccessLevel.IndexDataReader => "Search Index Data Reader",
            AccessLevel.IndexDataContributor => "Search Index Data Contributor",
            AccessLevel.ServiceContributor => "Search Service Contributor",
            AccessLevel.Reader => "Reader",
            AccessLevel.Contributor => "Contributor",
            AccessLevel.FullAccess => "Owner",
            _ => required.ToString()
        };

        return $"The operation '{operation}' requires the '{roleNeeded}' role or higher.";
    }
}

/// <summary>
/// Extension methods for authorization.
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Gets the access level from the HTTP context.
    /// </summary>
    public static AccessLevel GetAccessLevel(this HttpContext context)
    {
        if (context.Items.TryGetValue("AccessLevel", out var level) && level is AccessLevel accessLevel)
        {
            return accessLevel;
        }
        return AccessLevel.None;
    }

    /// <summary>
    /// Checks if the request has at least the specified access level.
    /// </summary>
    public static bool HasAccess(this HttpContext context, AccessLevel required)
    {
        var actual = context.GetAccessLevel();
        return actual >= required || actual == AccessLevel.FullAccess;
    }
}
