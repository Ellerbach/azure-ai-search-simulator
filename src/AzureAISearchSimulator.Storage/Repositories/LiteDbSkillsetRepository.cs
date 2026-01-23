using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// LiteDB implementation of skillset repository.
/// </summary>
public class LiteDbSkillsetRepository : ISkillsetRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILogger<LiteDbSkillsetRepository> _logger;
    private const string CollectionName = "skillsets";

    public LiteDbSkillsetRepository(
        IOptions<SimulatorSettings> settings,
        ILogger<LiteDbSkillsetRepository> logger)
    {
        _logger = logger;
        
        var dataDir = settings.Value.DataDirectory;
        Directory.CreateDirectory(dataDir);
        
        var dbPath = Path.Combine(dataDir, "skillsets.db");
        _database = new LiteDatabase(dbPath);
        
        // Ensure index on name field
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        collection.EnsureIndex(x => x.Name, unique: true);
        
        _logger.LogInformation("LiteDB skillset repository initialized at {DbPath}", dbPath);
    }

    public Task<Skillset> CreateAsync(Skillset skillset)
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        
        var document = SkillsetDocument.FromSkillset(skillset);
        document.ETag = GenerateETag();
        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        collection.Insert(document);
        
        _logger.LogDebug("Created skillset '{Name}'", skillset.Name);
        
        var result = document.ToSkillset();
        return Task.FromResult(result);
    }

    public Task<Skillset?> GetByNameAsync(string name)
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        var document = collection.FindOne(x => x.Name == name);
        
        return Task.FromResult(document?.ToSkillset());
    }

    public Task<IEnumerable<Skillset>> GetAllAsync()
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        var documents = collection.FindAll();
        
        var skillsets = documents.Select(d => d.ToSkillset());
        return Task.FromResult(skillsets);
    }

    public Task<Skillset> UpdateAsync(Skillset skillset)
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        var existing = collection.FindOne(x => x.Name == skillset.Name);

        if (existing == null)
        {
            throw new InvalidOperationException($"Skillset '{skillset.Name}' not found");
        }

        var document = SkillsetDocument.FromSkillset(skillset);
        document.Id = existing.Id;
        document.ETag = GenerateETag();
        document.CreatedAt = existing.CreatedAt;
        document.UpdatedAt = DateTime.UtcNow;

        collection.Update(document);
        
        _logger.LogDebug("Updated skillset '{Name}'", skillset.Name);

        return Task.FromResult(document.ToSkillset());
    }

    public Task<bool> DeleteAsync(string name)
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        var deleted = collection.DeleteMany(x => x.Name == name);
        
        _logger.LogDebug("Deleted skillset '{Name}': {Deleted}", name, deleted > 0);
        
        return Task.FromResult(deleted > 0);
    }

    public Task<bool> ExistsAsync(string name)
    {
        var collection = _database.GetCollection<SkillsetDocument>(CollectionName);
        var exists = collection.Exists(x => x.Name == name);
        
        return Task.FromResult(exists);
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
internal class SkillsetDocument
{
    public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Skill> Skills { get; set; } = new();
    public CognitiveServicesAccount? CognitiveServices { get; set; }
    public KnowledgeStore? KnowledgeStore { get; set; }
    public IndexProjections? IndexProjections { get; set; }
    public EncryptionKey? EncryptionKey { get; set; }
    public string? ETag { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static SkillsetDocument FromSkillset(Skillset skillset)
    {
        return new SkillsetDocument
        {
            Name = skillset.Name,
            Description = skillset.Description,
            Skills = skillset.Skills,
            CognitiveServices = skillset.CognitiveServices,
            KnowledgeStore = skillset.KnowledgeStore,
            IndexProjections = skillset.IndexProjections,
            EncryptionKey = skillset.EncryptionKey
        };
    }

    public Skillset ToSkillset()
    {
        return new Skillset
        {
            Name = Name,
            Description = Description,
            Skills = Skills,
            CognitiveServices = CognitiveServices,
            KnowledgeStore = KnowledgeStore,
            IndexProjections = IndexProjections,
            EncryptionKey = EncryptionKey,
            ETag = ETag
        };
    }
}
