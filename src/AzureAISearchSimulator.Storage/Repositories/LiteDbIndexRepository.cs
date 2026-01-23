using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of index repository.
/// </summary>
public class LiteDbIndexRepository : IIndexRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<IndexDocument> _collection;
    private readonly ILogger<LiteDbIndexRepository> _logger;
    private const string CollectionName = "indexes";

    public LiteDbIndexRepository(
        IOptions<SimulatorSettings> settings,
        ILogger<LiteDbIndexRepository> logger)
    {
        _logger = logger;
        
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "simulator.db");
        _database = new LiteDatabase(dbPath);
        _collection = _database.GetCollection<IndexDocument>(CollectionName);
        
        // Create index on Name for fast lookups
        _collection.EnsureIndex(x => x.Name, unique: true);
        
        _logger.LogInformation("LiteDB index repository initialized at {DbPath}", dbPath);
    }

    public Task<SearchIndex> CreateAsync(SearchIndex index, CancellationToken cancellationToken = default)
    {
        var doc = ToDocument(index);
        doc.CreatedAt = DateTime.UtcNow;
        doc.ModifiedAt = DateTime.UtcNow;
        doc.ETag = GenerateETag();
        
        _collection.Insert(doc);
        _logger.LogInformation("Created index {IndexName}", index.Name);
        
        return Task.FromResult(FromDocument(doc));
    }

    public Task<SearchIndex> UpdateAsync(SearchIndex index, CancellationToken cancellationToken = default)
    {
        var existing = _collection.FindOne(x => x.Name == index.Name);
        if (existing == null)
        {
            throw new InvalidOperationException($"Index '{index.Name}' not found");
        }

        var doc = ToDocument(index);
        doc.Id = existing.Id;
        doc.CreatedAt = existing.CreatedAt;
        doc.ModifiedAt = DateTime.UtcNow;
        doc.ETag = GenerateETag();
        
        _collection.Update(doc);
        _logger.LogInformation("Updated index {IndexName}", index.Name);
        
        return Task.FromResult(FromDocument(doc));
    }

    public Task<SearchIndex?> GetByNameAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var doc = _collection.FindOne(x => x.Name == indexName);
        return Task.FromResult(doc != null ? FromDocument(doc) : null);
    }

    public Task<IReadOnlyList<SearchIndex>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var docs = _collection.FindAll();
        var indexes = docs.Select(FromDocument).ToList();
        return Task.FromResult<IReadOnlyList<SearchIndex>>(indexes);
    }

    public Task<bool> DeleteAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var deleted = _collection.DeleteMany(x => x.Name == indexName);
        if (deleted > 0)
        {
            _logger.LogInformation("Deleted index {IndexName}", indexName);
        }
        return Task.FromResult(deleted > 0);
    }

    public Task<bool> ExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        var exists = _collection.Exists(x => x.Name == indexName);
        return Task.FromResult(exists);
    }

    private static string GenerateETag()
    {
        return $"\"{Guid.NewGuid():N}\"";
    }

    private static IndexDocument ToDocument(SearchIndex index)
    {
        return new IndexDocument
        {
            Name = index.Name,
            IndexJson = JsonSerializer.Serialize(index),
            CreatedAt = index.CreatedAt,
            ModifiedAt = index.ModifiedAt,
            ETag = index.ETag ?? GenerateETag()
        };
    }

    private static SearchIndex FromDocument(IndexDocument doc)
    {
        var index = JsonSerializer.Deserialize<SearchIndex>(doc.IndexJson) 
            ?? throw new InvalidOperationException("Failed to deserialize index");
        index.CreatedAt = doc.CreatedAt;
        index.ModifiedAt = doc.ModifiedAt;
        index.ETag = doc.ETag;
        return index;
    }

    public void Dispose()
    {
        _database?.Dispose();
    }
}

/// <summary>
/// Internal document for LiteDB storage.
/// </summary>
internal class IndexDocument
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IndexJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string ETag { get; set; } = string.Empty;
}
