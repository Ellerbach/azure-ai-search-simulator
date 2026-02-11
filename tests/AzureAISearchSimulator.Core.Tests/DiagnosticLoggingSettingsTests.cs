using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for DiagnosticLoggingSettings configuration.
/// </summary>
public class DiagnosticLoggingSettingsTests
{
    [Fact]
    public void DiagnosticLoggingSettings_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new DiagnosticLoggingSettings();

        // Assert
        Assert.False(settings.Enabled);
        Assert.True(settings.LogDocumentDetails);
        Assert.True(settings.LogSkillExecution);
        Assert.False(settings.LogSkillInputPayloads);
        Assert.False(settings.LogSkillOutputPayloads);
        Assert.False(settings.LogEnrichedDocumentState);
        Assert.True(settings.LogFieldMappings);
        Assert.Equal(500, settings.MaxStringLogLength);
        Assert.True(settings.IncludeTimings);
    }

    [Fact]
    public void DiagnosticLoggingSettings_SectionName_ShouldBeCorrect()
    {
        // Assert
        Assert.Equal("DiagnosticLogging", DiagnosticLoggingSettings.SectionName);
    }

    [Fact]
    public void DiagnosticLoggingSettings_Enabled_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { Enabled = true };

        // Assert
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogDocumentDetails_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogDocumentDetails = false };

        // Assert
        Assert.False(settings.LogDocumentDetails);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogSkillExecution_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogSkillExecution = false };

        // Assert
        Assert.False(settings.LogSkillExecution);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogSkillInputPayloads_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogSkillInputPayloads = true };

        // Assert
        Assert.True(settings.LogSkillInputPayloads);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogSkillOutputPayloads_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogSkillOutputPayloads = true };

        // Assert
        Assert.True(settings.LogSkillOutputPayloads);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogEnrichedDocumentState_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogEnrichedDocumentState = true };

        // Assert
        Assert.True(settings.LogEnrichedDocumentState);
    }

    [Fact]
    public void DiagnosticLoggingSettings_LogFieldMappings_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { LogFieldMappings = false };

        // Assert
        Assert.False(settings.LogFieldMappings);
    }

    [Fact]
    public void DiagnosticLoggingSettings_MaxStringLogLength_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { MaxStringLogLength = 1000 };

        // Assert
        Assert.Equal(1000, settings.MaxStringLogLength);
    }

    [Fact]
    public void DiagnosticLoggingSettings_MaxStringLogLength_ZeroMeansNoTruncation()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { MaxStringLogLength = 0 };

        // Assert
        Assert.Equal(0, settings.MaxStringLogLength);
    }

    [Fact]
    public void DiagnosticLoggingSettings_IncludeTimings_ShouldBeConfigurable()
    {
        // Arrange
        var settings = new DiagnosticLoggingSettings { IncludeTimings = false };

        // Assert
        Assert.False(settings.IncludeTimings);
    }

    [Fact]
    public void DiagnosticLoggingSettings_FullConfiguration_ShouldWorkTogether()
    {
        // Arrange - Enable everything for verbose debugging
        var settings = new DiagnosticLoggingSettings
        {
            Enabled = true,
            LogDocumentDetails = true,
            LogSkillExecution = true,
            LogSkillInputPayloads = true,
            LogSkillOutputPayloads = true,
            LogEnrichedDocumentState = true,
            LogFieldMappings = true,
            MaxStringLogLength = 2000,
            IncludeTimings = true
        };

        // Assert
        Assert.True(settings.Enabled);
        Assert.True(settings.LogDocumentDetails);
        Assert.True(settings.LogSkillExecution);
        Assert.True(settings.LogSkillInputPayloads);
        Assert.True(settings.LogSkillOutputPayloads);
        Assert.True(settings.LogEnrichedDocumentState);
        Assert.True(settings.LogFieldMappings);
        Assert.Equal(2000, settings.MaxStringLogLength);
        Assert.True(settings.IncludeTimings);
    }

    [Fact]
    public void DiagnosticLoggingSettings_MinimalConfiguration_ShouldWorkTogether()
    {
        // Arrange - Enable only basic logging
        var settings = new DiagnosticLoggingSettings
        {
            Enabled = true,
            LogDocumentDetails = false,
            LogSkillExecution = true,
            LogSkillInputPayloads = false,
            LogSkillOutputPayloads = false,
            LogEnrichedDocumentState = false,
            LogFieldMappings = false,
            MaxStringLogLength = 100,
            IncludeTimings = false
        };

        // Assert
        Assert.True(settings.Enabled);
        Assert.False(settings.LogDocumentDetails);
        Assert.True(settings.LogSkillExecution);
        Assert.False(settings.LogSkillInputPayloads);
        Assert.False(settings.LogSkillOutputPayloads);
        Assert.False(settings.LogEnrichedDocumentState);
        Assert.False(settings.LogFieldMappings);
        Assert.Equal(100, settings.MaxStringLogLength);
        Assert.False(settings.IncludeTimings);
    }
}
