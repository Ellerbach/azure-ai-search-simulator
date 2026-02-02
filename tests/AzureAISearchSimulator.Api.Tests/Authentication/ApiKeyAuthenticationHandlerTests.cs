using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for ApiKeyAuthenticationHandler.
/// </summary>
public class ApiKeyAuthenticationHandlerTests
{
    private readonly Mock<ILogger<ApiKeyAuthenticationHandler>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AuthenticationSettings>> _authSettingsMock;
    private readonly Mock<IOptionsMonitor<SimulatorSettings>> _simulatorSettingsMock;
    private readonly ApiKeyAuthenticationHandler _handler;

    private const string ValidAdminKey = "admin-key-12345";
    private const string ValidQueryKey = "query-key-67890";

    public ApiKeyAuthenticationHandlerTests()
    {
        _loggerMock = new Mock<ILogger<ApiKeyAuthenticationHandler>>();
        _authSettingsMock = new Mock<IOptionsMonitor<AuthenticationSettings>>();
        _simulatorSettingsMock = new Mock<IOptionsMonitor<SimulatorSettings>>();

        // Setup default configuration
        var authSettings = new AuthenticationSettings
        {
            ApiKey = new ApiKeySettings
            {
                AdminApiKey = null, // Fall back to SimulatorSettings
                QueryApiKey = null
            }
        };
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(authSettings);

        var simulatorSettings = new SimulatorSettings
        {
            AdminApiKey = ValidAdminKey,
            QueryApiKey = ValidQueryKey
        };
        _simulatorSettingsMock.Setup(x => x.CurrentValue).Returns(simulatorSettings);

        _handler = new ApiKeyAuthenticationHandler(
            _loggerMock.Object,
            _authSettingsMock.Object,
            _simulatorSettingsMock.Object);
    }

    [Fact]
    public void AuthenticationMode_ReturnsApiKey()
    {
        Assert.Equal("ApiKey", _handler.AuthenticationMode);
    }

    [Fact]
    public void Priority_ReturnsZero()
    {
        // API key should have highest priority (lowest number)
        Assert.Equal(0, _handler.Priority);
    }

    #region CanHandle Tests

    [Fact]
    public void CanHandle_WithApiKeyHeader_ReturnsTrue()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidAdminKey);
        Assert.True(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithApiKeyQueryParameter_ReturnsTrue()
    {
        var context = CreateHttpContext(apiKeyQuery: ValidAdminKey);
        Assert.True(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithoutApiKey_ReturnsFalse()
    {
        var context = CreateHttpContext();
        Assert.False(_handler.CanHandle(context));
    }

    [Fact]
    public void CanHandle_WithBearerToken_ReturnsFalse()
    {
        var context = CreateHttpContext();
        context.Request.Headers.Authorization = "Bearer some-token";
        Assert.False(_handler.CanHandle(context));
    }

    #endregion

    #region AuthenticateAsync Tests - Admin Key

    [Fact]
    public async Task AuthenticateAsync_WithValidAdminKey_ReturnsSuccess()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidAdminKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("ApiKey", result.AuthenticationMode);
        Assert.Equal("ApiKey", result.IdentityType);
        Assert.Equal("admin", result.IdentityId);
        Assert.Equal("Admin API Key", result.IdentityName);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidAdminKey_HasCorrectRoles()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidAdminKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.Contains("Owner", result.Roles);
        Assert.Contains("Search Service Contributor", result.Roles);
        Assert.Contains("Search Index Data Contributor", result.Roles);
        Assert.Contains("Search Index Data Reader", result.Roles);
    }

    #endregion

    #region AuthenticateAsync Tests - Query Key

    [Fact]
    public async Task AuthenticateAsync_WithValidQueryKey_ReturnsSuccess()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidQueryKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal("ApiKey", result.AuthenticationMode);
        Assert.Equal("ApiKey", result.IdentityType);
        Assert.Equal("query", result.IdentityId);
        Assert.Equal("Query API Key", result.IdentityName);
        Assert.Equal(AccessLevel.IndexDataReader, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidQueryKey_HasCorrectRoles()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidQueryKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.Single(result.Roles);
        Assert.Contains("Search Index Data Reader", result.Roles);
    }

    #endregion

    #region AuthenticateAsync Tests - Invalid Keys

    [Fact]
    public async Task AuthenticateAsync_WithInvalidKey_ReturnsFailure()
    {
        var context = CreateHttpContext(apiKeyHeader: "invalid-key");

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("InvalidApiKey", result.ErrorCode);
        Assert.Equal("ApiKey", result.AuthenticationMode);
        Assert.Equal(AccessLevel.None, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_WithEmptyKey_ReturnsFailure()
    {
        var context = CreateHttpContext(apiKeyHeader: "");

        var result = await _handler.AuthenticateAsync(context);

        Assert.False(result.IsAuthenticated);
        Assert.Equal("MissingApiKey", result.ErrorCode);
    }

    #endregion

    #region AuthenticateAsync Tests - Query Parameter

    [Fact]
    public async Task AuthenticateAsync_WithValidAdminKeyInQueryParameter_ReturnsSuccess()
    {
        var context = CreateHttpContext(apiKeyQuery: ValidAdminKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_HeaderTakesPrecedenceOverQueryParameter()
    {
        var context = CreateHttpContext(apiKeyHeader: ValidAdminKey, apiKeyQuery: ValidQueryKey);

        var result = await _handler.AuthenticateAsync(context);

        // Header should win - admin key in header
        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    #endregion

    #region Configuration Override Tests

    [Fact]
    public async Task AuthenticateAsync_UsesAuthenticationSettingsWhenConfigured()
    {
        // Override the auth settings with specific keys
        var customAdminKey = "custom-admin-key";
        var authSettings = new AuthenticationSettings
        {
            ApiKey = new ApiKeySettings
            {
                AdminApiKey = customAdminKey,
                QueryApiKey = "custom-query-key"
            }
        };
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(authSettings);

        var context = CreateHttpContext(apiKeyHeader: customAdminKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_FallsBackToSimulatorSettingsWhenAuthKeyIsNull()
    {
        // Auth settings have null keys - should fall back to simulator settings
        var authSettings = new AuthenticationSettings
        {
            ApiKey = new ApiKeySettings
            {
                AdminApiKey = null,
                QueryApiKey = null
            }
        };
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(authSettings);

        var context = CreateHttpContext(apiKeyHeader: ValidAdminKey);

        var result = await _handler.AuthenticateAsync(context);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext(
        string? apiKeyHeader = null,
        string? apiKeyQuery = null)
    {
        var context = new DefaultHttpContext();

        if (apiKeyHeader != null)
        {
            context.Request.Headers["api-key"] = apiKeyHeader;
        }

        if (apiKeyQuery != null)
        {
            context.Request.QueryString = new QueryString($"?api-key={apiKeyQuery}");
        }

        // Mock the connection
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        return context;
    }

    #endregion
}
