using AzureAISearchSimulator.Core.Models;
using System.Text.Json;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// ShaperSkill - Restructures data into a different shape.
/// </summary>
public class ShaperSkillExecutor : ISkillExecutor
{
    public string ODataType => "#Microsoft.Skills.Util.ShaperSkill";

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
                // Build the shaped output from inputs
                var shapedOutput = new Dictionary<string, object?>();

                foreach (var input in skill.Inputs)
                {
                    object? value;
                    
                    if (input.Source != null)
                    {
                        var sourcePath = ResolveSourcePath(ctx, input.Source);
                        value = document.GetValue(sourcePath);
                    }
                    else if (input.Inputs != null && input.Inputs.Count > 0)
                    {
                        // Nested inputs - create a nested object
                        var nestedObj = new Dictionary<string, object?>();
                        foreach (var nestedInput in input.Inputs)
                        {
                            if (nestedInput.Source != null)
                            {
                                var nestedPath = ResolveSourcePath(ctx, nestedInput.Source);
                                nestedObj[nestedInput.Name] = document.GetValue(nestedPath);
                            }
                        }
                        value = nestedObj;
                    }
                    else
                    {
                        value = null;
                    }

                    shapedOutput[input.Name] = value;
                }

                // Get the output configuration
                var outputDef = skill.Outputs.FirstOrDefault(o => o.Name == "output");
                var targetName = outputDef?.TargetName ?? "shapedOutput";
                var outputPath = $"{ctx}/{targetName}";

                document.SetValue(outputPath, shapedOutput);
            }

            return Task.FromResult(SkillExecutionResult.Succeeded());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SkillExecutionResult.Failed($"ShaperSkill error: {ex.Message}"));
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
