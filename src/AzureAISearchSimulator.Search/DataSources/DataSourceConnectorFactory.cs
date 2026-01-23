using AzureAISearchSimulator.Core.Models;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Search.DataSources;

/// <summary>
/// Factory for creating data source connectors based on type.
/// </summary>
public interface IDataSourceConnectorFactory
{
    IDataSourceConnector GetConnector(string dataSourceType);
    bool SupportsType(string dataSourceType);
}

/// <summary>
/// Default implementation of connector factory.
/// </summary>
public class DataSourceConnectorFactory : IDataSourceConnectorFactory
{
    private readonly ILogger<DataSourceConnectorFactory> _logger;
    private readonly IEnumerable<IDataSourceConnector> _connectors;

    public DataSourceConnectorFactory(
        ILogger<DataSourceConnectorFactory> logger,
        IEnumerable<IDataSourceConnector> connectors)
    {
        _logger = logger;
        _connectors = connectors;
    }

    public IDataSourceConnector GetConnector(string dataSourceType)
    {
        var connector = _connectors.FirstOrDefault(c => 
            c.Type.Equals(dataSourceType, StringComparison.OrdinalIgnoreCase));
        
        if (connector == null)
        {
            _logger.LogError("No connector found for data source type: {Type}", dataSourceType);
            throw new NotSupportedException($"Data source type '{dataSourceType}' is not supported. " +
                $"Supported types: {string.Join(", ", _connectors.Select(c => c.Type))}");
        }

        return connector;
    }

    public bool SupportsType(string dataSourceType)
    {
        return _connectors.Any(c => c.Type.Equals(dataSourceType, StringComparison.OrdinalIgnoreCase));
    }
}
