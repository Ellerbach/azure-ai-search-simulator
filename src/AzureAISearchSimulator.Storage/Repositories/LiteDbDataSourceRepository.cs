using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of data source repository.
/// </summary>
public class LiteDbDataSourceRepository : IDataSourceRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<DataSourceDocument> _collection;
    private readonly ILogger<LiteDbDataSourceRepository> _logger;

    public LiteDbDataSourceRepository(
        IOptions<SimulatorSettings> settings,
        ILogger<LiteDbDataSourceRepository> logger)
    {
        _logger = logger;
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "datasources.db");
        _database = new LiteDatabase(dbPath);
        _collection = _database.GetCollection<DataSourceDocument>("datasources");
        _collection.EnsureIndex(x => x.Name, unique: true);
        
        _logger.LogInformation("LiteDB datasource repository initialized at {DbPath}", dbPath);
    }

    public Task<DataSource> CreateAsync(DataSource dataSource)
    {
        dataSource.ODataETag = GenerateETag();
        
        var doc = new DataSourceDocument
        {
            Id = ObjectId.NewObjectId(),
            Name = dataSource.Name,
            Data = System.Text.Json.JsonSerializer.Serialize(dataSource)
        };
        
        _collection.Insert(doc);
        _logger.LogDebug("Created datasource document: {Name}", doc.Name);
        return Task.FromResult(dataSource);
    }

    public Task<DataSource> UpdateAsync(DataSource dataSource)
    {
        dataSource.ODataETag = GenerateETag();
        
        var existing = _collection.FindOne(x => x.Name == dataSource.Name);
        if (existing != null)
        {
            existing.Data = System.Text.Json.JsonSerializer.Serialize(dataSource);
            _collection.Update(existing);
        }
        
        return Task.FromResult(dataSource);
    }

    public Task<DataSource?> GetAsync(string name)
    {
        var doc = _collection.FindOne(x => x.Name == name);
        if (doc == null)
        {
            return Task.FromResult<DataSource?>(null);
        }
        
        var dataSource = System.Text.Json.JsonSerializer.Deserialize<DataSource>(doc.Data);
        return Task.FromResult(dataSource);
    }

    public Task<IEnumerable<DataSource>> ListAsync()
    {
        var docs = _collection.FindAll().ToList();
        var dataSources = docs
            .Select(d => System.Text.Json.JsonSerializer.Deserialize<DataSource>(d.Data)!)
            .ToList();
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

    /// <summary>
    /// Internal document for storing data source as JSON.
    /// </summary>
    private class DataSourceDocument
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
}
