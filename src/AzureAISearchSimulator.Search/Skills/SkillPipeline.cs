using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
    private readonly DiagnosticLoggingSettings _diagnosticSettings;
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SkillPipeline(
        IEnumerable<ISkillExecutor> skillExecutors,
        ILogger<SkillPipeline> logger,
        IOptions<DiagnosticLoggingSettings> diagnosticSettings)
    {
        _skillExecutors = skillExecutors;
        _logger = logger;
        _diagnosticSettings = diagnosticSettings.Value;
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

            // Diagnostic: Log skill execution start
            if (_diagnosticSettings.Enabled && _diagnosticSettings.LogSkillExecution)
            {
                _logger.LogInformation("[DIAGNOSTIC] Starting skill '{SkillName}' ({SkillType}) in skillset '{SkillsetName}'",
                    skillName, skill.ODataType, skillset.Name);
            }
            else
            {
                _logger.LogDebug("Executing skill '{SkillName}' ({SkillType})", skillName, skill.ODataType);
            }

            // Diagnostic: Log input payload before skill execution
            if (_diagnosticSettings.Enabled && _diagnosticSettings.LogSkillInputPayloads)
            {
                LogSkillInputPayload(skill, document, skillName);
            }

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

                // Diagnostic: Log skill output payload
                if (_diagnosticSettings.Enabled && _diagnosticSettings.LogSkillOutputPayloads)
                {
                    LogSkillOutputPayload(skill, document, skillName, skillResult.Success);
                }

                // Diagnostic: Log enriched document state
                if (_diagnosticSettings.Enabled && _diagnosticSettings.LogEnrichedDocumentState)
                {
                    LogEnrichedDocumentState(document, skillName);
                }

                // Diagnostic: Log skill completion with timing
                if (_diagnosticSettings.Enabled && _diagnosticSettings.LogSkillExecution)
                {
                    var duration = DateTime.UtcNow - skillStart;
                    _logger.LogInformation(
                        "[DIAGNOSTIC] Completed skill '{SkillName}' - Success: {Success}, Duration: {Duration}ms, Warnings: {WarningCount}, Errors: {ErrorCount}",
                        skillName, skillResult.Success, duration.TotalMilliseconds, skillResult.Warnings.Count, skillResult.Errors.Count);
                }

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

    /// <summary>
    /// Logs the input payload for a skill execution.
    /// </summary>
    private void LogSkillInputPayload(Skill skill, EnrichedDocument document, string skillName)
    {
        try
        {
            var inputData = new Dictionary<string, object?>();
            var context = skill.Context ?? "/document";

            foreach (var input in skill.Inputs)
            {
                if (input.Source != null)
                {
                    var sourcePath = input.Source.Replace("/document", context);
                    var value = document.GetValue(sourcePath);
                    inputData[input.Name] = TruncateValue(value);
                }
            }

            var json = JsonSerializer.Serialize(inputData, _jsonOptions);
            _logger.LogInformation("[DIAGNOSTIC] Skill '{SkillName}' INPUT payload:\n{Payload}", skillName, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[DIAGNOSTIC] Failed to log input payload for skill '{SkillName}': {Error}", 
                skillName, ex.Message);
        }
    }

    /// <summary>
    /// Logs the output payload after a skill execution.
    /// </summary>
    private void LogSkillOutputPayload(Skill skill, EnrichedDocument document, string skillName, bool success)
    {
        try
        {
            var outputData = new Dictionary<string, object?>();
            var context = skill.Context ?? "/document";

            foreach (var output in skill.Outputs)
            {
                var targetName = output.TargetName ?? output.Name;
                var outputPath = $"{context}/{targetName}";
                var value = document.GetValue(outputPath);
                outputData[targetName] = TruncateValue(value);
            }

            var json = JsonSerializer.Serialize(outputData, _jsonOptions);
            _logger.LogInformation("[DIAGNOSTIC] Skill '{SkillName}' OUTPUT payload (Success: {Success}):\n{Payload}", 
                skillName, success, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[DIAGNOSTIC] Failed to log output payload for skill '{SkillName}': {Error}", 
                skillName, ex.Message);
        }
    }

    /// <summary>
    /// Logs the complete enriched document state.
    /// </summary>
    private void LogEnrichedDocumentState(EnrichedDocument document, string afterSkillName)
    {
        try
        {
            var docData = document.ToDictionary();
            var truncatedData = TruncateDictionary(docData);
            var json = JsonSerializer.Serialize(truncatedData, _jsonOptions);
            _logger.LogInformation("[DIAGNOSTIC] Enriched document state after skill '{SkillName}':\n{DocumentState}", 
                afterSkillName, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[DIAGNOSTIC] Failed to log enriched document state after skill '{SkillName}': {Error}", 
                afterSkillName, ex.Message);
        }
    }

    /// <summary>
    /// Truncates a value for logging based on settings.
    /// </summary>
    private object? TruncateValue(object? value)
    {
        if (value == null) return null;
        if (_diagnosticSettings.MaxStringLogLength <= 0) return value;

        if (value is string strValue && strValue.Length > _diagnosticSettings.MaxStringLogLength)
        {
            return strValue.Substring(0, _diagnosticSettings.MaxStringLogLength) + $"... [truncated, total length: {strValue.Length}]";
        }

        if (value is IEnumerable<object> enumerable && value is not string)
        {
            var list = enumerable.Take(10).Select(TruncateValue).ToList();
            var count = enumerable.Count();
            if (count > 10)
            {
                list.Add($"... [{count - 10} more items]");
            }
            return list;
        }

        return value;
    }

    /// <summary>
    /// Truncates all string values in a dictionary.
    /// </summary>
    private Dictionary<string, object?> TruncateDictionary(Dictionary<string, object?> dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in dict)
        {
            result[kvp.Key] = TruncateValue(kvp.Value);
        }
        return result;
    }
}
