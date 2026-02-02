using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Credentials;
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
    /// Also registers the credential factory for authentication.
    /// </summary>
    public static IServiceCollection AddAzureDataSourceConnectors(this IServiceCollection services)
    {
        // Register credential factory for outbound authentication
        services.AddSingleton<ICredentialFactory, CredentialFactory>();
        
        // Register Azure Blob Storage connector
        services.AddSingleton<IDataSourceConnector, AzureBlobStorageConnector>();
        
        // Register ADLS Gen2 connector
        services.AddSingleton<IDataSourceConnector, AdlsGen2Connector>();
        
        return services;
    }
}
