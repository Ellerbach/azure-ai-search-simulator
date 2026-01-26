namespace AzureAISearchSimulator.Core.Configuration;

/// <summary>
/// Configuration settings for vector search.
/// </summary>
public class VectorSearchSettings
{
    public const string SectionName = "VectorSearchSettings";

    /// <summary>
    /// Default number of dimensions for vector fields.
    /// </summary>
    public int DefaultDimensions { get; set; } = 1536;

    /// <summary>
    /// Maximum number of vectors per index.
    /// </summary>
    public int MaxVectorsPerIndex { get; set; } = 100000;

    /// <summary>
    /// Similarity metric to use (cosine, euclidean, dotProduct).
    /// </summary>
    public string SimilarityMetric { get; set; } = "cosine";

    /// <summary>
    /// Whether to use HNSW algorithm for vector search (true) or brute-force (false).
    /// </summary>
    public bool UseHnsw { get; set; } = true;

    /// <summary>
    /// HNSW algorithm settings.
    /// </summary>
    public HnswSettings HnswSettings { get; set; } = new();

    /// <summary>
    /// Hybrid search settings for combining text and vector search.
    /// </summary>
    public HybridSearchSettings HybridSearchSettings { get; set; } = new();
}

/// <summary>
/// Configuration settings for HNSW algorithm parameters.
/// </summary>
public class HnswSettings
{
    /// <summary>
    /// Number of bi-directional links created for each element during construction.
    /// Higher values lead to better search quality but slower construction and more memory.
    /// Recommended values: 12-48, default: 16.
    /// </summary>
    public int M { get; set; } = 16;

    /// <summary>
    /// Size of the dynamic list used during construction.
    /// Higher values lead to better quality index but slower construction.
    /// Recommended values: 100-500, default: 200.
    /// </summary>
    public int EfConstruction { get; set; } = 200;

    /// <summary>
    /// Size of the dynamic list used during search.
    /// Higher values lead to better recall but slower search.
    /// Recommended values: 50-500, default: 100.
    /// </summary>
    public int EfSearch { get; set; } = 100;

    /// <summary>
    /// Multiplier for oversampling during filtered vector search.
    /// When filtering, retrieve k * OversampleMultiplier candidates to ensure enough results after filtering.
    /// Default: 5.
    /// </summary>
    public int OversampleMultiplier { get; set; } = 5;

    /// <summary>
    /// Random seed for reproducible index construction.
    /// Set to -1 for random seed.
    /// </summary>
    public int RandomSeed { get; set; } = 42;
}

/// <summary>
/// Configuration settings for hybrid search (combining text and vector search).
/// </summary>
public class HybridSearchSettings
{
    /// <summary>
    /// Default fusion method for combining text and vector scores.
    /// Options: "RRF" (Reciprocal Rank Fusion), "Weighted" (weighted combination).
    /// </summary>
    public string DefaultFusionMethod { get; set; } = "RRF";

    /// <summary>
    /// Default weight for vector search scores in weighted fusion (0.0-1.0).
    /// </summary>
    public double DefaultVectorWeight { get; set; } = 0.7;

    /// <summary>
    /// Default weight for text search scores in weighted fusion (0.0-1.0).
    /// </summary>
    public double DefaultTextWeight { get; set; } = 0.3;

    /// <summary>
    /// RRF constant k used in the formula: 1 / (k + rank).
    /// Higher values give more weight to lower-ranked results.
    /// Default: 60 (commonly used value).
    /// </summary>
    public int RrfK { get; set; } = 60;
}
