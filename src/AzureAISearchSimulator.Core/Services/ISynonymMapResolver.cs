namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Interface for resolving synonyms at query time.
/// Used by the search service to expand search terms using synonym maps.
/// </summary>
public interface ISynonymMapResolver
{
    /// <summary>
    /// Expands a search term using the synonym maps assigned to a field.
    /// Returns all original terms plus their synonym expansions.
    /// </summary>
    /// <param name="synonymMapNames">The synonym map names configured on the field.</param>
    /// <param name="term">The search term to expand.</param>
    /// <returns>The original term plus all matching synonyms.</returns>
    IReadOnlyList<string> ExpandTerms(IEnumerable<string> synonymMapNames, string term);
}
