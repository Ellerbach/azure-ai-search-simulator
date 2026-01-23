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
    public int MaxVectorsPerIndex { get; set; } = 50000;

    /// <summary>
    /// Similarity metric to use (cosine, euclidean, dotProduct).
    /// </summary>
    public string SimilarityMetric { get; set; } = "cosine";
}
