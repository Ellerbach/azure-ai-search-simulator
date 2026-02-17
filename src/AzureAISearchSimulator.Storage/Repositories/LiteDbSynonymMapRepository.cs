using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of synonym map repository.
/// </summary>
public class LiteDbSynonymMapRepository : ISynonymMapRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILogger<LiteDbSynonymMapRepository> _logger;
    private const string CollectionName = "synonymmaps";

    public LiteDbSynonymMapRepository(
        IOptions<SimulatorSettings> settings,
        ILogger<LiteDbSynonymMapRepository> logger)
    {
        _logger = logger;

        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "synonymmaps.db");
        _database = new LiteDatabase(dbPath);

        // Ensure index on name field
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        collection.EnsureIndex(x => x.Name, unique: true);

        _logger.LogInformation("LiteDB synonym map repository initialized at {DbPath}", dbPath);
    }

    public Task<SynonymMap> CreateAsync(SynonymMap synonymMap)
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);

        var document = SynonymMapDocument.FromSynonymMap(synonymMap);
        document.ETag = GenerateETag();
        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        collection.Insert(document);

        _logger.LogDebug("Created synonym map '{Name}'", synonymMap.Name);

        var result = document.ToSynonymMap();
        return Task.FromResult(result);
    }

    public Task<SynonymMap?> GetByNameAsync(string name)
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        var document = collection.FindOne(x => x.Name == name);

        return Task.FromResult(document?.ToSynonymMap());
    }

    public Task<IEnumerable<SynonymMap>> GetAllAsync()
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        var documents = collection.FindAll();

        var synonymMaps = documents.Select(d => d.ToSynonymMap());
        return Task.FromResult(synonymMaps);
    }

    public Task<SynonymMap> UpdateAsync(SynonymMap synonymMap)
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        var existing = collection.FindOne(x => x.Name == synonymMap.Name);

        if (existing == null)
        {
            throw new InvalidOperationException($"Synonym map '{synonymMap.Name}' not found");
        }

        var document = SynonymMapDocument.FromSynonymMap(synonymMap);
        document.Id = existing.Id;
        document.ETag = GenerateETag();
        document.CreatedAt = existing.CreatedAt;
        document.UpdatedAt = DateTime.UtcNow;

        collection.Update(document);

        _logger.LogDebug("Updated synonym map '{Name}'", synonymMap.Name);

        return Task.FromResult(document.ToSynonymMap());
    }

    public Task<bool> DeleteAsync(string name)
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        var deleted = collection.DeleteMany(x => x.Name == name);

        _logger.LogDebug("Deleted synonym map '{Name}': {Deleted}", name, deleted > 0);

        return Task.FromResult(deleted > 0);
    }

    public Task<bool> ExistsAsync(string name)
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        var exists = collection.Exists(x => x.Name == name);

        return Task.FromResult(exists);
    }

    public Task<int> CountAsync()
    {
        var collection = _database.GetCollection<SynonymMapDocument>(CollectionName);
        return Task.FromResult(collection.Count());
    }

    public void Dispose()
    {
        _database?.Dispose();
    }

    private static string GenerateETag()
    {
        return $"\"{Guid.NewGuid():N}\"";
    }
}

/// <summary>
/// Internal document representation for LiteDB storage.
/// </summary>
internal class SynonymMapDocument
{
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = "solr";
    public string Synonyms { get; set; } = string.Empty;
    public EncryptionKey? EncryptionKey { get; set; }
    public string? ETag { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static SynonymMapDocument FromSynonymMap(SynonymMap synonymMap)
    {
        return new SynonymMapDocument
        {
            Name = synonymMap.Name,
            Format = synonymMap.Format,
            Synonyms = synonymMap.Synonyms,
            EncryptionKey = synonymMap.EncryptionKey
        };
    }

    public SynonymMap ToSynonymMap()
    {
        return new SynonymMap
        {
            Name = Name,
            Format = Format,
            Synonyms = Synonyms,
            EncryptionKey = EncryptionKey,
            ETag = ETag
        };
    }
}
