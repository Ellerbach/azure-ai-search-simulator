using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureAISearchSimulator.Core.Configuration;

namespace AzureAISearchSimulator.Search.Hnsw;

/// <summary>
/// Service for combining text and vector search results using RRF or weighted fusion.
/// </summary>
public class HybridSearchService : IHybridSearchService
{
    private readonly ILogger<HybridSearchService> _logger;
    private readonly HybridSearchSettings _settings;

    public HybridSearchService(
        ILogger<HybridSearchService> logger,
        IOptions<VectorSearchSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value.HybridSearchSettings;
    }

    /// <inheritdoc />
    public IReadOnlyList<HybridSearchResult> FuseResults(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        FusionMethod method = FusionMethod.RRF,
        double vectorWeight = 0.7,
        double textWeight = 0.3,
        int rrfK = 60,
        int topK = 50)
    {
        return method switch
        {
            FusionMethod.RRF => FuseWithRRF(textResults, vectorResults, rrfK, topK),
            FusionMethod.Weighted => FuseWithWeightedScores(textResults, vectorResults, vectorWeight, textWeight, topK),
            _ => FuseWithRRF(textResults, vectorResults, rrfK, topK)
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<HybridSearchResult> FuseWithRRF(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        int k = 60,
        int topK = 50)
    {
        var textList = textResults.ToList();
        var vectorList = vectorResults.ToList();

        _logger.LogDebug("Fusing {TextCount} text results with {VectorCount} vector results using RRF (k={K})",
            textList.Count, vectorList.Count, k);

        // Build rank maps (1-indexed ranks)
        var textRanks = new Dictionary<string, int>();
        for (int i = 0; i < textList.Count; i++)
        {
            textRanks[textList[i].DocumentId] = i + 1;
        }

        var vectorRanks = new Dictionary<string, int>();
        for (int i = 0; i < vectorList.Count; i++)
        {
            vectorRanks[vectorList[i].DocumentId] = i + 1;
        }

        // Collect all unique document IDs
        var allDocIds = textRanks.Keys.Union(vectorRanks.Keys).ToHashSet();

        // Calculate RRF scores
        var results = new List<HybridSearchResult>();
        foreach (var docId in allDocIds)
        {
            var textRank = textRanks.GetValueOrDefault(docId, 0);
            var vectorRank = vectorRanks.GetValueOrDefault(docId, 0);

            // RRF formula: sum(1 / (k + rank)) for each result set where document appears
            double rrfScore = 0;
            if (textRank > 0)
            {
                rrfScore += 1.0 / (k + textRank);
            }
            if (vectorRank > 0)
            {
                rrfScore += 1.0 / (k + vectorRank);
            }

            // Get original scores
            var textScore = textRank > 0 ? textList[textRank - 1].Score : 0;
            var vectorScore = vectorRank > 0 ? vectorList[vectorRank - 1].Score : 0;

            results.Add(new HybridSearchResult
            {
                DocumentId = docId,
                Score = rrfScore,
                TextScore = textScore,
                VectorScore = vectorScore,
                TextRank = textRank,
                VectorRank = vectorRank
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<HybridSearchResult> FuseWithWeightedScores(
        IEnumerable<(string DocumentId, double Score)> textResults,
        IEnumerable<VectorSearchResult> vectorResults,
        double vectorWeight = 0.7,
        double textWeight = 0.3,
        int topK = 50)
    {
        var textList = textResults.ToList();
        var vectorList = vectorResults.ToList();

        _logger.LogDebug("Fusing {TextCount} text results with {VectorCount} vector results using Weighted (text={TextWeight}, vector={VectorWeight})",
            textList.Count, vectorList.Count, textWeight, vectorWeight);

        // Normalize text scores using min-max normalization
        var normalizedTextScores = NormalizeScores(textList.Select(t => (t.DocumentId, t.Score)));

        // Vector scores are already normalized (0-1) from the VectorSearchService
        var normalizedVectorScores = vectorList.ToDictionary(v => v.DocumentId, v => v.Score);

        // Build rank maps for reference
        var textRanks = new Dictionary<string, int>();
        for (int i = 0; i < textList.Count; i++)
        {
            textRanks[textList[i].DocumentId] = i + 1;
        }

        var vectorRanks = new Dictionary<string, int>();
        for (int i = 0; i < vectorList.Count; i++)
        {
            vectorRanks[vectorList[i].DocumentId] = i + 1;
        }

        // Collect all unique document IDs
        var allDocIds = normalizedTextScores.Keys.Union(normalizedVectorScores.Keys).ToHashSet();

        // Calculate weighted scores
        var results = new List<HybridSearchResult>();
        foreach (var docId in allDocIds)
        {
            var normalizedTextScore = normalizedTextScores.GetValueOrDefault(docId, 0);
            var normalizedVectorScore = normalizedVectorScores.GetValueOrDefault(docId, 0);

            // Weighted combination
            var finalScore = (normalizedTextScore * textWeight) + (normalizedVectorScore * vectorWeight);

            // Get original scores
            var textRank = textRanks.GetValueOrDefault(docId, 0);
            var vectorRank = vectorRanks.GetValueOrDefault(docId, 0);
            var originalTextScore = textRank > 0 ? textList[textRank - 1].Score : 0;
            var originalVectorScore = vectorRank > 0 ? vectorList[vectorRank - 1].Score : 0;

            results.Add(new HybridSearchResult
            {
                DocumentId = docId,
                Score = finalScore,
                TextScore = originalTextScore,
                VectorScore = originalVectorScore,
                TextRank = textRank,
                VectorRank = vectorRank
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Normalizes scores to 0-1 range using min-max normalization.
    /// </summary>
    private static Dictionary<string, double> NormalizeScores(IEnumerable<(string DocumentId, double Score)> scores)
    {
        var scoreList = scores.ToList();
        if (scoreList.Count == 0)
        {
            return new Dictionary<string, double>();
        }

        var minScore = scoreList.Min(s => s.Score);
        var maxScore = scoreList.Max(s => s.Score);
        var range = maxScore - minScore;

        // Handle edge case where all scores are the same
        if (range == 0)
        {
            return scoreList.ToDictionary(s => s.DocumentId, _ => 1.0);
        }

        return scoreList.ToDictionary(
            s => s.DocumentId,
            s => (s.Score - minScore) / range);
    }
}
