using AzureAISearchSimulator.Core.Services.Authentication;

namespace AzureAISearchSimulator.Api.Tests.Authentication;

/// <summary>
/// Unit tests for AccessLevel enum and its extensions.
/// </summary>
public class AccessLevelTests
{
    #region CanQuery Tests

    [Theory]
    [InlineData(AccessLevel.IndexDataReader, true)]
    [InlineData(AccessLevel.IndexDataContributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.ServiceContributor, false)]
    [InlineData(AccessLevel.Contributor, false)]
    public void CanQuery_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanQuery());
    }

    #endregion

    #region CanModifyDocuments Tests

    [Theory]
    [InlineData(AccessLevel.IndexDataContributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.ServiceContributor, false)]
    [InlineData(AccessLevel.Contributor, false)]
    public void CanModifyDocuments_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanModifyDocuments());
    }

    #endregion

    #region CanManageIndexes Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor, true)]
    [InlineData(AccessLevel.Contributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    public void CanManageIndexes_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanManageIndexes());
    }

    #endregion

    #region CanManageIndexers Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor, true)]
    [InlineData(AccessLevel.Contributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    public void CanManageIndexers_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanManageIndexers());
    }

    #endregion

    #region CanManageSkillsets Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor, true)]
    [InlineData(AccessLevel.Contributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    public void CanManageSkillsets_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanManageSkillsets());
    }

    #endregion

    #region CanManageDataSources Tests

    [Theory]
    [InlineData(AccessLevel.ServiceContributor, true)]
    [InlineData(AccessLevel.Contributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    public void CanManageDataSources_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanManageDataSources());
    }

    #endregion

    #region CanReadServiceInfo Tests

    [Theory]
    [InlineData(AccessLevel.Reader, true)]
    [InlineData(AccessLevel.ServiceContributor, true)]
    [InlineData(AccessLevel.Contributor, true)]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    public void CanReadServiceInfo_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.CanReadServiceInfo());
    }

    #endregion

    #region IsAdmin Tests

    [Theory]
    [InlineData(AccessLevel.FullAccess, true)]
    [InlineData(AccessLevel.None, false)]
    [InlineData(AccessLevel.Reader, false)]
    [InlineData(AccessLevel.IndexDataReader, false)]
    [InlineData(AccessLevel.IndexDataContributor, false)]
    [InlineData(AccessLevel.ServiceContributor, false)]
    [InlineData(AccessLevel.Contributor, false)]
    public void IsAdmin_ReturnsExpectedResult(AccessLevel level, bool expected)
    {
        Assert.Equal(expected, level.IsAdmin());
    }

    #endregion

    #region Access Level Hierarchy Tests

    [Fact]
    public void AccessLevels_HaveCorrectNumericOrder()
    {
        // Verify the numeric ordering is correct for permission escalation
        Assert.True((int)AccessLevel.None < (int)AccessLevel.IndexDataReader);
        Assert.True((int)AccessLevel.IndexDataReader < (int)AccessLevel.Reader);
        Assert.True((int)AccessLevel.Reader < (int)AccessLevel.IndexDataContributor);
        Assert.True((int)AccessLevel.IndexDataContributor < (int)AccessLevel.ServiceContributor);
        Assert.True((int)AccessLevel.ServiceContributor < (int)AccessLevel.Contributor);
        Assert.True((int)AccessLevel.Contributor < (int)AccessLevel.FullAccess);
    }

    #endregion
}
