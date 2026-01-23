using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Current status of an indexer.
/// </summary>
public class IndexerStatus
{
    /// <summary>
    /// Overall status (unknown, error, running).
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = IndexerStatusValue.Unknown;

    /// <summary>
    /// Last execution result.
    /// </summary>
    [JsonPropertyName("lastResult")]
    public IndexerExecutionResult? LastResult { get; set; }

    /// <summary>
    /// Execution history.
    /// </summary>
    [JsonPropertyName("executionHistory")]
    public List<IndexerExecutionResult> ExecutionHistory { get; set; } = new();

    /// <summary>
    /// Limits and quotas.
    /// </summary>
    [JsonPropertyName("limits")]
    public IndexerLimits Limits { get; set; } = new();
}

/// <summary>
/// Possible indexer status values.
/// </summary>
public static class IndexerStatusValue
{
    public const string Unknown = "unknown";
    public const string Error = "error";
    public const string Running = "running";
}

/// <summary>
/// Result of an indexer execution.
/// </summary>
public class IndexerExecutionResult
{
    /// <summary>
    /// Status of this execution.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = IndexerExecutionStatus.InProgress;

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Start time of execution.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// End time of execution.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Number of items processed.
    /// </summary>
    [JsonPropertyName("itemsProcessed")]
    public int ItemsProcessed { get; set; }

    /// <summary>
    /// Number of items that failed.
    /// </summary>
    [JsonPropertyName("itemsFailed")]
    public int ItemsFailed { get; set; }

    /// <summary>
    /// Initial tracking state.
    /// </summary>
    [JsonPropertyName("initialTrackingState")]
    public string? InitialTrackingState { get; set; }

    /// <summary>
    /// Final tracking state.
    /// </summary>
    [JsonPropertyName("finalTrackingState")]
    public string? FinalTrackingState { get; set; }

    /// <summary>
    /// Errors encountered during execution.
    /// </summary>
    [JsonPropertyName("errors")]
    public List<IndexerExecutionError> Errors { get; set; } = new();

    /// <summary>
    /// Warnings encountered during execution.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<IndexerExecutionWarning> Warnings { get; set; } = new();
}

/// <summary>
/// Possible execution status values.
/// </summary>
public static class IndexerExecutionStatus
{
    public const string TransientFailure = "transientFailure";
    public const string Success = "success";
    public const string InProgress = "inProgress";
    public const string Reset = "reset";
}

/// <summary>
/// Error during indexer execution.
/// </summary>
public class IndexerExecutionError
{
    /// <summary>
    /// Error key.
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Status code.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>
    /// Name of the item that failed.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Details about the error.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; set; }

    /// <summary>
    /// Documentation link.
    /// </summary>
    [JsonPropertyName("documentationLink")]
    public string? DocumentationLink { get; set; }
}

/// <summary>
/// Warning during indexer execution.
/// </summary>
public class IndexerExecutionWarning
{
    /// <summary>
    /// Warning key.
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Warning message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Name of the item that generated warning.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Details about the warning.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; set; }

    /// <summary>
    /// Documentation link.
    /// </summary>
    [JsonPropertyName("documentationLink")]
    public string? DocumentationLink { get; set; }
}

/// <summary>
/// Limits for indexer execution.
/// </summary>
public class IndexerLimits
{
    /// <summary>
    /// Maximum run time.
    /// </summary>
    [JsonPropertyName("maxRunTime")]
    public string MaxRunTime { get; set; } = "PT2H"; // 2 hours default

    /// <summary>
    /// Maximum document extraction size.
    /// </summary>
    [JsonPropertyName("maxDocumentExtractionSize")]
    public long MaxDocumentExtractionSize { get; set; } = 16 * 1024 * 1024; // 16 MB

    /// <summary>
    /// Maximum document content characters to extract.
    /// </summary>
    [JsonPropertyName("maxDocumentContentCharactersToExtract")]
    public long MaxDocumentContentCharactersToExtract { get; set; } = 64000;
}
