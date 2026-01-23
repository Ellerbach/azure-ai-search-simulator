using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service for managing data sources.
/// </summary>
public interface IDataSourceService
{
    /// <summary>
    /// Creates a new data source.
    /// </summary>
    Task<DataSource> CreateAsync(DataSource dataSource);

    /// <summary>
    /// Creates or updates a data source.
    /// </summary>
    Task<DataSource> CreateOrUpdateAsync(string name, DataSource dataSource);

    /// <summary>
    /// Gets a data source by name.
    /// </summary>
    Task<DataSource?> GetAsync(string name);

    /// <summary>
    /// Lists all data sources.
    /// </summary>
    Task<IEnumerable<DataSource>> ListAsync();

    /// <summary>
    /// Deletes a data source.
    /// </summary>
    Task<bool> DeleteAsync(string name);

    /// <summary>
    /// Checks if a data source exists.
    /// </summary>
    Task<bool> ExistsAsync(string name);
}
