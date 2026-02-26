using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service for evaluating scoring profiles and computing document boosts.
/// </summary>
public interface IScoringProfileService
{
    /// <summary>
    /// Resolves the active scoring profile for a search request.
    /// Returns null if no profile applies.
    /// </summary>
    /// <param name="index">The search index definition.</param>
    /// <param name="request">The search request.</param>
    /// <returns>The resolved scoring profile, or null.</returns>
    ScoringProfile? ResolveProfile(SearchIndex index, SearchRequest request);

    /// <summary>
    /// Calculates the document boost multiplier from scoring functions.
    /// Text weights are handled separately at query time via Lucene field boosts.
    /// </summary>
    /// <param name="profile">The active scoring profile.</param>
    /// <param name="documentFields">The document's field values.</param>
    /// <param name="scoringParameters">Parsed scoring parameters (name → value).</param>
    /// <returns>A boost multiplier (1.0 means no boost).</returns>
    double CalculateDocumentBoost(
        ScoringProfile profile,
        Dictionary<string, object?> documentFields,
        Dictionary<string, string> scoringParameters);

    /// <summary>
    /// Parses the scoring parameters from the request format ("name-value") into a dictionary.
    /// Handles the double-dash convention for negative geo coordinates.
    /// </summary>
    /// <param name="scoringParameters">Raw scoring parameters from the request.</param>
    /// <returns>Dictionary of parameter name to value.</returns>
    Dictionary<string, string> ParseScoringParameters(List<string>? scoringParameters);

    /// <summary>
    /// Gets the text weights (field boosts) from a scoring profile.
    /// Returns null if the profile has no text weights.
    /// </summary>
    /// <param name="profile">The scoring profile.</param>
    /// <returns>Dictionary of field name to boost weight, or null.</returns>
    Dictionary<string, double>? GetTextWeights(ScoringProfile? profile);
}
