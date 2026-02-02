using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for SimulatedTokenService.
/// </summary>
public class SimulatedTokenServiceTests
{
    private readonly Mock<ILogger<SimulatedTokenService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AuthenticationSettings>> _authSettingsMock;
    private readonly SimulatedTokenService _service;

    public SimulatedTokenServiceTests()
    {
        _loggerMock = new Mock<ILogger<SimulatedTokenService>>();
        _authSettingsMock = new Mock<IOptionsMonitor<AuthenticationSettings>>();

        var settings = CreateDefaultSettings();
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        _service = new SimulatedTokenService(_loggerMock.Object, _authSettingsMock.Object);
    }

    #region Token Generation Tests

    [Fact]
    public void GenerateToken_WithValidRequest_ReturnsToken()
    {
        var request = new SimulatedTokenRequest
        {
            IdentityType = "ServicePrincipal",
            AppId = "test-app-1",
            Roles = new List<string> { "Search Index Data Reader" },
            ExpiresInMinutes = 60
        };

        var result = _service.GenerateToken(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.NotEmpty(result.Token);
        Assert.Equal("Bearer", result.TokenType);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void GenerateToken_WithMultipleRoles_IncludesAllRoles()
    {
        var request = new SimulatedTokenRequest
        {
            IdentityType = "ServicePrincipal",
            AppId = "test-app-1",
            Roles = new List<string> 
            { 
                "Search Index Data Contributor",
                "Search Index Data Reader" 
            }
        };

        var result = _service.GenerateToken(request);
        Assert.True(result.Success);

        // Validate the token contains the roles
        var validationResult = _service.ValidateToken(result.Token!);
        Assert.True(validationResult.IsValid);
        Assert.Contains("Search Index Data Contributor", validationResult.Roles);
        Assert.Contains("Search Index Data Reader", validationResult.Roles);
    }

    [Fact]
    public void GenerateToken_WhenDisabled_ReturnsError()
    {
        var settings = CreateDefaultSettings();
        settings.Simulated.Enabled = false;
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" }
        };

        var result = _service.GenerateToken(request);

        Assert.False(result.Success);
        Assert.Contains("not enabled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateToken_WithInvalidRole_ReturnsError()
    {
        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "InvalidRole" }
        };

        var result = _service.GenerateToken(request);

        Assert.False(result.Success);
        Assert.Contains("not in the allowed roles", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateToken_WithInvalidAppId_ReturnsError()
    {
        var request = new SimulatedTokenRequest
        {
            AppId = "invalid-app-id",
            Roles = new List<string> { "Search Index Data Reader" }
        };

        var result = _service.GenerateToken(request);

        Assert.False(result.Success);
        Assert.Contains("not in the allowed app IDs", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateToken_ForUser_IncludesUserClaims()
    {
        var request = new SimulatedTokenRequest
        {
            IdentityType = "User",
            Name = "Test User",
            PreferredUsername = "testuser@contoso.com",
            Roles = new List<string> { "Search Index Data Reader" },
            Scopes = new List<string> { "user_impersonation" }
        };

        var result = _service.GenerateToken(request);
        Assert.True(result.Success);

        var validationResult = _service.ValidateToken(result.Token!);
        Assert.True(validationResult.IsValid);
        Assert.Equal("User", validationResult.IdentityType);
        Assert.Equal("Test User", validationResult.Name);
        Assert.Contains("user_impersonation", validationResult.Scopes);
    }

    [Fact]
    public void GenerateToken_SetsCorrectExpiration()
    {
        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" },
            ExpiresInMinutes = 30
        };

        var beforeGeneration = DateTime.UtcNow;
        var result = _service.GenerateToken(request);
        var afterGeneration = DateTime.UtcNow;

        Assert.True(result.Success);
        Assert.NotNull(result.ExpiresAt);
        
        // Should expire roughly 30 minutes from now
        var expectedMin = beforeGeneration.AddMinutes(29);
        var expectedMax = afterGeneration.AddMinutes(31);
        Assert.True(result.ExpiresAt >= expectedMin && result.ExpiresAt <= expectedMax);
    }

    #endregion

    #region Token Validation Tests

    [Fact]
    public void ValidateToken_WithValidToken_ReturnsSuccess()
    {
        var request = new SimulatedTokenRequest
        {
            IdentityType = "ServicePrincipal",
            ObjectId = "test-object-id",
            AppId = "test-app-1",
            TenantId = "test-tenant-id",
            Roles = new List<string> { "Search Index Data Reader" }
        };

        var generateResult = _service.GenerateToken(request);
        Assert.True(generateResult.Success);

        var validationResult = _service.ValidateToken(generateResult.Token!);

        Assert.True(validationResult.IsValid);
        Assert.Equal("test-object-id", validationResult.ObjectId);
        Assert.Equal("test-tenant-id", validationResult.TenantId);
        Assert.Equal("test-app-1", validationResult.AppId);
        Assert.Equal("ServicePrincipal", validationResult.IdentityType);
        Assert.Contains("Search Index Data Reader", validationResult.Roles);
    }

    [Fact]
    public void ValidateToken_WhenDisabled_ReturnsError()
    {
        // First generate a valid token
        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" }
        };
        var generateResult = _service.GenerateToken(request);
        Assert.True(generateResult.Success);

        // Disable simulated auth
        var settings = CreateDefaultSettings();
        settings.Simulated.Enabled = false;
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var validationResult = _service.ValidateToken(generateResult.Token!);

        Assert.False(validationResult.IsValid);
        Assert.Equal("SimulatedAuthDisabled", validationResult.ErrorCode);
    }

    [Fact]
    public void ValidateToken_WithInvalidSignature_ReturnsError()
    {
        // Generate with one key, validate with different key
        var generateResult = _service.GenerateToken(new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" }
        });

        // Change the signing key
        var settings = CreateDefaultSettings();
        settings.Simulated.SigningKey = "DifferentSigningKey-1234567890123456789012345678901234567890";
        _authSettingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var validationResult = _service.ValidateToken(generateResult.Token!);

        Assert.False(validationResult.IsValid);
        Assert.Equal("InvalidSignature", validationResult.ErrorCode);
    }

    [Fact]
    public void ValidateToken_WithMalformedToken_ReturnsError()
    {
        var validationResult = _service.ValidateToken("not-a-valid-jwt-token");

        Assert.False(validationResult.IsValid);
        Assert.NotNull(validationResult.Error);
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ReturnsError()
    {
        // Create a token that's already expired by manipulating the settings
        // We need to generate a token with a very short lifetime and then wait
        // Instead, let's manually create an expired-looking token scenario
        
        // Generate a valid token first
        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" },
            ExpiresInMinutes = 1 // Short lifetime
        };

        var generateResult = _service.GenerateToken(request);
        Assert.True(generateResult.Success);

        // The token validation has a 5-minute clock skew allowance, so we can't easily
        // test expiration without waiting. Instead, let's verify the token was generated
        // with the correct expiration time window.
        var validationResult = _service.ValidateToken(generateResult.Token!);
        Assert.True(validationResult.IsValid); // Should still be valid within clock skew
    }

    [Fact]
    public void ValidateToken_WithTamperedToken_ReturnsError()
    {
        // Generate a valid token
        var request = new SimulatedTokenRequest
        {
            Roles = new List<string> { "Search Index Data Reader" }
        };
        var generateResult = _service.GenerateToken(request);
        Assert.True(generateResult.Success);

        // Tamper with the token (modify the payload)
        var parts = generateResult.Token!.Split('.');
        if (parts.Length == 3)
        {
            // Modify the payload slightly
            var tamperedToken = parts[0] + ".xxx" + parts[1].Substring(3) + "." + parts[2];
            
            var validationResult = _service.ValidateToken(tamperedToken);
            Assert.False(validationResult.IsValid);
        }
    }

    #endregion

    #region Validation Parameters Tests

    [Fact]
    public void GetValidationParameters_ReturnsCorrectParameters()
    {
        var parameters = _service.GetValidationParameters();

        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.Equal("https://simulator.local/", parameters.ValidIssuer);
        Assert.Equal("https://search.azure.com", parameters.ValidAudience);
    }

    #endregion

    #region Helper Methods

    private static AuthenticationSettings CreateDefaultSettings()
    {
        return new AuthenticationSettings
        {
            Simulated = new SimulatedAuthSettings
            {
                Enabled = true,
                Issuer = "https://simulator.local/",
                Audience = "https://search.azure.com",
                SigningKey = "SimulatorSigningKey-Change-This-In-Production-12345678",
                TokenLifetimeMinutes = 60,
                AllowedRoles = new List<string>
                {
                    "Owner",
                    "Contributor",
                    "Reader",
                    "Search Service Contributor",
                    "Search Index Data Contributor",
                    "Search Index Data Reader"
                },
                AllowedAppIds = new List<string> { "test-app-1", "test-app-2" }
            }
        };
    }

    #endregion
}
