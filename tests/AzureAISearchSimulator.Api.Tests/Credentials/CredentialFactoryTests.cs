using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Credentials;
using AzureAISearchSimulator.DataSources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Credentials;

/// <summary>
/// Unit tests for CredentialFactory.
/// </summary>
public class CredentialFactoryTests
{
    private readonly Mock<ILogger<CredentialFactory>> _loggerMock;
    private readonly Mock<IOptionsMonitor<OutboundAuthenticationSettings>> _settingsMock;
    private readonly IMemoryCache _cache;
    private readonly CredentialFactory _factory;

    public CredentialFactoryTests()
    {
        _loggerMock = new Mock<ILogger<CredentialFactory>>();
        _settingsMock = new Mock<IOptionsMonitor<OutboundAuthenticationSettings>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        var settings = CreateDefaultSettings();
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        _factory = new CredentialFactory(
            _loggerMock.Object,
            _settingsMock.Object,
            _cache);
    }

    #region GetDefaultCredential Tests

    [Fact]
    public void GetDefaultCredential_ReturnsCredential()
    {
        var credential = _factory.GetDefaultCredential();

        Assert.NotNull(credential);
    }

    [Fact]
    public void GetDefaultCredential_CachesCredential()
    {
        var credential1 = _factory.GetDefaultCredential();
        var credential2 = _factory.GetDefaultCredential();

        Assert.Same(credential1, credential2);
    }

    [Fact]
    public void GetDefaultCredential_WithDefaultAzureCredential_CreatesCorrectType()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredentialType = "DefaultAzureCredential";
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var credential = _factory.GetDefaultCredential();

