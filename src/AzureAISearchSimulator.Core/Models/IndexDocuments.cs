using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Request to index documents (upload, merge, delete).
/// </summary>
public class IndexDocumentsRequest
{
    /// <summary>
    /// The batch of document actions to perform.
    /// </summary>
    [JsonPropertyName("value")]
    public List<IndexAction> Value { get; set; } = new();
}

/// <summary>
/// A single document action (upload, merge, delete, etc.).
/// </summary>
public class IndexAction : Dictionary<string, object?>
{
    /// <summary>
    /// When set by index projections, indicates the target index for this action.
    /// If null, the indexer's default TargetIndexName is used.
    /// Not serialized to JSON.
    /// </summary>
    [JsonIgnore]
    public string? TargetIndexName { get; set; }

    /// <summary>
    /// Gets the action type from @search.action field.
    /// </summary>
    [JsonIgnore]
    public IndexActionType ActionType
    {
        get
        {
            if (TryGetValue("@search.action", out var action))
            {
                // Handle both string and JsonElement (System.Text.Json deserializes
                // Dictionary<string, object?> values as JsonElement, not string)
                var actionStr = action switch
                {
                    string s => s,
                    System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String => je.GetString(),
                    _ => null
                };

                if (actionStr != null)
                {
                    return actionStr.ToLowerInvariant() switch
                    {
                        "upload" => IndexActionType.Upload,
                        "merge" => IndexActionType.Merge,
                        "mergeorupload" => IndexActionType.MergeOrUpload,
                        "delete" => IndexActionType.Delete,
                        _ => IndexActionType.Upload
                    };
                }
            }
            return IndexActionType.Upload; // Default action
        }
    }

    /// <summary>
    /// Gets the document without the @search.action field.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, object?> Document
    {
        get
        {
            var doc = new Dictionary<string, object?>(this);
            doc.Remove("@search.action");
            return doc;
        }
    }
}

/// <summary>
/// Document action types.
/// </summary>
public enum IndexActionType
{
    /// <summary>
    /// Upload a new document or replace if exists.
    /// </summary>
    Upload,

    /// <summary>
    /// Merge with existing document (update fields).
    /// </summary>
    Merge,

    /// <summary>
    /// Merge if exists, otherwise upload.
    /// </summary>
    MergeOrUpload,

    /// <summary>
    /// Delete the document.
    /// </summary>
    Delete
}

/// <summary>
/// Response from indexing documents.
/// </summary>
public class IndexDocumentsResponse
{
    /// <summary>
    /// Results for each document action.
    /// </summary>
    [JsonPropertyName("value")]
    public List<IndexingResult> Value { get; set; } = new();
}

/// <summary>
/// Result of indexing a single document.
/// </summary>
public class IndexingResult
{
    /// <summary>
    /// The document key.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}
