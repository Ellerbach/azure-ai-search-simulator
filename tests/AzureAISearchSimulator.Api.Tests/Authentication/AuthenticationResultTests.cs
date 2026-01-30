using AzureAISearchSimulator.Core.Services.Authentication;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for AuthenticationResult model.
/// </summary>
public class AuthenticationResultTests
{
    #region Success Factory Method Tests

    [Fact]
    public void Success_CreatesAuthenticatedResult()
    {
        var result = AuthenticationResult.Success(
            authenticationMode: "ApiKey",
            identityType: "ApiKey",
            identityId: "admin",
            identityName: "Admin API Key",
            accessLevel: AccessLevel.FullAccess);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("ApiKey", result.AuthenticationMode);
        Assert.Equal("ApiKey", result.IdentityType);
        Assert.Equal("admin", result.IdentityId);
        Assert.Equal("Admin API Key", result.IdentityName);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Success_WithRoles_IncludesRoles()
    {
        var roles = new List<string> { "Owner", "Search Index Data Reader" };
        
        var result = AuthenticationResult.Success(
            authenticationMode: "EntraId",
            identityType: "ServicePrincipal",
            identityId: "00000000-0000-0000-0000-000000000001",
            identityName: "TestApp",
            accessLevel: AccessLevel.FullAccess,
            roles: roles);

        Assert.Equal(2, result.Roles.Count);
        Assert.Contains("Owner", result.Roles);
        Assert.Contains("Search Index Data Reader", result.Roles);
    }

    [Fact]
    public void Success_WithoutRoles_HasEmptyRolesList()
    {
        var result = AuthenticationResult.Success(
            authenticationMode: "ApiKey",
            identityType: "ApiKey",
            identityId: "admin",
            identityName: "Admin",
            accessLevel: AccessLevel.FullAccess);

        Assert.NotNull(result.Roles);
        Assert.Empty(result.Roles);
    }

    #endregion

    #region Failure Factory Method Tests

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        var result = AuthenticationResult.Failure(
            errorCode: "InvalidApiKey",
            errorMessage: "The provided API key is not valid.");

        Assert.False(result.IsAuthenticated);
        Assert.Equal("InvalidApiKey", result.ErrorCode);
        Assert.Equal("The provided API key is not valid.", result.ErrorMessage);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
    }

    [Fact]
    public void Failure_WithAuthenticationMode_IncludesMode()
    {
        var result = AuthenticationResult.Failure(
            errorCode: "TokenExpired",
            errorMessage: "The token has expired.",
            authenticationMode: "EntraId");

        Assert.False(result.IsAuthenticated);
        Assert.Equal("EntraId", result.AuthenticationMode);
    }

    [Fact]
    public void Failure_HasEmptyRolesList()
    {
        var result = AuthenticationResult.Failure("Error", "Test error");

        Assert.NotNull(result.Roles);
        Assert.Empty(result.Roles);
    }

    #endregion

    #region NoCredentials Factory Method Tests

    [Fact]
    public void NoCredentials_CreatesExpectedResult()
    {
        var result = AuthenticationResult.NoCredentials();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("NoCredentials", result.ErrorCode);
        Assert.Equal("No authentication credentials were provided.", result.ErrorMessage);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
        Assert.Null(result.AuthenticationMode);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void TenantId_CanBeSet()
    {
        var result = AuthenticationResult.Success(
            authenticationMode: "EntraId",
            identityType: "User",
            identityId: "user-id",
            identityName: "Test User",
            accessLevel: AccessLevel.IndexDataReader);
        
        result.TenantId = "tenant-id-123";

        Assert.Equal("tenant-id-123", result.TenantId);
    }

    [Fact]
    public void ApplicationId_CanBeSet()
    {
        var result = AuthenticationResult.Success(
            authenticationMode: "EntraId",
            identityType: "ServicePrincipal",
            identityId: "sp-id",
            identityName: "Test App",
            accessLevel: AccessLevel.ServiceContributor);
        
        result.ApplicationId = "app-id-456";

        Assert.Equal("app-id-456", result.ApplicationId);
    }

    [Fact]
    public void Scopes_CanBePopulated()
    {
        var result = new AuthenticationResult
        {
            IsAuthenticated = true,
            Scopes = new List<string> { "user_impersonation", "read_all" }
        };

        Assert.Equal(2, result.Scopes.Count);
        Assert.Contains("user_impersonation", result.Scopes);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultConstructor_SetsCorrectDefaults()
    {
        var result = new AuthenticationResult();

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.IdentityType);
        Assert.Null(result.IdentityId);
        Assert.Null(result.IdentityName);
        Assert.Null(result.TenantId);
        Assert.Null(result.ApplicationId);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
        Assert.NotNull(result.Roles);
        Assert.Empty(result.Roles);
        Assert.NotNull(result.Scopes);
        Assert.Empty(result.Scopes);
        Assert.Null(result.AuthenticationMode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorCode);
    }

    #endregion
}
