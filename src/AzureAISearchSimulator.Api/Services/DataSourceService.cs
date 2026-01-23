using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.DataSources;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Service for managing data sources.
/// </summary>
public class DataSourceService : IDataSourceService
{
    private readonly IDataSourceRepository _repository;
    private readonly IDataSourceConnectorFactory _connectorFactory;
    private readonly ILogger<DataSourceService> _logger;

    public DataSourceService(
        IDataSourceRepository repository,
        IDataSourceConnectorFactory connectorFactory,
        ILogger<DataSourceService> logger)
    {
        _repository = repository;
        _connectorFactory = connectorFactory;
        _logger = logger;
    }

    public async Task<DataSource> CreateAsync(DataSource dataSource)
    {
        ValidateDataSource(dataSource);

        if (await _repository.ExistsAsync(dataSource.Name))
        {
            throw new InvalidOperationException($"Data source '{dataSource.Name}' already exists.");
        }

        _logger.LogInformation("Creating data source: {Name} (type: {Type})", 
            dataSource.Name, dataSource.Type);

        return await _repository.CreateAsync(dataSource);
    }

    public async Task<DataSource> CreateOrUpdateAsync(string name, DataSource dataSource)
    {
        dataSource.Name = name;
        ValidateDataSource(dataSource);

        _logger.LogInformation("Creating or updating data source: {Name} (type: {Type})", 
            dataSource.Name, dataSource.Type);

        if (await _repository.ExistsAsync(name))
        {
            return await _repository.UpdateAsync(dataSource);
        }

        return await _repository.CreateAsync(dataSource);
    }

    public async Task<DataSource?> GetAsync(string name)
    {
        return await _repository.GetAsync(name);
    }

    public async Task<IEnumerable<DataSource>> ListAsync()
    {
        return await _repository.ListAsync();
    }

    public async Task<bool> DeleteAsync(string name)
    {
        _logger.LogInformation("Deleting data source: {Name}", name);
        return await _repository.DeleteAsync(name);
    }

    public async Task<bool> ExistsAsync(string name)
    {
        return await _repository.ExistsAsync(name);
    }

    private void ValidateDataSource(DataSource dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource.Name))
        {
            throw new ArgumentException("Data source name is required.");
        }

        if (string.IsNullOrWhiteSpace(dataSource.Type))
        {
            throw new ArgumentException("Data source type is required.");
        }

        if (!_connectorFactory.SupportsType(dataSource.Type))
        {
            throw new ArgumentException($"Data source type '{dataSource.Type}' is not supported.");
        }

        if (dataSource.Container == null || string.IsNullOrWhiteSpace(dataSource.Container.Name))
        {
            throw new ArgumentException("Data source container name is required.");
        }
    }
}
