using System.Text.Json.Serialization;
using LiteDB;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a skillset that defines a sequence of skills for enrichment processing.
/// </summary>
public class Skillset
{
    /// <summary>
    /// Internal ID for LiteDB storage (not serialized to JSON).
    /// </summary>
    [BsonId]
    [JsonIgnore]
    public int InternalId { get; set; }

    /// <summary>
    /// The name of the skillset.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the skillset.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The list of skills in this skillset.
    /// </summary>
    [JsonPropertyName("skills")]
    public List<Skill> Skills { get; set; } = new();

    /// <summary>
    /// Cognitive services account configuration (optional for simulator).
    /// </summary>
    [JsonPropertyName("cognitiveServices")]
    public CognitiveServicesAccount? CognitiveServices { get; set; }

    /// <summary>
    /// Knowledge store configuration for projections (optional).
    /// </summary>
    [JsonPropertyName("knowledgeStore")]
    public KnowledgeStore? KnowledgeStore { get; set; }

    /// <summary>
    /// Index projections configuration (optional).
    /// </summary>
    [JsonPropertyName("indexProjections")]
    public IndexProjections? IndexProjections { get; set; }

    /// <summary>
    /// Optional encryption key for customer-managed encryption.
    /// </summary>
    [JsonPropertyName("encryptionKey")]
    public EncryptionKey? EncryptionKey { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// Represents a skill in a skillset.
/// </summary>
public class Skill
{
    /// <summary>
    /// The type of the skill (e.g., "#Microsoft.Skills.Text.SplitSkill").
    /// </summary>
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = string.Empty;

    /// <summary>
    /// The unique name of the skill within the skillset.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Description of what this skill does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The context for skill execution (e.g., "/document" or "/document/pages/*").
    /// </summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    /// <summary>
    /// The input mappings for the skill.
    /// </summary>
    [JsonPropertyName("inputs")]
    public List<SkillInput> Inputs { get; set; } = new();

    /// <summary>
    /// The output mappings for the skill.
    /// </summary>
    [JsonPropertyName("outputs")]
    public List<SkillOutput> Outputs { get; set; } = new();

    // Skill-specific properties

    /// <summary>
    /// For TextSplitSkill: The mode of text splitting (pages, sentences).
    /// </summary>
    [JsonPropertyName("textSplitMode")]
    public string? TextSplitMode { get; set; }

    /// <summary>
    /// For TextSplitSkill: Maximum page length.
    /// </summary>
    [JsonPropertyName("maximumPageLength")]
    public int? MaximumPageLength { get; set; }

    /// <summary>
    /// For TextSplitSkill: Page overlap length.
    /// </summary>
    [JsonPropertyName("pageOverlapLength")]
    public int? PageOverlapLength { get; set; }

    /// <summary>
    /// For MergeSkill: Separator to insert between merged text.
    /// </summary>
    [JsonPropertyName("insertPreTag")]
    public string? InsertPreTag { get; set; }

    /// <summary>
    /// For MergeSkill: Suffix to insert after merged text.
    /// </summary>
    [JsonPropertyName("insertPostTag")]
    public string? InsertPostTag { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: The URI of the web API.
    /// </summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: HTTP method (default: POST).
    /// </summary>
    [JsonPropertyName("httpMethod")]
    public string? HttpMethod { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: Request timeout.
    /// </summary>
    [JsonPropertyName("timeout")]
    public string? Timeout { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: Batch size.
    /// </summary>
    [JsonPropertyName("batchSize")]
    public int? BatchSize { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: Degree of parallelism.
    /// </summary>
    [JsonPropertyName("degreeOfParallelism")]
    public int? DegreeOfParallelism { get; set; }

    /// <summary>
    /// For CustomWebApiSkill: HTTP headers.
    /// </summary>
    [JsonPropertyName("httpHeaders")]
    public Dictionary<string, string>? HttpHeaders { get; set; }

    /// <summary>
    /// For AzureOpenAIEmbeddingSkill: Resource URI.
    /// </summary>
    [JsonPropertyName("resourceUri")]
    public string? ResourceUri { get; set; }

    /// <summary>
    /// For AzureOpenAIEmbeddingSkill: Deployment ID.
    /// </summary>
    [JsonPropertyName("deploymentId")]
    public string? DeploymentId { get; set; }

    /// <summary>
    /// For AzureOpenAIEmbeddingSkill: Model name.
    /// </summary>
    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    /// <summary>
    /// For AzureOpenAIEmbeddingSkill: Dimensions (e.g., 1536 for text-embedding-ada-002).
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }

    /// <summary>
    /// For AzureOpenAIEmbeddingSkill: API key for the Azure OpenAI resource.
    /// If not specified, uses the key from appsettings.json.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// For ShaperSkill: The shaping definitions.
    /// </summary>
    [JsonPropertyName("output")]
    public object? Output { get; set; }

    /// <summary>
    /// For ConditionalSkill: The condition expression.
    /// </summary>
    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Default language code for language-aware skills.
    /// </summary>
    [JsonPropertyName("defaultLanguageCode")]
    public string? DefaultLanguageCode { get; set; }
}

/// <summary>
/// Represents an input mapping for a skill.
/// </summary>
public class SkillInput
{
    /// <summary>
    /// The name of the input parameter.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The source path for the input value (e.g., "/document/content").
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Alternative: list of sources for array inputs.
    /// </summary>
    [JsonPropertyName("sourceContext")]
    public string? SourceContext { get; set; }

    /// <summary>
    /// Alternative: list of input definitions for complex inputs.
    /// </summary>
    [JsonPropertyName("inputs")]
    public List<SkillInput>? Inputs { get; set; }
}

/// <summary>
/// Represents an output mapping for a skill.
/// </summary>
public class SkillOutput
{
    /// <summary>
    /// The name of the output from the skill.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The target name in the enriched document.
    /// </summary>
    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }
}

/// <summary>
/// Cognitive Services account configuration.
/// </summary>
public class CognitiveServicesAccount
{
    /// <summary>
    /// OData type for the account configuration.
    /// </summary>
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }

    /// <summary>
    /// Resource ID for the cognitive services account.
    /// </summary>
    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; set; }

    /// <summary>
    /// API key (for non-managed identity scenarios).
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Description of the account.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Knowledge store configuration for projections.
/// </summary>
public class KnowledgeStore
{
    /// <summary>
    /// Storage account connection string.
    /// </summary>
    [JsonPropertyName("storageConnectionString")]
    public string? StorageConnectionString { get; set; }

    /// <summary>
    /// List of projections.
    /// </summary>
    [JsonPropertyName("projections")]
    public List<Projection>? Projections { get; set; }
}

/// <summary>
/// Projection configuration for knowledge store.
/// </summary>
public class Projection
{
    /// <summary>
    /// Table projections.
    /// </summary>
    [JsonPropertyName("tables")]
    public List<TableProjection>? Tables { get; set; }

    /// <summary>
    /// Object projections.
    /// </summary>
    [JsonPropertyName("objects")]
    public List<ObjectProjection>? Objects { get; set; }

    /// <summary>
    /// File projections.
    /// </summary>
    [JsonPropertyName("files")]
    public List<FileProjection>? Files { get; set; }
}

/// <summary>
/// Table projection configuration.
/// </summary>
public class TableProjection
{
    [JsonPropertyName("tableName")]
    public string? TableName { get; set; }

    [JsonPropertyName("generatedKeyName")]
    public string? GeneratedKeyName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("sourceContext")]
    public string? SourceContext { get; set; }

    [JsonPropertyName("inputs")]
    public List<SkillInput>? Inputs { get; set; }
}

/// <summary>
/// Object projection configuration.
/// </summary>
public class ObjectProjection
{
    [JsonPropertyName("storageContainer")]
    public string? StorageContainer { get; set; }

    [JsonPropertyName("generatedKeyName")]
    public string? GeneratedKeyName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("sourceContext")]
    public string? SourceContext { get; set; }
}

/// <summary>
/// File projection configuration.
/// </summary>
public class FileProjection
{
    [JsonPropertyName("storageContainer")]
    public string? StorageContainer { get; set; }

