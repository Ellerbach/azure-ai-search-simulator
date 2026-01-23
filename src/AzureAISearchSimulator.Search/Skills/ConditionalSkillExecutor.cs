using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// ConditionalSkill - Outputs different values based on a condition.
/// </summary>
public class ConditionalSkillExecutor : ISkillExecutor
{
    public string ODataType => "#Microsoft.Skills.Util.ConditionalSkill";

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
                // Get inputs
                var conditionInput = skill.Inputs.FirstOrDefault(i => i.Name == "condition");
                var whenTrueInput = skill.Inputs.FirstOrDefault(i => i.Name == "whenTrue");
                var whenFalseInput = skill.Inputs.FirstOrDefault(i => i.Name == "whenFalse");

                // Evaluate condition
                bool conditionResult = false;
                
                if (conditionInput?.Source != null)
                {
                    var conditionPath = ResolveSourcePath(ctx, conditionInput.Source);
                    var conditionValue = document.GetValue(conditionPath);
                    conditionResult = EvaluateCondition(conditionValue);
                }
                else if (!string.IsNullOrEmpty(skill.Condition))
                {
                    // Parse condition expression like "$(/document/language) == 'en'"
                    conditionResult = EvaluateExpression(skill.Condition, document, ctx);
                }

                // Get the appropriate value
                object? outputValue;
                if (conditionResult)
                {
                    if (whenTrueInput?.Source != null)
                    {
                        var path = ResolveSourcePath(ctx, whenTrueInput.Source);
                        outputValue = document.GetValue(path);
                    }
                    else
                    {
                        outputValue = true;
                    }
                }
                else
                {
                    if (whenFalseInput?.Source != null)
                    {
                        var path = ResolveSourcePath(ctx, whenFalseInput.Source);
                        outputValue = document.GetValue(path);
                    }
                    else
                    {
                        outputValue = false;
                    }
                }

                // Set output
                var outputDef = skill.Outputs.FirstOrDefault(o => o.Name == "output");
                var targetName = outputDef?.TargetName ?? "output";
                var outputPath = $"{ctx}/{targetName}";

                document.SetValue(outputPath, outputValue);
            }

            return Task.FromResult(SkillExecutionResult.Succeeded());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SkillExecutionResult.Failed($"ConditionalSkill error: {ex.Message}"));
        }
    }

    private static bool EvaluateCondition(object? value)
    {
        return value switch
        {
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && s.ToLowerInvariant() != "false",
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            null => false,
            _ => true
        };
    }

    private static bool EvaluateExpression(string expression, EnrichedDocument document, string context)
    {
        // Simple expression parser for patterns like:
        // $(/document/language) == 'en'
        // $(/document/content) != null

        expression = expression.Trim();

        // Check for equality comparison
        if (expression.Contains("=="))
        {
            var parts = expression.Split("==", 2);
            var left = EvaluateOperand(parts[0].Trim(), document, context);
            var right = EvaluateOperand(parts[1].Trim(), document, context);
            return Equals(left, right);
        }

        if (expression.Contains("!="))
        {
            var parts = expression.Split("!=", 2);
            var left = EvaluateOperand(parts[0].Trim(), document, context);
            var right = EvaluateOperand(parts[1].Trim(), document, context);
            return !Equals(left, right);
        }

        // Single value - evaluate as boolean
        var val = EvaluateOperand(expression, document, context);
        return EvaluateCondition(val);
    }

    private static object? EvaluateOperand(string operand, EnrichedDocument document, string context)
    {
        operand = operand.Trim();

        // Path reference: $(/document/field) or /document/field
        if (operand.StartsWith("$(") && operand.EndsWith(")"))
        {
            var path = operand[2..^1];
            return document.GetValue(path);
        }

        if (operand.StartsWith("/"))
        {
            return document.GetValue(operand);
        }

        // String literal: 'value' or "value"
        if ((operand.StartsWith("'") && operand.EndsWith("'")) ||
            (operand.StartsWith("\"") && operand.EndsWith("\"")))
        {
            return operand[1..^1];
        }

        // Null
        if (operand.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Boolean
        if (operand.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (operand.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Number
        if (int.TryParse(operand, out int i))
        {
            return i;
        }
        if (double.TryParse(operand, out double d))
        {
            return d;
        }

        return operand;
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
