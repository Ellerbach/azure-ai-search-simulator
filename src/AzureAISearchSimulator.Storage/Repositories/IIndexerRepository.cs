using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// Repository for indexer metadata storage.
/// </summary>
public interface IIndexerRepository
{
    Task<Indexer> CreateAsync(Indexer indexer);
    Task<Indexer> UpdateAsync(Indexer indexer);
    Task<Indexer?> GetAsync(string name);
    Task<IEnumerable<Indexer>> ListAsync();
    Task<bool> DeleteAsync(string name);
    Task<bool> ExistsAsync(string name);
    
    // Status tracking
    Task<IndexerStatus?> GetStatusAsync(string name);
    Task SaveStatusAsync(string name, IndexerStatus status);
}