        Assert.NotNull(credential);
        Assert.Contains("DefaultAzureCredential", credential.GetType().Name);
    }

    [Fact]
    public void GetDefaultCredential_WithManagedIdentity_CreatesCorrectType()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredentialType = "ManagedIdentity";
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var factory = new CredentialFactory(_loggerMock.Object, _settingsMock.Object, _cache);
        var credential = factory.GetDefaultCredential();

        Assert.NotNull(credential);
        Assert.Contains("ManagedIdentityCredential", credential.GetType().Name);
    }

    [Fact]
    public void GetDefaultCredential_WithServicePrincipal_NoSecrets_ThrowsException()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredentialType = "ServicePrincipal";
        settings.ServicePrincipal = new ServicePrincipalSettings
        {
            TenantId = "test-tenant",
            ClientId = "test-client"
            // No secret or certificate
        };
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var factory = new CredentialFactory(_loggerMock.Object, _settingsMock.Object, _cache);

        Assert.Throws<InvalidOperationException>(() => factory.GetDefaultCredential());
    }

    #endregion

    #region GetCredential Tests

    [Fact]
    public void GetCredential_WithNoIdentity_ReturnsDefaultCredential()
    {
        var credential = _factory.GetCredential();

        Assert.NotNull(credential);
    }

    [Fact]
    public void GetCredential_WithNoneIdentity_ThrowsException()
    {
        var identity = new SearchIdentity
        {
            ODataType = SearchIdentityTypes.None
        };

        Assert.Throws<InvalidOperationException>(() => _factory.GetCredential(identity: identity));
    }

    [Fact]
    public void GetCredential_WithUserAssignedIdentity_ReturnsManagedIdentityCredential()
    {
        var identity = new SearchIdentity
        {
            ODataType = SearchIdentityTypes.UserAssignedIdentity,
            UserAssignedIdentity = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-identity"
        };

        var credential = _factory.GetCredential(identity: identity);

        Assert.NotNull(credential);
        Assert.Contains("ManagedIdentityCredential", credential.GetType().Name);
    }

    [Fact]
    public void GetCredential_WithSystemAssignedIdentity_ReturnsManagedIdentityCredential()
    {
        var identity = new SearchIdentity(); // No ODataType = system assigned

        var credential = _factory.GetCredential(identity: identity);

        Assert.NotNull(credential);
        Assert.Contains("ManagedIdentityCredential", credential.GetType().Name);
    }

    #endregion

    #region GetCredentialInfo Tests

    [Fact]
    public void GetCredentialInfo_ReturnsCorrectType()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredentialType = "DefaultAzureCredential";
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var info = _factory.GetCredentialInfo();

        Assert.Equal("DefaultAzureCredential", info.CredentialType);
    }

    [Fact]
    public void GetCredentialInfo_IncludesTokenCacheStatus()
    {
        var info = _factory.GetCredentialInfo();

        Assert.Contains("TokenCacheEnabled", info.Details.Keys);
    }

    [Fact]
    public void GetCredentialInfo_MasksSensitiveData()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredentialType = "ServicePrincipal";
        settings.ServicePrincipal = new ServicePrincipalSettings
        {
            TenantId = "12345678-1234-1234-1234-123456789012",
            ClientId = "abcdefgh-abcd-abcd-abcd-abcdefghijkl",
            ClientSecret = "super-secret"
        };
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var factory = new CredentialFactory(_loggerMock.Object, _settingsMock.Object, _cache);
        var info = factory.GetCredentialInfo();

        // Tenant and Client IDs should be masked
        Assert.Contains("...", info.TenantId);
        Assert.Contains("...", info.ClientId);
    }

    #endregion

    #region SearchIdentity Tests

    [Fact]
    public void SearchIdentity_IsNone_ReturnsTrueForNoneType()
    {
        var identity = new SearchIdentity
        {
            ODataType = SearchIdentityTypes.None
        };

        Assert.True(identity.IsNone);
        Assert.False(identity.IsUserAssigned);
    }

    [Fact]
    public void SearchIdentity_IsUserAssigned_ReturnsTrueForUserAssignedType()
    {
        var identity = new SearchIdentity
        {
            ODataType = SearchIdentityTypes.UserAssignedIdentity,
            UserAssignedIdentity = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-identity"
        };

        Assert.True(identity.IsUserAssigned);
        Assert.False(identity.IsNone);
    }

    [Fact]
    public void SearchIdentity_IsSystemAssigned_ReturnsTrueForDefaultIdentity()
    {
        var identity = new SearchIdentity();

        Assert.True(identity.IsSystemAssigned);
        Assert.False(identity.IsNone);
        Assert.False(identity.IsUserAssigned);
    }

    #endregion

    #region CredentialTestResult Tests

    [Fact]
    public void CredentialTestResult_Succeeded_CreatesSuccessResult()
    {
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var elapsed = TimeSpan.FromMilliseconds(150);

        var result = CredentialTestResult.Succeeded("DefaultAzureCredential", expiration, elapsed, "https://test/.default");

        Assert.True(result.Success);
        Assert.Equal("DefaultAzureCredential", result.CredentialType);
        Assert.Equal(expiration, result.TokenExpiration);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.Null(result.Error);
    }

    [Fact]
    public void CredentialTestResult_Failed_CreatesFailureResult()
    {
        var elapsed = TimeSpan.FromMilliseconds(500);

        var result = CredentialTestResult.Failed("Authentication failed", elapsed);

        Assert.False(result.Success);
        Assert.Equal("Authentication failed", result.Error);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.Null(result.CredentialType);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void DefaultCredentialSettings_ExcludedCredentials_AreRespected()
    {
        var settings = CreateDefaultSettings();
        settings.DefaultCredential.ExcludeAzureCliCredential = true;
        settings.DefaultCredential.ExcludeAzurePowerShellCredential = true;
        _settingsMock.Setup(x => x.CurrentValue).Returns(settings);

        var factory = new CredentialFactory(_loggerMock.Object, _settingsMock.Object, _cache);
        var info = factory.GetCredentialInfo();

        Assert.Contains("AzureCli", info.Details["ExcludedCredentials"]);
        Assert.Contains("AzurePowerShell", info.Details["ExcludedCredentials"]);
    }

    [Fact]
    public void TokenCacheSettings_DefaultValues_AreCorrect()
    {
        var settings = new TokenCacheSettings();

        Assert.True(settings.Enabled);
        Assert.Equal(5, settings.RefreshBeforeExpirationMinutes);
        Assert.Equal(100, settings.MaxCacheSize);
    }

    [Fact]
    public void ManagedIdentitySettings_DefaultValues_AreCorrect()
    {
        var settings = new ManagedIdentitySettings();

        Assert.True(settings.Enabled);
        Assert.Null(settings.ClientId);
        Assert.Null(settings.ResourceId);
    }

    #endregion

    #region Helper Methods

    private static OutboundAuthenticationSettings CreateDefaultSettings()
    {
        return new OutboundAuthenticationSettings
        {
            DefaultCredentialType = "DefaultAzureCredential",
            ServicePrincipal = new ServicePrincipalSettings(),
            ManagedIdentity = new ManagedIdentitySettings(),
            TokenCache = new TokenCacheSettings(),
            DefaultCredential = new DefaultCredentialSettings
            {
                ExcludeInteractiveBrowserCredential = true
            }
        };
    }

    #endregion
}
