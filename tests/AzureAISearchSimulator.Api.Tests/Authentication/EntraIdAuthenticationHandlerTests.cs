using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for EntraIdAuthenticationHandler.
/// </summary>
public class EntraIdAuthenticationHandlerTests
{
    private readonly Mock<ILogger<EntraIdAuthenticationHandler>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AuthenticationSettings>> _authSettingsMock;
    private readonly Mock<IEntraIdTokenValidator> _tokenValidatorMock;
    private readonly EntraIdAuthenticationHandler _handler;

    public EntraIdAuthenticationHandlerTests()
    {
        _loggerMock = new Mock<ILogger<EntraIdAuthenticationHandler>>();
        _authSettingsMock = new Mock<IOptionsMonitor<AuthenticationSettings>>();
        _tokenValidatorMock = new Mock<IEntraIdTokenValidator>();

        var settings = CreateDefaultSettings();
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        _handler = new EntraIdAuthenticationHandler(
            _loggerMock.Object,
            _authSettingsMock.Object,
            _tokenValidatorMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void AuthenticationMode_ReturnsEntraId()
    {
        Assert.Equal("EntraId", _handler.AuthenticationMode);
    }

    [Fact]
    public void Priority_Returns20()
    {
        // Should be after API key (0) and Simulated (10)
        Assert.Equal(20, _handler.Priority);
    }

    #endregion

    #region CanHandle Tests

    [Fact]
    public void CanHandle_WithBearerToken_WhenEnabled_ReturnsTrue()
    {
        var context = CreateHttpContext(bearerToken: "some-token");

        Assert.True(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WhenNotInEnabledModes_ReturnsFalse()
    {
        var settings = CreateDefaultSettings();
        settings.EnabledModes = new List<string> { "ApiKey", "Simulated" };
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var context = CreateHttpContext(bearerToken: "some-token");

        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WhenTenantIdNotConfigured_ReturnsFalse()
    {
        var settings = CreateDefaultSettings();
        settings.EntraId.TenantId = "";
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var context = CreateHttpContext(bearerToken: "some-token");

        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithoutBearerToken_ReturnsFalse()
    {
        var context = CreateHttpContext();

        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithApiKeyOnly_ReturnsFalse()
    {
        var context = CreateHttpContext();
        context.Request.Headers["api-key"] = "admin-key";

        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithNonBearerAuth_ReturnsFalse()
    {
        var context = CreateHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_CaseInsensitiveEntraIdMode()
    {
        var settings = CreateDefaultSettings();
        settings.EnabledModes = new List<string> { "entraID" }; // lowercase
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var context = CreateHttpContext(bearerToken: "some-token");

        Assert.True(_handler.CanHandle(context));
    }

    #endregion

    #region AuthenticateAsync Tests

    [Fact]
    public async Task AuthenticateAsync_WithValidToken_ReturnsSuccess()
    {
        var context = CreateHttpContext(bearerToken: "valid-token");
        var validationResult = CreateValidationResult(
            objectId: "user-oid",
            tenantId: "test-tenant",
            appId: "test-app",
            identityType: "User",
            name: "Test User",
            roles: new List<string> { "Search Index Data Reader" });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("valid-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("EntraId", result.AuthenticationMode);
        Assert.Equal("User", result.IdentityType);
        Assert.Equal("user-oid", result.IdentityId);
        Assert.Equal("Test User", result.IdentityName);
        Assert.Equal("test-tenant", result.TenantId);
        Assert.Equal("test-app", result.ApplicationId);
    }

    [Fact]
    public async Task AuthenticateAsync_WithServicePrincipal_ReturnsCorrectIdentityType()
    {
        var context = CreateHttpContext(bearerToken: "sp-token");
        var validationResult = CreateValidationResult(
            objectId: "sp-oid",
            identityType: "ServicePrincipal",
            appId: "my-app-id",
            roles: new List<string> { "Owner" });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("sp-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("ServicePrincipal", result.IdentityType);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingHeader_ReturnsFailure()
    {
        var context = CreateHttpContext();

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("MissingToken", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithNonBearerScheme_ReturnsFailure()
    {
        var context = CreateHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("InvalidScheme", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithEmptyToken_ReturnsFailure()
    {
        var context = CreateHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("MissingToken", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidToken_ReturnsFailure()
    {
        var context = CreateHttpContext(bearerToken: "invalid-token");

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("invalid-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntraIdValidationResult.Failure("InvalidToken", "Token validation failed."));

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("InvalidToken", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithExpiredToken_ReturnsFailure()
    {
        var context = CreateHttpContext(bearerToken: "expired-token");

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("expired-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntraIdValidationResult.Failure("TokenExpired", "The token has expired."));

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("TokenExpired", result.ErrorCode);
    }

    #endregion

    #region Role Mapping Tests

    [Theory]
    [InlineData("Owner", AccessLevel.FullAccess)]
    [InlineData("8e3af657-a8ff-443c-a75c-2fe8c4bcb635", AccessLevel.FullAccess)] // Owner GUID
    [InlineData("Contributor", AccessLevel.Contributor)]
    [InlineData("b24988ac-6180-42a0-ab88-20f7382dd24c", AccessLevel.Contributor)] // Contributor GUID
    [InlineData("Reader", AccessLevel.Reader)]
    [InlineData("acdd72a7-3385-48ef-bd42-f606fba81ae7", AccessLevel.Reader)] // Reader GUID
    [InlineData("Search Service Contributor", AccessLevel.ServiceContributor)]
    [InlineData("7ca78c08-252a-4471-8644-bb5ff32d4ba0", AccessLevel.ServiceContributor)] // Service Contributor GUID
    [InlineData("Search Index Data Contributor", AccessLevel.IndexDataContributor)]
    [InlineData("8ebe5a00-799e-43f5-93ac-243d3dce84a7", AccessLevel.IndexDataContributor)] // Data Contributor GUID
    [InlineData("Search Index Data Reader", AccessLevel.IndexDataReader)]
    [InlineData("1407120a-92aa-4202-b7e9-c0e197c71c8f", AccessLevel.IndexDataReader)] // Data Reader GUID
    public async Task AuthenticateAsync_MapsRoleToCorrectAccessLevel(string role, AccessLevel expectedLevel)
    {
        var context = CreateHttpContext(bearerToken: "role-token");
        var validationResult = CreateValidationResult(roles: new List<string> { role });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("role-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(expectedLevel, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMultipleRoles_UsesHighestAccessLevel()
    {
        var context = CreateHttpContext(bearerToken: "multi-role-token");
        var validationResult = CreateValidationResult(
            roles: new List<string> { "Reader", "Search Index Data Contributor", "Owner" });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("multi-role-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel); // Owner is highest
    }

    [Fact]
    public async Task AuthenticateAsync_WithNoRoles_ReturnsNoneAccessLevel()
    {
        var context = CreateHttpContext(bearerToken: "no-role-token");
        var validationResult = CreateValidationResult(roles: new List<string>());

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("no-role-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithUnknownRole_ReturnsNoneAccessLevel()
    {
        var context = CreateHttpContext(bearerToken: "unknown-role-token");
        var validationResult = CreateValidationResult(roles: new List<string> { "Custom.Admin.Role" });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("unknown-role-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
    }

    #endregion

    #region Scopes Tests

    [Fact]
    public async Task AuthenticateAsync_ExtractsScopes()
    {
        var context = CreateHttpContext(bearerToken: "scoped-token");
        var validationResult = CreateValidationResult(
            scopes: new List<string> { "Search.Read", "Search.Write" });

        _tokenValidatorMock
            .Setup(x => x.ValidateTokenAsync("scoped-token", It.IsAny<EntraIdSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Contains("Search.Read", result.Scopes);
        Assert.Contains("Search.Write", result.Scopes);
    }

    #endregion

    #region Helper Methods

    private static AuthenticationSettings CreateDefaultSettings()
    {
        return new AuthenticationSettings
        {
            EnabledModes = new List<string> { "ApiKey", "EntraId" },
            DefaultMode = "ApiKey",
            ApiKeyTakesPrecedence = true,
            EntraId = new EntraIdSettings
            {
                Instance = "https://login.microsoftonline.com/",
                TenantId = "test-tenant-id",
                ClientId = "test-client-id",
                Audience = "https://search.azure.com",
                RequireHttpsMetadata = true,
                AllowMultipleTenants = false
            },
            RoleMapping = new RoleMappingSettings
            {
                OwnerRoles = new List<string> { "Owner", "8e3af657-a8ff-443c-a75c-2fe8c4bcb635" },
                ContributorRoles = new List<string> { "Contributor", "b24988ac-6180-42a0-ab88-20f7382dd24c" },
                ReaderRoles = new List<string> { "Reader", "acdd72a7-3385-48ef-bd42-f606fba81ae7" },
                ServiceContributorRoles = new List<string> { "Search Service Contributor", "7ca78c08-252a-4471-8644-bb5ff32d4ba0" },
                IndexDataContributorRoles = new List<string> { "Search Index Data Contributor", "8ebe5a00-799e-43f5-93ac-243d3dce84a7" },
                IndexDataReaderRoles = new List<string> { "Search Index Data Reader", "1407120a-92aa-4202-b7e9-c0e197c71c8f" }
            }
        };
    }

    private static DefaultHttpContext CreateHttpContext(string? bearerToken = null)
    {
        var context = new DefaultHttpContext();
        if (bearerToken != null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        }
        return context;
    }

    private static EntraIdValidationResult CreateValidationResult(
        string? objectId = "test-oid",
        string? tenantId = "test-tenant",
        string? appId = "test-app",
        string? identityType = "User",
        string? name = "Test User",
        List<string>? roles = null,
        List<string>? scopes = null)
    {
        var result = new EntraIdValidationResult
        {
            IsValid = true,
            ObjectId = objectId,
            TenantId = tenantId,
            AppId = appId,
            IdentityType = identityType,
            Name = name,
            Roles = roles ?? new List<string> { "Search Index Data Reader" },
            Scopes = scopes ?? new List<string>(),
            Principal = new ClaimsPrincipal(new ClaimsIdentity())
        };
        return result;
    }

    #endregion
}
