using AzureAISearchSimulator.Core.Models;
using System.Text.Json;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for ResourceIdentity model and related identity types.
/// </summary>
public class ResourceIdentityTests
{
    #region Factory Method Tests

    [Fact]
    public void None_ShouldReturnCorrectIdentity()
    {
        // Act
        var identity = ResourceIdentity.None();

        // Assert
        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityTypes.None, identity.ODataType);
        Assert.Null(identity.UserAssignedIdentity);
        Assert.True(identity.IsNone);
        Assert.False(identity.IsUserAssigned);
        Assert.False(identity.IsSystemAssigned);
    }

    [Fact]
    public void SystemAssigned_ShouldReturnCorrectIdentity()
    {
        // Act
        var identity = ResourceIdentity.SystemAssigned();

        // Assert
        Assert.NotNull(identity);
        Assert.Null(identity.ODataType);
        Assert.Null(identity.UserAssignedIdentity);
        Assert.False(identity.IsNone);
        Assert.False(identity.IsUserAssigned);
        Assert.True(identity.IsSystemAssigned);
    }

    [Fact]
    public void UserAssigned_ShouldReturnCorrectIdentity()
    {
        // Arrange
        var resourceId = "/subscriptions/12345/resourceGroups/myRg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myIdentity";

        // Act
        var identity = ResourceIdentity.UserAssigned(resourceId);

        // Assert
        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityTypes.UserAssignedIdentity, identity.ODataType);
        Assert.Equal(resourceId, identity.UserAssignedIdentity);
        Assert.False(identity.IsNone);
        Assert.True(identity.IsUserAssigned);
        Assert.False(identity.IsSystemAssigned);
    }

    [Fact]
    public void UserAssigned_WithNullResourceId_ShouldCreateIdentityWithNullValue()
    {
        // Note: Azure AI Search validates at API level, not at model level
        // Act
        var identity = ResourceIdentity.UserAssigned(null!);

        // Assert - the model accepts null but the type is set
        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityTypes.UserAssignedIdentity, identity.ODataType);
        Assert.Null(identity.UserAssignedIdentity);
    }

    [Fact]
    public void UserAssigned_WithEmptyResourceId_ShouldCreateIdentityWithEmptyValue()
    {
        // Note: Azure AI Search validates at API level, not at model level
        // Act
        var identity = ResourceIdentity.UserAssigned("");

        // Assert - the model accepts empty string but the type is set
        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityTypes.UserAssignedIdentity, identity.ODataType);
        Assert.Equal("", identity.UserAssignedIdentity);
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void None_ShouldSerializeCorrectly()
    {
        // Arrange
        var identity = ResourceIdentity.None();

        // Act
        var json = JsonSerializer.Serialize(identity);
        var result = JsonDocument.Parse(json);

        // Assert
        Assert.Equal(ResourceIdentityTypes.None, result.RootElement.GetProperty("@odata.type").GetString());
    }

    [Fact]
    public void UserAssigned_ShouldSerializeCorrectly()
    {
        // Arrange
        var resourceId = "/subscriptions/12345/resourceGroups/myRg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myIdentity";
        var identity = ResourceIdentity.UserAssigned(resourceId);

        // Act
        var json = JsonSerializer.Serialize(identity);
        var result = JsonDocument.Parse(json);

        // Assert
        Assert.Equal(ResourceIdentityTypes.UserAssignedIdentity, result.RootElement.GetProperty("@odata.type").GetString());
        Assert.Equal(resourceId, result.RootElement.GetProperty("userAssignedIdentity").GetString());
    }

    [Fact]
    public void None_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = @"{""@odata.type"":""#Microsoft.Azure.Search.DataNone""}";

        // Act
        var identity = JsonSerializer.Deserialize<ResourceIdentity>(json);

        // Assert
        Assert.NotNull(identity);
        Assert.True(identity.IsNone);
        Assert.Null(identity.UserAssignedIdentity);
    }

    [Fact]
    public void UserAssigned_ShouldDeserializeCorrectly()
    {
        // Arrange
        var resourceId = "/subscriptions/12345/resourceGroups/myRg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myIdentity";
        var json = $@"{{""@odata.type"":""#Microsoft.Azure.Search.DataUserAssignedIdentity"",""userAssignedIdentity"":""{resourceId}""}}";

        // Act
        var identity = JsonSerializer.Deserialize<ResourceIdentity>(json);

        // Assert
        Assert.NotNull(identity);
        Assert.True(identity.IsUserAssigned);
        Assert.Equal(resourceId, identity.UserAssignedIdentity);
    }

    [Fact]
    public void SystemAssigned_WithNullType_ShouldDeserializeCorrectly()
    {
        // Arrange - system assigned typically has no @odata.type
        var json = @"{}";

        // Act
        var identity = JsonSerializer.Deserialize<ResourceIdentity>(json);

        // Assert
        Assert.NotNull(identity);
        Assert.True(identity.IsSystemAssigned);
    }

    #endregion

    #region ResourceIdentityTypes Constants Tests

    [Fact]
    public void ResourceIdentityTypes_None_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Azure.Search.DataNone", ResourceIdentityTypes.None);
    }

    [Fact]
    public void ResourceIdentityTypes_UserAssignedIdentity_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Azure.Search.DataUserAssignedIdentity", ResourceIdentityTypes.UserAssignedIdentity);
    }

    #endregion

    #region CognitiveServicesAccountTypes Constants Tests

    [Fact]
    public void CognitiveServicesAccountTypes_ByKey_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Azure.Search.CognitiveServicesByKey", CognitiveServicesAccountTypes.ByKey);
    }

    [Fact]
    public void CognitiveServicesAccountTypes_AIServicesByKey_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Azure.Search.AIServicesByKey", CognitiveServicesAccountTypes.AIServicesByKey);
    }

    [Fact]
    public void CognitiveServicesAccountTypes_AIServicesByIdentity_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Azure.Search.AIServicesByIdentity", CognitiveServicesAccountTypes.AIServicesByIdentity);
    }

    #endregion

    #region Integration with DataSource Tests

    [Fact]
    public void DataSource_WithIdentity_ShouldSerializeCorrectly()
    {
        // Arrange
        var identity = ResourceIdentity.UserAssigned("/subscriptions/test/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myId");
        var dataSource = new DataSource
        {
            Name = "test-datasource",
            Type = "azureblob",
            Credentials = new DataSourceCredentials { ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test" },
            Container = new DataSourceContainer { Name = "container" },
            Identity = identity
        };

        // Act
        var json = JsonSerializer.Serialize(dataSource);
        var deserialized = JsonSerializer.Deserialize<DataSource>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Identity);
        Assert.True(deserialized.Identity.IsUserAssigned);
    }

    [Fact]
    public void DataSource_WithoutIdentity_ShouldSerializeCorrectly()
    {
        // Arrange
        var dataSource = new DataSource
        {
            Name = "test-datasource",
            Type = "azureblob",
            Credentials = new DataSourceCredentials { ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test" },
            Container = new DataSourceContainer { Name = "container" }
        };

        // Act
        var json = JsonSerializer.Serialize(dataSource);
        var deserialized = JsonSerializer.Deserialize<DataSource>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Identity);
    }

    #endregion

    #region Integration with Indexer Tests

    [Fact]
    public void Indexer_WithIdentity_ShouldSerializeCorrectly()
    {
        // Arrange
        var indexer = new Indexer
        {
            Name = "test-indexer",
            DataSourceName = "test-datasource",
            TargetIndexName = "test-index",
            Identity = ResourceIdentity.SystemAssigned()
        };

        // Act
        var json = JsonSerializer.Serialize(indexer);
        var deserialized = JsonSerializer.Deserialize<Indexer>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Identity);
        Assert.True(deserialized.Identity.IsSystemAssigned);
    }

    #endregion

    #region Skill Auth Identity Tests

    [Fact]
    public void Skill_WithAuthIdentity_ShouldSerializeCorrectly()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Custom.WebApiSkill",
            Name = "test-skill",
            Uri = "https://test.azurewebsites.net/api/skill",
            Context = "/document",
            AuthResourceId = "https://test.azurewebsites.net",
            AuthIdentity = ResourceIdentity.UserAssigned("/subscriptions/test/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myId"),
            Inputs = new List<SkillInput>
            {
                new SkillInput { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new SkillOutput { Name = "result", TargetName = "processedResult" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(skill);
        var deserialized = JsonSerializer.Deserialize<Skill>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("https://test.azurewebsites.net", deserialized.AuthResourceId);
        Assert.NotNull(deserialized.AuthIdentity);
        Assert.True(deserialized.AuthIdentity.IsUserAssigned);
    }

    [Fact]
    public void Skill_WithoutAuthIdentity_ShouldSerializeCorrectly()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Custom.WebApiSkill",
            Name = "test-skill",
            Uri = "https://test.azurewebsites.net/api/skill",
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new SkillInput { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new SkillOutput { Name = "result", TargetName = "processedResult" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(skill);
        var deserialized = JsonSerializer.Deserialize<Skill>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.AuthResourceId);
        Assert.Null(deserialized.AuthIdentity);
    }

    #endregion

    #region CognitiveServicesAccount Identity Tests

    [Fact]
    public void CognitiveServicesAccount_ByKey_ShouldSetCorrectValues()
    {
        // Arrange
        var account = new CognitiveServicesAccount
        {
            ODataType = CognitiveServicesAccountTypes.ByKey,
            Key = "test-api-key"
        };

        // Assert
        Assert.True(account.UsesApiKey);
        Assert.False(account.UsesManagedIdentity);
    }

    [Fact]
    public void CognitiveServicesAccount_ByIdentity_ShouldSetCorrectValues()
    {
        // Arrange
        var account = new CognitiveServicesAccount
        {
            ODataType = CognitiveServicesAccountTypes.AIServicesByIdentity,
            SubdomainUrl = "https://test.cognitiveservices.azure.com",
            Identity = ResourceIdentity.SystemAssigned()
        };

        // Assert
        Assert.False(account.UsesApiKey);
        Assert.True(account.UsesManagedIdentity);
        Assert.NotNull(account.Identity);
        Assert.True(account.Identity.IsSystemAssigned);
    }

    [Fact]
    public void CognitiveServicesAccount_ShouldSerializeCorrectly()
    {
        // Arrange
        var account = new CognitiveServicesAccount
        {
            ODataType = CognitiveServicesAccountTypes.AIServicesByIdentity,
            SubdomainUrl = "https://test.cognitiveservices.azure.com",
            Identity = ResourceIdentity.UserAssigned("/subscriptions/test/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/myId")
        };

        // Act
        var json = JsonSerializer.Serialize(account);
        var deserialized = JsonSerializer.Deserialize<CognitiveServicesAccount>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(CognitiveServicesAccountTypes.AIServicesByIdentity, deserialized.ODataType);
        Assert.Equal("https://test.cognitiveservices.azure.com", deserialized.SubdomainUrl);
        Assert.NotNull(deserialized.Identity);
        Assert.True(deserialized.Identity.IsUserAssigned);
    }

    #endregion
}
