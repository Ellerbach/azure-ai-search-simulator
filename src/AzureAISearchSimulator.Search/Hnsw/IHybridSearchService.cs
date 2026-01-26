namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Result of a hybrid search combining text and vector results.
/// </summary>
public class HybridSearchResult
{
    /// <summary>
    /// Document identifier.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Final fused score (higher is better).
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Original text search score (0 if not in text results).
    /// </summary>
    public double TextScore { get; init; }

    /// <summary>
    /// Original vector search score (0 if not in vector results).
    /// </summary>
    public double VectorScore { get; init; }

    /// <summary>
    /// Rank in text search results (0 if not in text results).
    /// </summary>
    public int TextRank { get; init; }

    /// <summary>
    /// Rank in vector search results (0 if not in vector results).
    /// </summary>
    public int VectorRank { get; init; }
}

/// <summary>
/// Method used to fuse text and vector search results.
/// </summary>
public enum FusionMethod
{
    /// <summary>
    /// Reciprocal Rank Fusion: score = sum(1 / (k + rank)) for each result set.
    /// </summary>
    RRF,

    /// <summary>
    /// Weighted combination of normalized scores.
    /// </summary>
    Weighted
}

/// <summary>
/// Service for combining text and vector search results using various fusion methods.
/// </summary>
public interface IHybridSearchService
{
    /// <summary>
    /// Combines text and vector search results using the specified fusion method.
    /// </summary>
    /// <param name="textResults">Text search results with (documentId, score) pairs.</param>
    /// <param name="vectorResults">Vector search results.</param>
    /// <param name="method">Fusion method to use.</param>
    /// <param name="vectorWeight">Weight for vector scores (0.0-1.0) when using Weighted fusion.</param>
    /// <param name="textWeight">Weight for text scores (0.0-1.0) when using Weighted fusion.</param>
    /// <param name="rrfK">Constant k for RRF formula (default: 60).</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <returns>Fused results ordered by score (descending).</returns>
    IReadOnlyList<HybridSearchResult> FuseResults(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        FusionMethod method = FusionMethod.RRF,
        double vectorWeight = 0.7,
        double textWeight = 0.3,
        int rrfK = 60,
        int topK = 50);

    /// <summary>
    /// Combines text and vector search results using Reciprocal Rank Fusion (RRF).
    /// RRF score = sum(1 / (k + rank)) for each result set.
    /// </summary>
    IReadOnlyList<HybridSearchResult> FuseWithRRF(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        int k = 60,
        int topK = 50);

    /// <summary>
    /// Combines text and vector search results using weighted score combination.
    /// Final score = (normalizedTextScore * textWeight) + (normalizedVectorScore * vectorWeight)
    /// </summary>
    IReadOnlyList<HybridSearchResult> FuseWithWeightedScores(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        double vectorWeight = 0.7,
        double textWeight = 0.3,
        int topK = 50);
}
