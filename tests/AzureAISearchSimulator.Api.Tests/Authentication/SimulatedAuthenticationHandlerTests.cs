using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for SimulatedAuthenticationHandler.
/// </summary>
public class SimulatedAuthenticationHandlerTests
{
    private readonly Mock<ILogger<SimulatedAuthenticationHandler>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AuthenticationSettings>> _authSettingsMock;
    private readonly Mock<ISimulatedTokenService> _tokenServiceMock;
    private readonly SimulatedAuthenticationHandler _handler;

    public SimulatedAuthenticationHandlerTests()
    {
        _loggerMock = new Mock<ILogger<SimulatedAuthenticationHandler>>();
        _authSettingsMock = new Mock<IOptionsMonitor<AuthenticationSettings>>();
        _tokenServiceMock = new Mock<ISimulatedTokenService>();

        var settings = CreateDefaultSettings();
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        _handler = new SimulatedAuthenticationHandler(
            _loggerMock.Object,
            _authSettingsMock.Object,
            _tokenServiceMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void AuthenticationMode_ReturnsSimulated()
    {
        Assert.Equal("Simulated", _handler.AuthenticationMode);
    }

    [Fact]
    public void Priority_Returns10()
    {
        // Should be after API key (0) but before real Entra ID
        Assert.Equal(10, _handler.Priority);
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
    public void CanHandle_WithBearerToken_WhenDisabled_ReturnsFalse()
    {
        var settings = CreateDefaultSettings();
        settings.Simulated.Enabled = false;
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

    #endregion

    #region AuthenticateAsync Tests - Success Cases

    [Fact]
    public async Task AuthenticateAsync_WithValidToken_ReturnsSuccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            TenantId = "test-tenant-id",
            AppId = "test-app-id",
            Name = "Test App",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Search Index Data Reader" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("Simulated", result.AuthenticationMode);
        Assert.Equal("ServicePrincipal", result.IdentityType);
        Assert.Equal("test-object-id", result.IdentityId);
        Assert.Equal("Test App", result.IdentityName);
        Assert.Equal("test-tenant-id", result.TenantId);
        Assert.Equal("test-app-id", result.ApplicationId);
    }

    [Fact]
    public async Task AuthenticateAsync_WithOwnerRole_ReturnsFullAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Owner" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithContributorRole_ReturnsContributorAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Contributor" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.Contributor, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithServiceContributorRole_ReturnsServiceContributorAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Search Service Contributor" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.ServiceContributor, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithDataContributorRole_ReturnsIndexDataContributorAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Search Index Data Contributor" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.IndexDataContributor, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithDataReaderRole_ReturnsIndexDataReaderAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Search Index Data Reader" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.IndexDataReader, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithReaderRole_ReturnsReaderAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string> { "Reader" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.Reader, result.AccessLevel);
    }

    #endregion

    #region AuthenticateAsync Tests - Failure Cases

    [Fact]
    public async Task AuthenticateAsync_WhenDisabled_ReturnsFailure()
    {
        var settings = CreateDefaultSettings();
        settings.Simulated.Enabled = false;
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var context = CreateHttpContext(bearerToken: "some-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("SimulatedAuthDisabled", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidToken_ReturnsFailure()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = false,
            Error = "Token is invalid",
            ErrorCode = "InvalidToken"
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "invalid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("InvalidToken", result.ErrorCode);
        Assert.Equal("Simulated", result.AuthenticationMode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithExpiredToken_ReturnsFailure()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = false,
            Error = "Token has expired",
            ErrorCode = "TokenExpired"
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "expired-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("TokenExpired", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingAuthHeader_ReturnsFailure()
    {
        var context = CreateHttpContext(); // No auth header

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("MissingToken", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithEmptyBearerToken_ReturnsFailure()
    {
        var context = CreateHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

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

    #endregion

    #region Role Mapping Tests

    [Fact]
    public async Task AuthenticateAsync_WithNoRoles_ReturnsNoneAccess()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            Roles = new List<string>() // No roles
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithRoleGuid_MapsCorrectly()
    {
        var validationResult = new TokenValidationResult
        {
            IsValid = true,
            ObjectId = "test-object-id",
            IdentityType = "ServicePrincipal",
            // Use the role GUID for Owner
            Roles = new List<string> { "8e3af657-a8ff-443c-a75c-2fe8c4bcb635" }
        };
        _tokenServiceMock.Setup(x => x.ValidateToken(It.IsAny<string>()))
            .Returns(validationResult);

        var context = CreateHttpContext(bearerToken: "valid-token");

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext(string? bearerToken = null)
    {
        var context = new DefaultHttpContext();

        if (bearerToken != null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        }

        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        return context;
    }

    private static AuthenticationSettings CreateDefaultSettings()
    {
        return new AuthenticationSettings
        {
            Simulated = new SimulatedAuthSettings
            {
                Enabled = true,
                Issuer = "https://simulator.local/",
                Audience = "https://search.azure.com",
                SigningKey = "SimulatorSigningKey-Change-This-In-Production-12345678"
            },
            RoleMapping = new RoleMappingSettings()
        };
    }

    #endregion
}
