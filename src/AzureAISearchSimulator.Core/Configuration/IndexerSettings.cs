namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for indexers.
/// </summary>
public class IndexerSettings
{
    public const string SectionName = "IndexerSettings";

    /// <summary>
    /// Maximum number of concurrent indexer executions.
    /// </summary>
    public int MaxConcurrentIndexers { get; set; } = 3;

    /// <summary>
    /// Default batch size for document processing.
    /// </summary>
    public int DefaultBatchSize { get; set; } = 1000;

    /// <summary>
    /// Enable scheduled indexer execution.
    /// </summary>
    public bool EnableScheduler { get; set; } = true;

    /// <summary>
    /// Default timeout for indexer execution in minutes.
    /// </summary>
    public int DefaultTimeoutMinutes { get; set; } = 60;
}
