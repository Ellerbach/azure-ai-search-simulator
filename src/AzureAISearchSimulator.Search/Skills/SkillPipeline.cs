using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// Interface for the skill pipeline that executes skillsets.
/// </summary>
public interface ISkillPipeline
{
    /// <summary>
    /// Executes all skills in a skillset against a document.
    /// </summary>
    /// <param name="skillset">The skillset to execute.</param>
    /// <param name="document">The enriched document to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pipeline execution result.</returns>
    Task<SkillPipelineResult> ExecuteAsync(
        Skillset skillset,
        EnrichedDocument document,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of skill pipeline execution.
/// </summary>
public class SkillPipelineResult
{
    public bool Success { get; set; }
    public List<SkillExecutionSummary> SkillResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
}

/// <summary>
/// Summary of a single skill execution.
/// </summary>
public class SkillExecutionSummary
{
    public string SkillName { get; set; } = string.Empty;
    public string SkillType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Default implementation of the skill pipeline.
/// </summary>
public class SkillPipeline : ISkillPipeline
{
    private readonly IEnumerable<ISkillExecutor> _skillExecutors;
    private readonly ILogger<SkillPipeline> _logger;

    public SkillPipeline(
        IEnumerable<ISkillExecutor> skillExecutors,
        ILogger<SkillPipeline> logger)
    {
        _skillExecutors = skillExecutors;
        _logger = logger;
    }

    public async Task<SkillPipelineResult> ExecuteAsync(
        Skillset skillset,
        EnrichedDocument document,
        CancellationToken cancellationToken = default)
    {
        var result = new SkillPipelineResult { Success = true };
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Executing skillset '{SkillsetName}' with {SkillCount} skills",
            skillset.Name, skillset.Skills.Count);

        // Build executor lookup
        var executorLookup = _skillExecutors.ToDictionary(e => e.ODataType, e => e);

        // Execute skills in order
        foreach (var skill in skillset.Skills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skillName = skill.Name ?? skill.ODataType;
            var skillStart = DateTime.UtcNow;

            _logger.LogDebug("Executing skill '{SkillName}' ({SkillType})", skillName, skill.ODataType);

            var summary = new SkillExecutionSummary
            {
                SkillName = skillName,
                SkillType = skill.ODataType
            };

            // Find executor for this skill type
            if (!executorLookup.TryGetValue(skill.ODataType, out var executor))
            {
                var error = $"No executor found for skill type '{skill.ODataType}'";
                _logger.LogWarning(error);
                summary.Success = false;
                summary.Errors.Add(error);
                result.Warnings.Add(error);
                
                // Continue with other skills (soft failure)
                result.SkillResults.Add(summary);
                continue;
            }

            try
            {
                var skillResult = await executor.ExecuteAsync(skill, document, cancellationToken);

                summary.Success = skillResult.Success;
                summary.Warnings.AddRange(skillResult.Warnings);
                summary.Errors.AddRange(skillResult.Errors);

                if (!skillResult.Success)
                {
                    _logger.LogWarning("Skill '{SkillName}' failed: {Errors}",
                        skillName, string.Join(", ", skillResult.Errors));
                    result.Errors.AddRange(skillResult.Errors);
                    
                    // For critical failures, stop the pipeline
                    if (skillResult.Errors.Any(e => e.Contains("required") || e.Contains("configuration")))
                    {
                        result.Success = false;
                        summary.Duration = DateTime.UtcNow - skillStart;
                        result.SkillResults.Add(summary);
                        break;
                    }
                }

                result.Warnings.AddRange(skillResult.Warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception executing skill '{SkillName}'", skillName);
                summary.Success = false;
                summary.Errors.Add($"Exception: {ex.Message}");
                result.Errors.Add($"Skill '{skillName}' threw exception: {ex.Message}");
            }

            summary.Duration = DateTime.UtcNow - skillStart;
            result.SkillResults.Add(summary);
        }

        result.TotalDuration = DateTime.UtcNow - startTime;

        _logger.LogInformation(
            "Skillset '{SkillsetName}' completed in {Duration}ms. Success: {Success}, Warnings: {WarningCount}, Errors: {ErrorCount}",
            skillset.Name,
            result.TotalDuration.TotalMilliseconds,
            result.Success,
            result.Warnings.Count,
            result.Errors.Count);

        return result;
    }
}
