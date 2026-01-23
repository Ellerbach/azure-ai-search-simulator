using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of data source repository.
/// </summary>
public class LiteDbDataSourceRepository : IDataSourceRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<DataSource> _collection;

    public LiteDbDataSourceRepository(IOptions<SimulatorSettings> settings)
    {
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "datasources.db");
        _database = new LiteDatabase(dbPath);
        _collection = _database.GetCollection<DataSource>("datasources");
        _collection.EnsureIndex(x => x.Name, unique: true);
    }

    public Task<DataSource> CreateAsync(DataSource dataSource)
    {
        dataSource.ODataETag = GenerateETag();
        _collection.Insert(dataSource);
        return Task.FromResult(dataSource);
    }

    public Task<DataSource> UpdateAsync(DataSource dataSource)
    {
        dataSource.ODataETag = GenerateETag();
        _collection.Update(dataSource);
        return Task.FromResult(dataSource);
    }

    public Task<DataSource?> GetAsync(string name)
    {
        var dataSource = _collection.FindOne(x => x.Name == name);
        return Task.FromResult(dataSource);
    }

    public Task<IEnumerable<DataSource>> ListAsync()
    {
        var dataSources = _collection.FindAll().ToList();
        return Task.FromResult<IEnumerable<DataSource>>(dataSources);
    }

    public Task<bool> DeleteAsync(string name)
    {
        var deleted = _collection.DeleteMany(x => x.Name == name) > 0;
        return Task.FromResult(deleted);
    }

    public Task<bool> ExistsAsync(string name)
    {
        var exists = _collection.Exists(x => x.Name == name);
        return Task.FromResult(exists);
    }

    private static string GenerateETag()
    {
        return $"\"{Guid.NewGuid():N}\"";
    }

    public void Dispose()
    {
        _database.Dispose();
    }
}
