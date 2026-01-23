using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of indexer repository.
/// </summary>
public class LiteDbIndexerRepository : IIndexerRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<Indexer> _collection;
    private readonly ILiteCollection<IndexerStatusRecord> _statusCollection;

    public LiteDbIndexerRepository(IOptions<SimulatorSettings> settings)
    {
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "indexers.db");
        _database = new LiteDatabase(dbPath);
        _collection = _database.GetCollection<Indexer>("indexers");
        _collection.EnsureIndex(x => x.Name, unique: true);
        
        _statusCollection = _database.GetCollection<IndexerStatusRecord>("indexer_status");
        _statusCollection.EnsureIndex(x => x.IndexerName, unique: true);
    }

    public Task<Indexer> CreateAsync(Indexer indexer)
    {
        indexer.ODataETag = GenerateETag();
        _collection.Insert(indexer);
        
        // Initialize status
        var statusRecord = new IndexerStatusRecord
        {
            IndexerName = indexer.Name,
            Status = new IndexerStatus()
        };
        _statusCollection.Insert(statusRecord);
        
        return Task.FromResult(indexer);
    }

    public Task<Indexer> UpdateAsync(Indexer indexer)
    {
        indexer.ODataETag = GenerateETag();
        _collection.Update(indexer);
        return Task.FromResult(indexer);
    }

    public Task<Indexer?> GetAsync(string name)
    {
        var indexer = _collection.FindOne(x => x.Name == name);
        return Task.FromResult(indexer);
    }

    public Task<IEnumerable<Indexer>> ListAsync()
    {
        var indexers = _collection.FindAll().ToList();
        return Task.FromResult<IEnumerable<Indexer>>(indexers);
    }

    public Task<bool> DeleteAsync(string name)
    {
        _statusCollection.DeleteMany(x => x.IndexerName == name);
        var deleted = _collection.DeleteMany(x => x.Name == name) > 0;
        return Task.FromResult(deleted);
    }

    public Task<bool> ExistsAsync(string name)
    {
        var exists = _collection.Exists(x => x.Name == name);
        return Task.FromResult(exists);
    }

    public Task<IndexerStatus?> GetStatusAsync(string name)
    {
        var record = _statusCollection.FindOne(x => x.IndexerName == name);
        return Task.FromResult(record?.Status);
    }

    public Task SaveStatusAsync(string name, IndexerStatus status)
    {
        var record = _statusCollection.FindOne(x => x.IndexerName == name);
        if (record != null)
        {
            record.Status = status;
            _statusCollection.Update(record);
        }
        else
        {
            _statusCollection.Insert(new IndexerStatusRecord
            {
                IndexerName = name,
                Status = status
            });
        }
        return Task.CompletedTask;
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
    /// Internal record for storing indexer status.
    /// </summary>
    private class IndexerStatusRecord
    {
        public int Id { get; set; }
        public string IndexerName { get; set; } = string.Empty;
        public IndexerStatus Status { get; set; } = new();
    }
}
