using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Interface for synonym map management operations.
/// </summary>
public interface ISynonymMapService
{
    /// <summary>
    /// Creates a new synonym map.
    /// </summary>
    Task<SynonymMap> CreateAsync(SynonymMap synonymMap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a synonym map by name.
    /// </summary>
    Task<SynonymMap?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all synonym maps.
    /// </summary>
    Task<IEnumerable<SynonymMap>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a synonym map.
    /// </summary>
    Task<SynonymMap> CreateOrUpdateAsync(string name, SynonymMap synonymMap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a synonym map.
    /// </summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a synonym map exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of synonym maps.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the expanded synonyms for a given term using a specific synonym map.
    /// Returns the original term plus all its synonyms.
    /// </summary>
    IReadOnlyList<string> GetSynonyms(string synonymMapName, string term);

    /// <summary>
    /// Expands a list of search terms using the synonym maps assigned to a field.
    /// Returns all original terms plus their synonyms.
    /// </summary>
    IReadOnlyList<string> ExpandTerms(IEnumerable<string> synonymMapNames, string term);
}
