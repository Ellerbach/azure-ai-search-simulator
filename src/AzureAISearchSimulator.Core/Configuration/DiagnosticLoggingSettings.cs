namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for diagnostic/verbose logging during indexing and skill execution.
/// These settings help with debugging by providing detailed insights into the processing pipeline.
/// </summary>
public class DiagnosticLoggingSettings
{
    public const string SectionName = "DiagnosticLogging";

    /// <summary>
    /// Enable verbose logging for the entire diagnostic subsystem.
    /// When false, all other settings in this section are ignored.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Log details about each document being processed by the indexer.
    /// Includes document key, content type, metadata, and processing status.
    /// </summary>
    public bool LogDocumentDetails { get; set; } = true;

    /// <summary>
    /// Log when each skill in a skillset is invoked and its execution result.
    /// </summary>
    public bool LogSkillExecution { get; set; } = true;

    /// <summary>
    /// Log the input payload passed to each skill.
    /// Warning: This can generate large amounts of log data.
    /// </summary>
    public bool LogSkillInputPayloads { get; set; } = false;

    /// <summary>
    /// Log the output payload produced by each skill.
    /// Warning: This can generate large amounts of log data.
    /// </summary>
    public bool LogSkillOutputPayloads { get; set; } = false;

    /// <summary>
    /// Log the complete enriched document state after each skill execution.
    /// Warning: This can generate very large amounts of log data.
    /// </summary>
    public bool LogEnrichedDocumentState { get; set; } = false;

    /// <summary>
    /// Log field mappings applied during document indexing.
    /// </summary>
    public bool LogFieldMappings { get; set; } = true;

    /// <summary>
    /// Maximum length of string values to log before truncating.
    /// Set to 0 for no truncation (use with caution).
    /// </summary>
    public int MaxStringLogLength { get; set; } = 500;

    /// <summary>
    /// Include timing information for each operation.
    /// </summary>
    public bool IncludeTimings { get; set; } = true;
}
