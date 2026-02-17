using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Service-level statistics for Azure AI Search, returned by GET /servicestats.
/// </summary>
public class ServiceStatistics
{
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    [JsonPropertyName("counters")]
    public ServiceCounters Counters { get; set; } = new();

    [JsonPropertyName("limits")]
    public ServiceLimits Limits { get; set; } = new();
}

/// <summary>
/// Resource counters with usage and quota information.
/// </summary>
public class ServiceCounters
{
    [JsonPropertyName("documentCount")]
    public ResourceCounter DocumentCount { get; set; } = new();

    [JsonPropertyName("indexesCount")]
    public ResourceCounter IndexesCount { get; set; } = new();

    [JsonPropertyName("indexersCount")]
    public ResourceCounter IndexersCount { get; set; } = new();

    [JsonPropertyName("dataSourcesCount")]
    public ResourceCounter DataSourcesCount { get; set; } = new();

    [JsonPropertyName("storageSize")]
    public ResourceCounter StorageSize { get; set; } = new();

    [JsonPropertyName("synonymMaps")]
    public ResourceCounter SynonymMaps { get; set; } = new();

    [JsonPropertyName("skillsetCount")]
    public ResourceCounter SkillsetCount { get; set; } = new();

    [JsonPropertyName("vectorIndexSize")]
    public ResourceCounter VectorIndexSize { get; set; } = new();
}

/// <summary>
/// A single resource counter with current usage and optional quota.
/// </summary>
public class ResourceCounter
{
    [JsonPropertyName("usage")]
    public long Usage { get; set; }

    /// <summary>
    /// The quota limit. Null means unlimited (e.g., document count on Standard tier).
    /// </summary>
    [JsonPropertyName("quota")]
    public long? Quota { get; set; }
}

/// <summary>
/// Service-level limits.
/// </summary>
public class ServiceLimits
{
    [JsonPropertyName("maxStoragePerIndex")]
    public long MaxStoragePerIndex { get; set; }

    [JsonPropertyName("maxFieldsPerIndex")]
    public int MaxFieldsPerIndex { get; set; }

    [JsonPropertyName("maxFieldNestingDepthPerIndex")]
    public int MaxFieldNestingDepthPerIndex { get; set; }

    [JsonPropertyName("maxComplexCollectionFieldsPerIndex")]
    public int MaxComplexCollectionFieldsPerIndex { get; set; }

    [JsonPropertyName("maxComplexObjectsInCollectionsPerDocument")]
    public int MaxComplexObjectsInCollectionsPerDocument { get; set; }
}
