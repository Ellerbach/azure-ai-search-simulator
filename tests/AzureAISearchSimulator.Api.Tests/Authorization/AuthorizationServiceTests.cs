using AzureAISearchSimulator.Api.Services.Authorization;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Authorization;

/// <summary>
/// Unit tests for AuthorizationService.
/// </summary>
public class AuthorizationServiceTests
{
    private readonly Mock<ILogger<AuthorizationService>> _loggerMock;
    private readonly AuthorizationService _service;

    public AuthorizationServiceTests()
    {
        _loggerMock = new Mock<ILogger<AuthorizationService>>();
        _service = new AuthorizationService(_loggerMock.Object);
    }

    #region Query Operations Tests

    [Theory]
    [InlineData(AccessLevel.IndexDataReader)]
    [InlineData(AccessLevel.IndexDataContributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_QueryOperations_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.Search));
        Assert.True(_service.IsAuthorized(level, SearchOperation.Suggest));
        Assert.True(_service.IsAuthorized(level, SearchOperation.Autocomplete));
        Assert.True(_service.IsAuthorized(level, SearchOperation.Lookup));
        Assert.True(_service.IsAuthorized(level, SearchOperation.Count));
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.Reader)]
    [InlineData(AccessLevel.ServiceContributor)]
    public void IsAuthorized_QueryOperations_DeniedForWrongLevels(AccessLevel level)
    {
        Assert.False(_service.IsAuthorized(level, SearchOperation.Search));
        Assert.False(_service.IsAuthorized(level, SearchOperation.Suggest));
    }

    #endregion

    #region Document Operations Tests

    [Theory]
    [InlineData(AccessLevel.IndexDataContributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_DocumentOperations_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.UploadDocuments));
        Assert.True(_service.IsAuthorized(level, SearchOperation.MergeDocuments));
        Assert.True(_service.IsAuthorized(level, SearchOperation.DeleteDocuments));
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.IndexDataReader)]
    [InlineData(AccessLevel.Reader)]
    [InlineData(AccessLevel.ServiceContributor)]
    public void IsAuthorized_DocumentOperations_DeniedForWrongLevels(AccessLevel level)
    {
        Assert.False(_service.IsAuthorized(level, SearchOperation.UploadDocuments));
        Assert.False(_service.IsAuthorized(level, SearchOperation.MergeDocuments));
    }

    #endregion

    #region Index Management Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor)]
    [InlineData(AccessLevel.Contributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_IndexManagement_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.CreateIndex));
        Assert.True(_service.IsAuthorized(level, SearchOperation.UpdateIndex));
        Assert.True(_service.IsAuthorized(level, SearchOperation.DeleteIndex));
        Assert.True(_service.IsAuthorized(level, SearchOperation.ListIndexes));
        Assert.True(_service.IsAuthorized(level, SearchOperation.GetIndex));
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.IndexDataReader)]
    [InlineData(AccessLevel.IndexDataContributor)]
    [InlineData(AccessLevel.Reader)]
    public void IsAuthorized_IndexManagement_DeniedForWrongLevels(AccessLevel level)
    {
        Assert.False(_service.IsAuthorized(level, SearchOperation.CreateIndex));
        Assert.False(_service.IsAuthorized(level, SearchOperation.DeleteIndex));
    }

    #endregion

    #region Indexer Management Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor)]
    [InlineData(AccessLevel.Contributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_IndexerManagement_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.CreateIndexer));
        Assert.True(_service.IsAuthorized(level, SearchOperation.RunIndexer));
        Assert.True(_service.IsAuthorized(level, SearchOperation.ResetIndexer));
        Assert.True(_service.IsAuthorized(level, SearchOperation.GetIndexerStatus));
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.IndexDataReader)]
    [InlineData(AccessLevel.IndexDataContributor)]
    public void IsAuthorized_IndexerManagement_DeniedForWrongLevels(AccessLevel level)
    {
        Assert.False(_service.IsAuthorized(level, SearchOperation.CreateIndexer));
        Assert.False(_service.IsAuthorized(level, SearchOperation.RunIndexer));
    }

    #endregion

    #region Data Source Management Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor)]
    [InlineData(AccessLevel.Contributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_DataSourceManagement_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.CreateDataSource));
        Assert.True(_service.IsAuthorized(level, SearchOperation.UpdateDataSource));
        Assert.True(_service.IsAuthorized(level, SearchOperation.DeleteDataSource));
    }

    #endregion

    #region Skillset Management Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor)]
    [InlineData(AccessLevel.Contributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_SkillsetManagement_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.CreateSkillset));
        Assert.True(_service.IsAuthorized(level, SearchOperation.UpdateSkillset));
        Assert.True(_service.IsAuthorized(level, SearchOperation.DeleteSkillset));
    }

    #endregion

    #region Service Info Tests

    [Theory]
    [InlineData(AccessLevel.Reader)]
    [InlineData(AccessLevel.ServiceContributor)]
    [InlineData(AccessLevel.Contributor)]
    [InlineData(AccessLevel.FullAccess)]
    public void IsAuthorized_ServiceInfo_AllowedForCorrectLevels(AccessLevel level)
    {
        Assert.True(_service.IsAuthorized(level, SearchOperation.GetServiceStatistics));
        Assert.True(_service.IsAuthorized(level, SearchOperation.GetServiceInfo));
    }

    [Theory]
    [InlineData(AccessLevel.None)]
    [InlineData(AccessLevel.IndexDataReader)]
    [InlineData(AccessLevel.IndexDataContributor)]
    public void IsAuthorized_ServiceInfo_DeniedForWrongLevels(AccessLevel level)
    {
        Assert.False(_service.IsAuthorized(level, SearchOperation.GetServiceStatistics));
        Assert.False(_service.IsAuthorized(level, SearchOperation.GetServiceInfo));
    }

    #endregion

    #region Admin Operations Tests

    [Fact]
    public void IsAuthorized_AdminOperations_OnlyAllowedForFullAccess()
    {
        Assert.True(_service.IsAuthorized(AccessLevel.FullAccess, SearchOperation.ManageApiKeys));
        Assert.True(_service.IsAuthorized(AccessLevel.FullAccess, SearchOperation.GenerateToken));
        
        Assert.False(_service.IsAuthorized(AccessLevel.Contributor, SearchOperation.ManageApiKeys));
        Assert.False(_service.IsAuthorized(AccessLevel.ServiceContributor, SearchOperation.GenerateToken));
    }

    #endregion

    #region FullAccess Tests

    [Fact]
    public void IsAuthorized_FullAccess_AllowsEverything()
    {
        var allOperations = Enum.GetValues<SearchOperation>();
        
        foreach (var operation in allOperations)
        {
            Assert.True(_service.IsAuthorized(AccessLevel.FullAccess, operation), 
                $"FullAccess should allow {operation}");
        }
    }

    #endregion

    #region Authorize Method Tests

    [Fact]
    public void Authorize_WithAuthenticatedRequest_ReturnsCorrectResult()
    {
        var authResult = AuthenticationResult.Success(
            "ApiKey", "ApiKey", "admin", "Admin Key", AccessLevel.FullAccess);
        
        var context = CreateHttpContextWithAuth(authResult);

        var result = _service.Authorize(context, SearchOperation.CreateIndex);

        Assert.True(result.IsAuthorized);
        Assert.Equal(AccessLevel.FullAccess, result.ActualAccessLevel);
    }

    [Fact]
    public void Authorize_WithInsufficientPermissions_ReturnsDenied()
    {
        var authResult = AuthenticationResult.Success(
            "Simulated", "ServicePrincipal", "sp-id", "Test SP", AccessLevel.IndexDataReader);
        
        var context = CreateHttpContextWithAuth(authResult);

        var result = _service.Authorize(context, SearchOperation.CreateIndex);

        Assert.False(result.IsAuthorized);
        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.Equal(AccessLevel.ServiceContributor, result.RequiredAccessLevel);
        Assert.Equal(AccessLevel.IndexDataReader, result.ActualAccessLevel);
    }

    [Fact]
    public void Authorize_WithNoAuthentication_ReturnsUnauthenticated()
    {
        var context = new DefaultHttpContext();

        var result = _service.Authorize(context, SearchOperation.Search);

        Assert.False(result.IsAuthorized);
        Assert.Equal("Unauthorized", result.ErrorCode);
    }

    [Fact]
    public void Authorize_WithFailedAuthentication_ReturnsUnauthenticated()
    {
        var authResult = AuthenticationResult.Failure("InvalidKey", "Invalid API key");
        var context = CreateHttpContextWithAuth(authResult);

        var result = _service.Authorize(context, SearchOperation.Search);

        Assert.False(result.IsAuthorized);
        Assert.Equal("Unauthorized", result.ErrorCode);
    }

    #endregion

    #region GetAuthenticationResult Tests

    [Fact]
    public void GetAuthenticationResult_WithAuthResult_ReturnsResult()
    {
        var authResult = AuthenticationResult.Success(
            "ApiKey", "ApiKey", "admin", "Admin", AccessLevel.FullAccess);
        
        var context = CreateHttpContextWithAuth(authResult);

        var result = _service.GetAuthenticationResult(context);

        Assert.NotNull(result);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void GetAuthenticationResult_WithoutAuthResult_ReturnsNull()
    {
        var context = new DefaultHttpContext();

        var result = _service.GetAuthenticationResult(context);

        Assert.Null(result);
    }

    #endregion

    #region AuthorizationResult Tests

    [Fact]
    public void AuthorizationResult_Allowed_CreatesCorrectResult()
    {
        var result = AuthorizationResult.Allowed(AccessLevel.FullAccess);

        Assert.True(result.IsAuthorized);
        Assert.Null(result.ErrorCode);
        Assert.Equal(AccessLevel.FullAccess, result.ActualAccessLevel);
    }

    [Fact]
    public void AuthorizationResult_Denied_CreatesCorrectResult()
    {
        var result = AuthorizationResult.Denied(
            AccessLevel.ServiceContributor, 
            AccessLevel.IndexDataReader);

        Assert.False(result.IsAuthorized);
        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.Equal(AccessLevel.ServiceContributor, result.RequiredAccessLevel);
        Assert.Equal(AccessLevel.IndexDataReader, result.ActualAccessLevel);
        Assert.Contains("Insufficient permissions", result.ErrorMessage);
    }

    [Fact]
    public void AuthorizationResult_Unauthenticated_CreatesCorrectResult()
    {
        var result = AuthorizationResult.Unauthenticated();

        Assert.False(result.IsAuthorized);
        Assert.Equal("Unauthorized", result.ErrorCode);
        Assert.Equal(AccessLevel.None, result.ActualAccessLevel);
    }

    #endregion

    #region Extension Methods Tests

    [Fact]
    public void GetAccessLevel_WithAccessLevelInContext_ReturnsLevel()
    {
        var context = new DefaultHttpContext();
        context.Items["AccessLevel"] = AccessLevel.ServiceContributor;

        var level = context.GetAccessLevel();

        Assert.Equal(AccessLevel.ServiceContributor, level);
    }

    [Fact]
    public void GetAccessLevel_WithoutAccessLevelInContext_ReturnsNone()
    {
        var context = new DefaultHttpContext();

        var level = context.GetAccessLevel();

        Assert.Equal(AccessLevel.None, level);
    }

    [Fact]
    public void HasAccess_WithSufficientLevel_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Items["AccessLevel"] = AccessLevel.ServiceContributor;

        Assert.True(context.HasAccess(AccessLevel.IndexDataReader));
        Assert.True(context.HasAccess(AccessLevel.ServiceContributor));
    }

    [Fact]
    public void HasAccess_WithInsufficientLevel_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Items["AccessLevel"] = AccessLevel.IndexDataReader;

        Assert.False(context.HasAccess(AccessLevel.ServiceContributor));
        Assert.False(context.HasAccess(AccessLevel.FullAccess));
    }

    [Fact]
    public void HasAccess_WithFullAccess_AlwaysReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Items["AccessLevel"] = AccessLevel.FullAccess;

        var allLevels = Enum.GetValues<AccessLevel>();
        foreach (var level in allLevels)
        {
            Assert.True(context.HasAccess(level));
        }
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContextWithAuth(AuthenticationResult authResult)
    {
        var context = new DefaultHttpContext();
        context.Items["AuthResult"] = authResult;
        context.Items["AccessLevel"] = authResult.AccessLevel;
        return context;
    }

    #endregion
}
