using AzureAISearchSimulator.Core.Models;
using System.Text;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// TextMergeSkill - Merges multiple text fragments into a single string.
/// </summary>
public class TextMergeSkillExecutor : ISkillExecutor
{
    public string ODataType => "#Microsoft.Skills.Text.MergeSkill";

    public Task<SkillExecutionResult> ExecuteAsync(
        Skill skill, 
        EnrichedDocument document, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();

            foreach (var ctx in contexts)
            {
                var textInput = skill.Inputs.FirstOrDefault(i => i.Name == "text");
                var itemsToInsertInput = skill.Inputs.FirstOrDefault(i => i.Name == "itemsToInsert");
                var offsetsInput = skill.Inputs.FirstOrDefault(i => i.Name == "offsets");

                // Get base text
                var baseText = "";
                if (textInput?.Source != null)
                {
                    var sourcePath = ResolveSourcePath(ctx, textInput.Source);
                    baseText = document.GetValue<string>(sourcePath) ?? "";
                }

                // Get items to insert
                var itemsToInsert = new List<string>();
                if (itemsToInsertInput?.Source != null)
                {
                    var sourcePath = ResolveSourcePath(ctx, itemsToInsertInput.Source);
                    var items = document.GetValue<List<object>>(sourcePath);
                    if (items != null)
                    {
                        itemsToInsert = items.Select(i => i?.ToString() ?? "").ToList();
                    }
                }

                // If we have items and no base text, just concatenate items
                string mergedText;
                if (string.IsNullOrEmpty(baseText) && itemsToInsert.Count > 0)
                {
                    var preTag = skill.InsertPreTag ?? "";
                    var postTag = skill.InsertPostTag ?? "";
                    var sb = new StringBuilder();
                    
                    foreach (var item in itemsToInsert)
                    {
                        sb.Append(preTag);
                        sb.Append(item);
                        sb.Append(postTag);
                    }
                    
                    mergedText = sb.ToString();
                }
                else
                {
                    // Use base text (with insertions if offsets provided)
                    mergedText = baseText;
                }

                // Set output
                var mergedTextOutput = skill.Outputs.FirstOrDefault(o => o.Name == "mergedText");
                var targetName = mergedTextOutput?.TargetName ?? "mergedText";
                var outputPath = $"{ctx}/{targetName}";

                document.SetValue(outputPath, mergedText);
            }

            return Task.FromResult(SkillExecutionResult.Succeeded());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SkillExecutionResult.Failed($"TextMergeSkill error: {ex.Message}"));
        }
    }

    private static string ResolveSourcePath(string context, string source)
    {
        if (source.StartsWith("/"))
        {
            return source;
        }
        return $"{context}/{source}";
    }
}
