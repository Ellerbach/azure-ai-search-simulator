namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for Lucene.NET search engine.
/// </summary>
public class LuceneSettings
{
    public const string SectionName = "LuceneSettings";

    /// <summary>
    /// Path to store Lucene index files.
    /// </summary>
    public string IndexPath { get; set; } = "./data/lucene";

    /// <summary>
    /// Interval in seconds between index commits.
    /// </summary>
    public int CommitIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of buffered documents before commit.
    /// </summary>
    public int MaxBufferedDocs { get; set; } = 1000;

    /// <summary>
    /// RAM buffer size in MB for indexing.
    /// </summary>
    public double RamBufferSizeMB { get; set; } = 256.0;
}
