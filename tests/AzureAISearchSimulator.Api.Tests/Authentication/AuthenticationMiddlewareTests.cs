using AzureAISearchSimulator.Api.Middleware;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for AuthenticationMiddleware.
/// </summary>
public class AuthenticationMiddlewareTests
{
    private readonly Mock<ILogger<AuthenticationMiddleware>> _loggerMock;

    public AuthenticationMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<AuthenticationMiddleware>>();
    }

    #region Skip Authentication Tests

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/swagger")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/scalar")]
    [InlineData("/favicon.ico")]
    [InlineData("/openapi")]
    [InlineData("/")]
    public async Task InvokeAsync_SkipsAuthenticationForPublicEndpoints(string path)
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var handlers = new List<IAuthenticationHandler>();
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: path);
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.True(nextCalled, $"Next should be called for path: {path}");
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    #endregion

    #region API Key Authentication Tests

    [Fact]
    public async Task InvokeAsync_WithValidApiKey_CallsNextMiddleware()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess);

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "admin-key");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.True(nextCalled);
        Assert.NotEqual(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidApiKey_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: false,
            errorCode: "InvalidApiKey",
            errorMessage: "Invalid API key");

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "invalid-key");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    #endregion

    #region No Credentials Tests

    [Fact]
    public async Task InvokeAsync_WithNoCredentials_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(canHandle: false);

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    #endregion

    #region API Key Precedence Tests

    [Fact]
    public async Task InvokeAsync_WhenApiKeyTakesPrecedence_UsesApiKeyFirst()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var apiKeyHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess);

        var bearerHandler = CreateMockBearerHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.IndexDataReader);

        var handlers = new List<IAuthenticationHandler> 
        { 
            apiKeyHandler.Object, 
            bearerHandler.Object 
        };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "admin-key");
        context.Request.Headers.Authorization = "Bearer some-token";
        var settings = CreateAuthSettings(apiKeyTakesPrecedence: true);

        await middleware.InvokeAsync(context, settings);

        Assert.True(nextCalled);
        // Verify API key handler was used (should have FullAccess)
        Assert.Equal(AccessLevel.FullAccess, context.Items["AccessLevel"]);
    }

    #endregion

    #region Handler Priority Tests

    [Fact]
    public async Task InvokeAsync_HandlersAreOrderedByPriority()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Create handlers with different priorities
        var lowPriorityHandler = CreateMockHandler(
            mode: "LowPriority",
            priority: 100,
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.IndexDataReader);

        var highPriorityHandler = CreateMockHandler(
            mode: "HighPriority",
            priority: 0,
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess);

        // Add in reverse order to verify sorting works
        var handlers = new List<IAuthenticationHandler>
        {
            lowPriorityHandler.Object,
            highPriorityHandler.Object
        };

        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes");
        context.Request.Headers["test-auth"] = "test";
        
        var settings = CreateAuthSettings(enabledModes: new[] { "HighPriority", "LowPriority" });

        await middleware.InvokeAsync(context, settings);

        Assert.True(nextCalled);
        // Should use high priority handler (FullAccess)
        Assert.Equal(AccessLevel.FullAccess, context.Items["AccessLevel"]);
    }

    #endregion

    #region Context Items Tests

    [Fact]
    public async Task InvokeAsync_SetsAuthenticationContextItems()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess,
            identityId: "admin",
            identityName: "Admin API Key",
            identityType: "ApiKey");

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "admin-key");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.True(nextCalled);
        Assert.NotNull(context.Items["AuthResult"]);
        Assert.Equal("ApiKey", context.Items["IdentityType"]);
        Assert.Equal("admin", context.Items["IdentityId"]);
        Assert.Equal("Admin API Key", context.Items["IdentityName"]);
        Assert.Equal(AccessLevel.FullAccess, context.Items["AccessLevel"]);
        Assert.Equal("ApiKey", context.Items["AuthMode"]);
    }

    [Fact]
    public async Task InvokeAsync_SetsApiKeyTypeForBackwardCompatibility()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess);

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "admin-key");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        // For backward compatibility with existing code
        Assert.Equal("Admin", context.Items["ApiKeyType"]);
    }

    [Fact]
    public async Task InvokeAsync_WithQueryAccess_SetsQueryApiKeyType()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.IndexDataReader);

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes/test/docs/search", apiKeyHeader: "query-key");
        var settings = CreateAuthSettings();

        await middleware.InvokeAsync(context, settings);

        Assert.Equal("Query", context.Items["ApiKeyType"]);
    }

    #endregion

    #region Disabled Handler Tests

    [Fact]
    public async Task InvokeAsync_SkipsDisabledHandlers()
    {
        var nextCalled = false;
        RequestDelegate next = (HttpContext ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var mockHandler = CreateMockApiKeyHandler(
            canHandle: true,
            isAuthenticated: true,
            accessLevel: AccessLevel.FullAccess);

        var handlers = new List<IAuthenticationHandler> { mockHandler.Object };
        var middleware = new AuthenticationMiddleware(next, _loggerMock.Object, handlers);

        var context = CreateHttpContext(path: "/indexes", apiKeyHeader: "admin-key");
        
        // ApiKey not in enabled modes
        var settings = CreateAuthSettings(enabledModes: new[] { "EntraId" });

        await middleware.InvokeAsync(context, settings);

        // Handler should be skipped, resulting in 401
        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext(
        string path = "/",
        string? apiKeyHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (apiKeyHeader != null)
        {
            context.Request.Headers["api-key"] = apiKeyHeader;
        }

        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        return context;
    }

    private static IOptionsSnapshot<AuthenticationSettings> CreateAuthSettings(
        bool apiKeyTakesPrecedence = true,
        string[]? enabledModes = null)
    {
        var settings = new AuthenticationSettings
        {
            EnabledModes = (enabledModes ?? new[] { "ApiKey" }).ToList(),
            ApiKeyTakesPrecedence = apiKeyTakesPrecedence
        };

        var mock = new Mock<IOptionsSnapshot<AuthenticationSettings>>();
        mock.Setup(x => x.Value).Returns(settings);
        return mock.Object;
    }

    private static Mock<IAuthenticationHandler> CreateMockApiKeyHandler(
        bool canHandle,
        bool isAuthenticated = false,
        AccessLevel accessLevel = AccessLevel.None,
        string? errorCode = null,
        string? errorMessage = null,
        string identityId = "admin",
        string identityName = "Admin API Key",
        string identityType = "ApiKey")
    {
        var mock = new Mock<IAuthenticationHandler>();
        mock.Setup(x => x.AuthenticationMode).Returns("ApiKey");
        mock.Setup(x => x.Priority).Returns(0);
        mock.Setup(x => x.CanHandle(It.IsAny<HttpContext>())).Returns(canHandle);

        var result = isAuthenticated
            ? AuthenticationResult.Success("ApiKey", identityType, identityId, identityName, accessLevel)
            : AuthenticationResult.Failure(errorCode ?? "Error", errorMessage ?? "Error", "ApiKey");

        mock.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        return mock;
    }

    private static Mock<IAuthenticationHandler> CreateMockBearerHandler(
        bool canHandle,
        bool isAuthenticated = false,
        AccessLevel accessLevel = AccessLevel.None)
    {
        var mock = new Mock<IAuthenticationHandler>();
        mock.Setup(x => x.AuthenticationMode).Returns("EntraId");
        mock.Setup(x => x.Priority).Returns(10);
        mock.Setup(x => x.CanHandle(It.IsAny<HttpContext>())).Returns(canHandle);

        var result = isAuthenticated
            ? AuthenticationResult.Success("EntraId", "ServicePrincipal", "sp-id", "Test App", accessLevel)
            : AuthenticationResult.Failure("InvalidToken", "Invalid token", "EntraId");

        mock.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        return mock;
    }

    private static Mock<IAuthenticationHandler> CreateMockHandler(
        string mode,
        int priority,
        bool canHandle,
        bool isAuthenticated = false,
        AccessLevel accessLevel = AccessLevel.None)
    {
        var mock = new Mock<IAuthenticationHandler>();
        mock.Setup(x => x.AuthenticationMode).Returns(mode);
        mock.Setup(x => x.Priority).Returns(priority);
        mock.Setup(x => x.CanHandle(It.IsAny<HttpContext>())).Returns(canHandle);

        var result = isAuthenticated
            ? AuthenticationResult.Success(mode, "Test", "id", "name", accessLevel)
            : AuthenticationResult.Failure("Error", "Error", mode);

        mock.Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        return mock;
    }

    #endregion
}
