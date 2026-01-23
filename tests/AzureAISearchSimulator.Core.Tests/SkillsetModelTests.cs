using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for Skillset models.
/// </summary>
public class SkillsetModelTests
{
    [Fact]
    public void Skillset_WithTextSplitSkill_ShouldBeConfigured()
    {
        // Arrange
        var skillset = new Skillset
        {
            Name = "text-processing",
            Description = "Text processing skillset",
            Skills = new List<Skill>
            {
                new()
                {
                    ODataType = "#Microsoft.Skills.Text.SplitSkill",
                    Name = "split-skill",
                    Context = "/document",
                    Inputs = new List<SkillInput>
                    {
                        new() { Name = "text", Source = "/document/content" }
                    },
                    Outputs = new List<SkillOutput>
                    {
                        new() { Name = "textItems", TargetName = "pages" }
                    }
                }
            }
        };

        // Assert
        Assert.Equal("text-processing", skillset.Name);
        Assert.Single(skillset.Skills);
        Assert.Equal("#Microsoft.Skills.Text.SplitSkill", skillset.Skills[0].ODataType);
        Assert.Single(skillset.Skills[0].Inputs);
        Assert.Single(skillset.Skills[0].Outputs);
    }

    [Fact]
    public void Skill_AzureOpenAIEmbedding_ShouldHaveRequiredProperties()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
            Name = "embedding-skill",
            Context = "/document",
            ResourceUri = "https://myresource.openai.azure.com",
            DeploymentId = "text-embedding-ada-002",
            ModelName = "text-embedding-ada-002",
            Inputs = new List<SkillInput>
            {
                new() { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "embedding", TargetName = "contentVector" }
            }
        };

        // Assert
        Assert.Equal("#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill", skill.ODataType);
        Assert.Equal("https://myresource.openai.azure.com", skill.ResourceUri);
        Assert.Equal("text-embedding-ada-002", skill.DeploymentId);
    }

    [Fact]
    public void Skill_CustomWebApi_ShouldHaveHttpConfiguration()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Custom.WebApiSkill",
            Name = "custom-skill",
            Context = "/document",
            Uri = "https://myfunction.azurewebsites.net/api/process",
            HttpMethod = "POST",
            Timeout = "PT30S",
            BatchSize = 10,
            HttpHeaders = new Dictionary<string, string>
            {
                ["x-functions-key"] = "my-key"
            },
            Inputs = new List<SkillInput>
            {
                new() { Name = "text", Source = "/document/content" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "result", TargetName = "customResult" }
            }
        };

        // Assert
        Assert.Equal("#Microsoft.Skills.Custom.WebApiSkill", skill.ODataType);
        Assert.Equal("https://myfunction.azurewebsites.net/api/process", skill.Uri);
        Assert.Equal("POST", skill.HttpMethod);
        Assert.Equal("PT30S", skill.Timeout);
        Assert.Equal(10, skill.BatchSize);
        Assert.True(skill.HttpHeaders?.ContainsKey("x-functions-key"));
    }

    [Fact]
    public void Skill_Shaper_ShouldHaveInputsAndOutputs()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Util.ShaperSkill",
            Name = "shaper-skill",
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new() { Name = "title", Source = "/document/metadata_storage_name" },
                new() { Name = "content", Source = "/document/content" },
                new() { Name = "path", Source = "/document/metadata_storage_path" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "documentInfo" }
            }
        };

        // Assert
        Assert.Equal("#Microsoft.Skills.Util.ShaperSkill", skill.ODataType);
        Assert.Equal(3, skill.Inputs.Count);
        Assert.Single(skill.Outputs);
    }

    [Fact]
    public void Skill_Conditional_ShouldHaveConditionConfiguration()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Util.ConditionalSkill",
            Name = "conditional-skill",
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new() { Name = "condition", Source = "= $(/document/language) == 'en'" },
                new() { Name = "whenTrue", Source = "/document/content" },
                new() { Name = "whenFalse", Source = "= ''" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "output", TargetName = "conditionalContent" }
            }
        };

        // Assert
        Assert.Equal("#Microsoft.Skills.Util.ConditionalSkill", skill.ODataType);
        Assert.Equal(3, skill.Inputs.Count);
        Assert.Contains(skill.Inputs, i => i.Name == "condition");
        Assert.Contains(skill.Inputs, i => i.Name == "whenTrue");
        Assert.Contains(skill.Inputs, i => i.Name == "whenFalse");
    }

    [Fact]
    public void Skill_TextMerge_ShouldHaveInsertTags()
    {
        // Arrange
        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Text.MergeSkill",
            Name = "merge-skill",
            Context = "/document",
            InsertPreTag = " ",
            InsertPostTag = " ",
            Inputs = new List<SkillInput>
            {
                new() { Name = "itemsToInsert", Source = "/document/pages/*" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "mergedText", TargetName = "merged_content" }
            }
        };

        // Assert
        Assert.Equal("#Microsoft.Skills.Text.MergeSkill", skill.ODataType);
        Assert.Equal(" ", skill.InsertPreTag);
        Assert.Equal(" ", skill.InsertPostTag);
    }

    [Fact]
    public void SkillInput_Source_ShouldSupportExpressions()
    {
        // Arrange
        var input = new SkillInput
        {
            Name = "condition",
            Source = "= $(/document/language) == 'en'"
        };

        // Assert
        Assert.StartsWith("=", input.Source);
    }

    [Fact]
    public void SkillOutput_TargetName_ShouldBeRequired()
    {
        // Arrange
        var output = new SkillOutput
        {
            Name = "textItems",
            TargetName = "pages"
        };

        // Assert
        Assert.NotNull(output.Name);
        Assert.NotNull(output.TargetName);
    }
}
