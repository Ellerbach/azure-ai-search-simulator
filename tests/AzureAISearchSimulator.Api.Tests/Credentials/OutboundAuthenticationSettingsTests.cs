using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Api.Tests.Credentials;

/// <summary>
/// Unit tests for OutboundAuthenticationSettings configuration.
/// </summary>
public class OutboundAuthenticationSettingsTests
{
    #region OutboundAuthenticationSettings Tests

    [Fact]
    public void OutboundAuthenticationSettings_DefaultValues_AreCorrect()
    {
        var settings = new OutboundAuthenticationSettings();

        Assert.Equal("DefaultAzureCredential", settings.DefaultCredentialType);
        Assert.NotNull(settings.ServicePrincipal);
        Assert.NotNull(settings.ManagedIdentity);
        Assert.NotNull(settings.TokenCache);
        Assert.NotNull(settings.DefaultCredential);
        Assert.False(settings.EnableDetailedLogging);
    }

    [Fact]
    public void OutboundAuthenticationSettings_SectionName_IsCorrect()
    {
        Assert.Equal("OutboundAuthentication", OutboundAuthenticationSettings.SectionName);
    }

    #endregion

    #region ServicePrincipalSettings Tests

    [Fact]
    public void ServicePrincipalSettings_DefaultValues_AreCorrect()
    {
        var settings = new ServicePrincipalSettings();

        Assert.Equal("", settings.TenantId);
        Assert.Equal("", settings.ClientId);
        Assert.Null(settings.ClientSecret);
        Assert.Null(settings.CertificatePath);
        Assert.Null(settings.CertificatePassword);
        Assert.Null(settings.CertificateThumbprint);
        Assert.False(settings.SendCertificateChain);
    }

    [Fact]
    public void ServicePrincipalSettings_CanBeConfigured()
    {
        var settings = new ServicePrincipalSettings
        {
            TenantId = "tenant-123",
            ClientId = "client-456",
            ClientSecret = "secret-789",
            CertificatePath = "/path/to/cert.pfx",
            CertificatePassword = "password",
            CertificateThumbprint = "ABC123",
            SendCertificateChain = true
        };

        Assert.Equal("tenant-123", settings.TenantId);
        Assert.Equal("client-456", settings.ClientId);
        Assert.Equal("secret-789", settings.ClientSecret);
        Assert.Equal("/path/to/cert.pfx", settings.CertificatePath);
        Assert.Equal("password", settings.CertificatePassword);
        Assert.Equal("ABC123", settings.CertificateThumbprint);
        Assert.True(settings.SendCertificateChain);
    }

    #endregion

    #region ManagedIdentitySettings Tests

    [Fact]
    public void ManagedIdentitySettings_DefaultValues_AreCorrect()
    {
        var settings = new ManagedIdentitySettings();

        Assert.True(settings.Enabled);
        Assert.Null(settings.ClientId);
        Assert.Null(settings.ResourceId);
    }

    [Fact]
    public void ManagedIdentitySettings_SystemAssigned_Configuration()
    {
        var settings = new ManagedIdentitySettings
        {
            Enabled = true,
            ClientId = null,
            ResourceId = null
        };

        Assert.True(settings.Enabled);
        Assert.Null(settings.ClientId);
        Assert.Null(settings.ResourceId);
    }

    [Fact]
    public void ManagedIdentitySettings_UserAssignedByClientId_Configuration()
    {
        var settings = new ManagedIdentitySettings
        {
            Enabled = true,
            ClientId = "12345678-1234-1234-1234-123456789012"
        };

        Assert.True(settings.Enabled);
        Assert.Equal("12345678-1234-1234-1234-123456789012", settings.ClientId);
    }

    [Fact]
    public void ManagedIdentitySettings_UserAssignedByResourceId_Configuration()
    {
        var resourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-identity";
        
        var settings = new ManagedIdentitySettings
        {
            Enabled = true,
            ResourceId = resourceId
        };

        Assert.True(settings.Enabled);
        Assert.Equal(resourceId, settings.ResourceId);
    }

    #endregion

    #region TokenCacheSettings Tests

    [Fact]
    public void TokenCacheSettings_DefaultValues_AreCorrect()
    {
        var settings = new TokenCacheSettings();

        Assert.True(settings.Enabled);
        Assert.Equal(5, settings.RefreshBeforeExpirationMinutes);
        Assert.Equal(100, settings.MaxCacheSize);
    }

    [Fact]
    public void TokenCacheSettings_CanBeDisabled()
    {
        var settings = new TokenCacheSettings
        {
            Enabled = false
        };

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void TokenCacheSettings_CanBeCustomized()
    {
        var settings = new TokenCacheSettings
        {
            Enabled = true,
            RefreshBeforeExpirationMinutes = 10,
            MaxCacheSize = 500
        };

        Assert.True(settings.Enabled);
        Assert.Equal(10, settings.RefreshBeforeExpirationMinutes);
        Assert.Equal(500, settings.MaxCacheSize);
    }

    #endregion

    #region DefaultCredentialSettings Tests

    [Fact]
    public void DefaultCredentialSettings_DefaultValues_AreCorrect()
    {
        var settings = new DefaultCredentialSettings();

        Assert.False(settings.ExcludeAzureCliCredential);
        Assert.False(settings.ExcludeAzurePowerShellCredential);
        Assert.False(settings.ExcludeEnvironmentCredential);
        Assert.False(settings.ExcludeManagedIdentityCredential);
        Assert.False(settings.ExcludeVisualStudioCredential);
        Assert.False(settings.ExcludeVisualStudioCodeCredential);
        Assert.False(settings.ExcludeSharedTokenCacheCredential);
        Assert.True(settings.ExcludeInteractiveBrowserCredential); // Default to true
        Assert.Null(settings.TenantId);
        Assert.Null(settings.ManagedIdentityClientId);
    }

    [Fact]
    public void DefaultCredentialSettings_CanExcludeCredentials()
    {
        var settings = new DefaultCredentialSettings
        {
            ExcludeAzureCliCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeManagedIdentityCredential = true
        };

        Assert.True(settings.ExcludeAzureCliCredential);
        Assert.True(settings.ExcludeAzurePowerShellCredential);
        Assert.True(settings.ExcludeManagedIdentityCredential);
    }

    [Fact]
    public void DefaultCredentialSettings_CanSetTenantId()
    {
        var settings = new DefaultCredentialSettings
        {
            TenantId = "my-tenant-id"
        };

        Assert.Equal("my-tenant-id", settings.TenantId);
    }

    [Fact]
    public void DefaultCredentialSettings_CanSetManagedIdentityClientId()
    {
        var settings = new DefaultCredentialSettings
        {
            ManagedIdentityClientId = "12345678-1234-1234-1234-123456789012"
        };

        Assert.Equal("12345678-1234-1234-1234-123456789012", settings.ManagedIdentityClientId);
    }

    #endregion

    #region Credential Types Tests

    [Theory]
    [InlineData("DefaultAzureCredential")]
    [InlineData("ServicePrincipal")]
    [InlineData("ManagedIdentity")]
    [InlineData("ConnectionString")]
    public void OutboundAuthenticationSettings_SupportsValidCredentialTypes(string credentialType)
    {
        var settings = new OutboundAuthenticationSettings
        {
            DefaultCredentialType = credentialType
        };

        Assert.Equal(credentialType, settings.DefaultCredentialType);
    }

    #endregion
}
