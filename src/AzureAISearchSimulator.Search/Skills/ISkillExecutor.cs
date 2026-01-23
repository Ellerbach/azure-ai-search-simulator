using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// Interface for skill execution.
/// </summary>
public interface ISkillExecutor
{
    /// <summary>
    /// The OData type this executor handles (e.g., "#Microsoft.Skills.Text.SplitSkill").
    /// </summary>
    string ODataType { get; }

    /// <summary>
    /// Executes the skill on the enriched document.
    /// </summary>
    /// <param name="skill">The skill definition.</param>
    /// <param name="document">The enriched document to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<SkillExecutionResult> ExecuteAsync(
        Skill skill, 
        EnrichedDocument document, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of skill execution.
/// </summary>
public class SkillExecutionResult
{
    /// <summary>
    /// Whether the execution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of warnings generated during execution.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// List of errors if execution failed.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static SkillExecutionResult Succeeded() => new() { Success = true };

    /// <summary>
    /// Creates a successful result with warnings.
    /// </summary>
    public static SkillExecutionResult SucceededWithWarnings(params string[] warnings) => 
        new() { Success = true, Warnings = warnings.ToList() };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static SkillExecutionResult Failed(params string[] errors) => 
        new() { Success = false, Errors = errors.ToList() };
}
