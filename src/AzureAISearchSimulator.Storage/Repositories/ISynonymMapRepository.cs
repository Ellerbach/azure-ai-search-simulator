using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// Interface for synonym map persistence.
/// </summary>
public interface ISynonymMapRepository
{
    Task<SynonymMap> CreateAsync(SynonymMap synonymMap);
    Task<SynonymMap?> GetByNameAsync(string name);
    Task<IEnumerable<SynonymMap>> GetAllAsync();
    Task<SynonymMap> UpdateAsync(SynonymMap synonymMap);
    Task<bool> DeleteAsync(string name);
    Task<bool> ExistsAsync(string name);
    Task<int> CountAsync();
}
