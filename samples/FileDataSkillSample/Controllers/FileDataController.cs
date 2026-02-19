using FileDataSkillSample.Models;
using Microsoft.AspNetCore.Mvc;

namespace FileDataSkillSample.Controllers;

/// <summary>
/// Custom skill that reads a file from disk (given a content path) and returns
/// its raw bytes as a base64-encoded file_data structure compatible with
/// Azure AI Search Document Extraction skill.
///
/// Skillset output:
///   { "name": "file_data", "targetName": "file_data" }
///
/// Response payload per record:
///   {
///     "recordId": "...",
///     "data": {
///       "file_data": {
///         "$type": "file",
///         "data": "&lt;base64-encoded-content&gt;"
///       }
///     }
///   }
/// </summary>
[ApiController]
[Route("api/skills")]
public class FileDataController : ControllerBase
{
    private readonly ILogger<FileDataController> _logger;
    private readonly IConfiguration _configuration;

    public FileDataController(ILogger<FileDataController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Reads a document from disk and returns its content as base64 in the
    /// file_data format expected by Azure AI Search Document Extraction.
    ///
    /// Inputs:
    ///   - documentId (string): Identifier for the document (used for logging/tracking).
    ///   - contentPath (string): Relative or absolute path to the file on disk.
    ///
    /// Output:
    ///   - file_data: { "$type": "file", "data": "&lt;base64&gt;" }
    /// </summary>
    [HttpPost("file-data")]
    public async Task<ActionResult<CustomSkillResponse>> FileData([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("FileData skill received {Count} records", request.Values.Count);

        // Optional base path from configuration — allows content paths to be relative
        var basePath = _configuration.GetValue<string>("FileData:BasePath") ?? string.Empty;

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var documentId = GetStringValue(record.Data, "documentId") ?? string.Empty;
                var contentPath = GetStringValue(record.Data, "contentPath") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(contentPath))
                {
                    outputRecord.Errors.Add(new CustomSkillMessage
                    {
                        Message = "Missing required input 'contentPath'.",
                        StatusCode = 400
                    });
                    response.Values.Add(outputRecord);
                    continue;
                }

                // Resolve the full file path
                var filePath = Path.IsPathRooted(contentPath)
                    ? contentPath
                    : Path.Combine(basePath, contentPath);

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FilePath} (documentId={DocumentId})", filePath, documentId);
                    outputRecord.Errors.Add(new CustomSkillMessage
                    {
                        Message = $"File not found: {filePath}",
                        StatusCode = 404
                    });
                    response.Values.Add(outputRecord);
                    continue;
                }

                // Read the file bytes and encode as base64
                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var encodedData = Convert.ToBase64String(fileBytes);

                _logger.LogInformation(
                    "FileData skill processed documentId={DocumentId}, file={FilePath}, size={Size} bytes",
                    documentId, filePath, fileBytes.Length);

                // Return in the file_data structure expected by Document Extraction
                outputRecord.Data["file_data"] = new Dictionary<string, object>
                {
                    { "$type", "file" },
                    { "data", encodedData }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing record {RecordId}", record.RecordId);
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error reading file: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    public ActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    #region Helper Methods

    private static string? GetStringValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    #endregion
}
