using AzureAISearchSimulator.Search.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace AzureAISearchSimulator.DataSources;

/// <summary>
/// Extension methods for registering data source connectors with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure data source connectors (Blob Storage, ADLS Gen2).
    /// </summary>
    public static IServiceCollection AddAzureDataSourceConnectors(this IServiceCollection services)
    {
        // Register Azure Blob Storage connector
        services.AddSingleton<IDataSourceConnector, AzureBlobStorageConnector>();
        
        // Register ADLS Gen2 connector
        services.AddSingleton<IDataSourceConnector, AdlsGen2Connector>();
        
        return services;
    }
}
