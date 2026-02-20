namespace FileDataSkillSample.Models;

/// <summary>
/// The response payload returned by a custom skill to Azure AI Search.
/// </summary>
public class CustomSkillResponse
{
    /// <summary>
    /// Array of output records, one for each input record.
    /// </summary>
    public List<CustomSkillOutputRecord> Values { get; set; } = new();
}

/// <summary>
/// A single output record in the custom skill response.
/// </summary>
public class CustomSkillOutputRecord
{
    /// <summary>
    /// Must match the recordId from the input record.
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// The output data fields as defined in the skillset's outputs.
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// Array of error messages that occurred processing this record.
    /// Must be plain strings — the simulator deserializes them as List&lt;string&gt;.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Array of warning messages for this record.
    /// Must be plain strings — the simulator deserializes them as List&lt;string&gt;.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
