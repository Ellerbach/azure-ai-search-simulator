namespace CustomSkillSample.Models;

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
    /// Array of errors that occurred processing this record.
    /// </summary>
    public List<CustomSkillMessage> Errors { get; set; } = new();

    /// <summary>
    /// Array of warnings for this record.
    /// </summary>
    public List<CustomSkillMessage> Warnings { get; set; } = new();
}

/// <summary>
/// An error or warning message from skill processing.
/// </summary>
public class CustomSkillMessage
{
    /// <summary>
    /// The error or warning message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional HTTP status code (for errors).
    /// </summary>
    public int? StatusCode { get; set; }
}
