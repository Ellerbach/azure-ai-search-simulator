using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// Repository interface for search index persistence.
/// </summary>
public interface IIndexRepository
{
    Task<SearchIndex> CreateAsync(SearchIndex index, CancellationToken cancellationToken = default);
    Task<SearchIndex> UpdateAsync(SearchIndex index, CancellationToken cancellationToken = default);
    Task<SearchIndex?> GetByNameAsync(string indexName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchIndex>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string indexName, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string indexName, CancellationToken cancellationToken = default);
}
