using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service for managing indexers.
/// </summary>
public interface IIndexerService
{
    /// <summary>
    /// Creates a new indexer.
    /// </summary>
    Task<Indexer> CreateAsync(Indexer indexer);

    /// <summary>
    /// Creates or updates an indexer.
    /// </summary>
    Task<Indexer> CreateOrUpdateAsync(string name, Indexer indexer);

    /// <summary>
    /// Gets an indexer by name.
    /// </summary>
    Task<Indexer?> GetAsync(string name);

    /// <summary>
    /// Lists all indexers.
    /// </summary>
    Task<IEnumerable<Indexer>> ListAsync();

    /// <summary>
    /// Deletes an indexer.
    /// </summary>
    Task<bool> DeleteAsync(string name);

    /// <summary>
    /// Checks if an indexer exists.
    /// </summary>
    Task<bool> ExistsAsync(string name);

    /// <summary>
    /// Gets the current status of an indexer.
    /// </summary>
    Task<IndexerStatus> GetStatusAsync(string name);

    /// <summary>
    /// Runs an indexer immediately.
    /// </summary>
    Task RunAsync(string name);

    /// <summary>
    /// Resets an indexer (clears tracking state).
    /// </summary>
    Task ResetAsync(string name);
}
