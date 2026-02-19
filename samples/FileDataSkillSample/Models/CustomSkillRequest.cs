namespace FileDataSkillSample.Models;

/// <summary>
/// The request payload sent by Azure AI Search to a custom skill.
/// </summary>
public class CustomSkillRequest
{
    /// <summary>
    /// Array of records to process.
    /// </summary>
    public List<CustomSkillInputRecord> Values { get; set; } = new();
}

/// <summary>
/// A single input record in the custom skill request.
/// </summary>
public class CustomSkillInputRecord
{
    /// <summary>
    /// Unique identifier for this record. Must be returned in the response.
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// The input data fields as defined in the skillset's inputs.
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}