    [JsonPropertyName("generatedKeyName")]
    public string? GeneratedKeyName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("sourceContext")]
    public string? SourceContext { get; set; }
}

/// <summary>
/// Index projections configuration.
/// </summary>
public class IndexProjections
{
    /// <summary>
    /// List of selectors for index projections.
    /// </summary>
    [JsonPropertyName("selectors")]
    public List<IndexProjectionSelector>? Selectors { get; set; }

    /// <summary>
    /// Parameters for index projections.
    /// </summary>
    [JsonPropertyName("parameters")]
    public IndexProjectionParameters? Parameters { get; set; }
}

/// <summary>
/// Selector for index projections.
/// </summary>
public class IndexProjectionSelector
{
    [JsonPropertyName("targetIndexName")]
    public string? TargetIndexName { get; set; }

    [JsonPropertyName("parentKeyFieldName")]
    public string? ParentKeyFieldName { get; set; }

    [JsonPropertyName("sourceContext")]
    public string? SourceContext { get; set; }

    [JsonPropertyName("mappings")]
    public List<FieldMapping>? Mappings { get; set; }
}

/// <summary>
/// Parameters for index projections.
/// </summary>
public class IndexProjectionParameters
{
    [JsonPropertyName("projectionMode")]
    public string? ProjectionMode { get; set; }
}

/// <summary>
/// Encryption key configuration.
/// </summary>
public class EncryptionKey
{
    [JsonPropertyName("keyVaultKeyName")]
    public string? KeyVaultKeyName { get; set; }

    [JsonPropertyName("keyVaultKeyVersion")]
    public string? KeyVaultKeyVersion { get; set; }

    [JsonPropertyName("keyVaultUri")]
    public string? KeyVaultUri { get; set; }

    [JsonPropertyName("accessCredentials")]
    public AccessCredentials? AccessCredentials { get; set; }
}

/// <summary>
/// Access credentials for encryption key.
/// </summary>
public class AccessCredentials
{
    [JsonPropertyName("applicationId")]
    public string? ApplicationId { get; set; }

    [JsonPropertyName("applicationSecret")]
    public string? ApplicationSecret { get; set; }
}
