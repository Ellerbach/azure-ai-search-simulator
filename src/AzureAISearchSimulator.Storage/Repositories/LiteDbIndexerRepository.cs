using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of indexer repository.
/// </summary>
public class LiteDbIndexerRepository : IIndexerRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<IndexerDocument> _collection;
    private readonly ILiteCollection<IndexerStatusRecord> _statusCollection;
    private readonly ILogger<LiteDbIndexerRepository> _logger;

    public LiteDbIndexerRepository(
        IOptions<SimulatorSettings> settings,
        ILogger<LiteDbIndexerRepository> logger)
    {
        _logger = logger;
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "indexers.db");
        _database = new LiteDatabase(dbPath);
        _collection = _database.GetCollection<IndexerDocument>("indexers");
        _collection.EnsureIndex(x => x.Name, unique: true);
        
        _statusCollection = _database.GetCollection<IndexerStatusRecord>("indexer_status");
        _statusCollection.EnsureIndex(x => x.IndexerName, unique: true);
        
        _logger.LogInformation("LiteDB indexer repository initialized at {DbPath}", dbPath);
    }

    public Task<Indexer> CreateAsync(Indexer indexer)
    {
        indexer.ODataETag = GenerateETag();
        
        var doc = new IndexerDocument
        {
            Id = ObjectId.NewObjectId(),
            Name = indexer.Name,
            Data = System.Text.Json.JsonSerializer.Serialize(indexer)
        };
        
        _collection.Insert(doc);
        
        // Force checkpoint to ensure data is persisted
        _database.Checkpoint();
        
        // Verify the insert worked
        var count = _collection.Count();
        var verify = _collection.FindOne(x => x.Name == indexer.Name);
        _logger.LogInformation("Created indexer document: {Name}, Collection count: {Count}, Verify found: {Found}", 
            doc.Name, count, verify != null);
        
        // Initialize status
        var statusRecord = new IndexerStatusRecord
        {
            Id = ObjectId.NewObjectId(),
            IndexerName = indexer.Name,
            Status = new IndexerStatus()
        };
        _statusCollection.Insert(statusRecord);
        
        return Task.FromResult(indexer);
    }

    public Task<Indexer> UpdateAsync(Indexer indexer)
    {
        indexer.ODataETag = GenerateETag();
        
        var existing = _collection.FindOne(x => x.Name == indexer.Name);
        if (existing != null)
        {
            existing.Data = System.Text.Json.JsonSerializer.Serialize(indexer);
            _collection.Update(existing);
        }
        
        return Task.FromResult(indexer);
    }

    public Task<Indexer?> GetAsync(string name)
    {
        var count = _collection.Count();
        var allDocs = _collection.FindAll().ToList();
        _logger.LogInformation("GetAsync called for: {Name}, Collection count: {Count}, All names: [{Names}]", 
            name, count, string.Join(", ", allDocs.Select(d => d.Name)));
        
        var doc = _collection.FindOne(x => x.Name == name);
        if (doc == null)
        {
            _logger.LogWarning("Indexer not found: {Name}", name);
            return Task.FromResult<Indexer?>(null);
        }
        
        _logger.LogInformation("Found indexer: {Name}", name);
        var indexer = System.Text.Json.JsonSerializer.Deserialize<Indexer>(doc.Data);
        return Task.FromResult(indexer);
    }

    public Task<IEnumerable<Indexer>> ListAsync()
    {
        var docs = _collection.FindAll().ToList();
        var indexers = docs
            .Select(d => System.Text.Json.JsonSerializer.Deserialize<Indexer>(d.Data)!)
            .ToList();
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
                Id = ObjectId.NewObjectId(),
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
    /// Internal document for storing indexer data as JSON.
    /// </summary>
    private class IndexerDocument
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal record for storing indexer status.
    /// </summary>
    private class IndexerStatusRecord
    {
        public ObjectId Id { get; set; }
        public string IndexerName { get; set; } = string.Empty;
        public IndexerStatus Status { get; set; } = new();
    }
}
