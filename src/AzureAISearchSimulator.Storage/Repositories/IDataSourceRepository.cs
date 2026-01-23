using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// Repository for data source metadata storage.
/// </summary>
public interface IDataSourceRepository
{
    Task<DataSource> CreateAsync(DataSource dataSource);
    Task<DataSource> UpdateAsync(DataSource dataSource);
    Task<DataSource?> GetAsync(string name);
    Task<IEnumerable<DataSource>> ListAsync();
    Task<bool> DeleteAsync(string name);
    Task<bool> ExistsAsync(string name);
}
