namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Main configuration settings for the Azure AI Search Simulator.
/// </summary>
public class SimulatorSettings
{
    public const string SectionName = "SimulatorSettings";

    /// <summary>
    /// Name of the simulated search service.
    /// </summary>
    public string ServiceName { get; set; } = "local-search-simulator";

    /// <summary>
    /// Directory for storing data files.
    /// </summary>
    public string DataDirectory { get; set; } = "./data";

    /// <summary>
    /// Admin API key for full read/write access.
    /// </summary>
    public string AdminApiKey { get; set; } = "admin-key-12345";

    /// <summary>
    /// Query API key for read-only search operations.
    /// </summary>
    public string QueryApiKey { get; set; } = "query-key-67890";

    /// <summary>
    /// Maximum number of indexes allowed.
    /// </summary>
    public int MaxIndexes { get; set; } = 50;

    /// <summary>
    /// Maximum number of documents per index.
    /// </summary>
    public int MaxDocumentsPerIndex { get; set; } = 100000;

    /// <summary>
    /// Maximum number of fields per index.
    /// </summary>
    public int MaxFieldsPerIndex { get; set; } = 1000;

    /// <summary>
    /// Default page size for search results.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Maximum page size for search results.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;
}
