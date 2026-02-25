using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.DocumentCracking;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// DocumentExtractionSkill - Extracts content from a file provided as base64-encoded data or a URL.
/// Implements the #Microsoft.Skills.Util.DocumentExtractionSkill behavior.
/// 
/// The file_data input must be an object with:
///   { "$type": "file", "data": "BASE64..." }       — inline base64 mode
///   { "$type": "file", "url": "...", "sasToken": "..." }  — URL download mode
/// 
/// Outputs:
///   content            — Extracted text from the document.
///   normalized_images  — Array of normalized images when imageAction is set; empty array when imageAction is "none" (default).
/// </summary>
public class DocumentExtractionSkillExecutor : ISkillExecutor
{
    private readonly IDocumentCrackerFactory _documentCrackerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DocumentExtractionSkillExecutor> _logger;

    public string ODataType => "#Microsoft.Skills.Util.DocumentExtractionSkill";

    public DocumentExtractionSkillExecutor(
        IDocumentCrackerFactory documentCrackerFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentExtractionSkillExecutor> logger)
    {
        _documentCrackerFactory = documentCrackerFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SkillExecutionResult> ExecuteAsync(
        Skill skill,
        EnrichedDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();
            var warnings = new List<string>();

            // Find the file_data input
            var fileDataInput = skill.Inputs.FirstOrDefault(i => i.Name == "file_data");
            if (fileDataInput?.Source == null)
            {
                return SkillExecutionResult.Failed("DocumentExtractionSkill requires 'file_data' input");
            }

            var parsingMode = skill.SkillParsingMode?.ToLowerInvariant() ?? "default";
            var dataToExtract = skill.DataToExtract?.ToLowerInvariant() ?? "contentandmetadata";

            foreach (var ctx in contexts)
            {
                var sourcePath = ResolveSourcePath(ctx, fileDataInput.Source);
                var fileDataValue = document.GetValue(sourcePath);

                if (fileDataValue == null)
                {
                    warnings.Add($"No file_data found at path '{sourcePath}'");
                    continue;
                }

                // Parse the file_data object
                var parseResult = ParseFileData(fileDataValue);
                if (!parseResult.Success)
                {
                    return SkillExecutionResult.Failed(parseResult.Error!);
                }

                // Obtain raw bytes
                byte[] fileBytes;
                try
                {
                    fileBytes = await GetFileBytesAsync(parseResult, cancellationToken);
                }
                catch (Exception ex)
                {
                    return SkillExecutionResult.Failed($"Failed to obtain file bytes: {ex.Message}");
                }

                if (fileBytes.Length == 0)
                {
                    warnings.Add("file_data resolved to empty content");
                    SetOutputs(skill, document, ctx, string.Empty, dataToExtract, null);
                    continue;
                }

                // Extract content based on parsing mode
                string extractedContent;
                CrackedDocument? crackedDoc = null;

                switch (parsingMode)
                {
                    case "text":
                        extractedContent = Encoding.UTF8.GetString(fileBytes);
                        break;

                    case "json":
                        extractedContent = ExtractJsonContent(fileBytes);
                        break;

                    default: // "default"
                        (extractedContent, crackedDoc, var crackWarnings) = await CrackDocumentAsync(fileBytes);
                        warnings.AddRange(crackWarnings);
                        break;
                }

                SetOutputs(skill, document, ctx, extractedContent, dataToExtract, crackedDoc);

                _logger.LogDebug(
                    "DocumentExtractionSkill extracted {CharCount} characters from file_data at '{Path}' (mode: {Mode})",
                    extractedContent.Length, sourcePath, parsingMode);
            }

            return warnings.Count > 0
                ? SkillExecutionResult.SucceededWithWarnings(warnings.ToArray())
                : SkillExecutionResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DocumentExtractionSkill");
            return SkillExecutionResult.Failed($"DocumentExtractionSkill error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the file_data value from the enriched document into a structured result.
    /// </summary>
    private FileDataParseResult ParseFileData(object fileDataValue)
    {
        Dictionary<string, object?>? fileDataDict = null;

        if (fileDataValue is Dictionary<string, object?> dict)
        {
            fileDataDict = dict;
        }
        else if (fileDataValue is JsonElement jsonElement)
        {
            fileDataDict = ConvertJsonElementToDictionary(jsonElement);
        }
        else
        {
            // Try to interpret as a serializable object
            try
            {
                var json = JsonSerializer.Serialize(fileDataValue);
                var element = JsonSerializer.Deserialize<JsonElement>(json);
                fileDataDict = ConvertJsonElementToDictionary(element);
            }
            catch
            {
                return FileDataParseResult.Fail("file_data is not a valid object");
            }
        }

        if (fileDataDict == null)
        {
            return FileDataParseResult.Fail("file_data is not a valid object");
        }

        // Check for $type == "file"
        var hasType = fileDataDict.TryGetValue("$type", out var typeValue);
        if (!hasType || typeValue?.ToString() != "file")
        {
            // Be lenient: if the object has "data" or "url", treat it as valid even without $type
            // This handles cases where the custom skill doesn't include $type
            var hasDataOrUrl = fileDataDict.ContainsKey("data") || fileDataDict.ContainsKey("url");
            if (!hasDataOrUrl)
            {
                return FileDataParseResult.Fail("file_data must have '$type' set to 'file' or contain 'data'/'url' property");
            }
            _logger.LogDebug("file_data missing '$type: file' but has data/url — accepting anyway");
        }

        // Check for inline base64 data
        if (fileDataDict.TryGetValue("data", out var dataValue) && dataValue != null)
        {
            var base64String = dataValue.ToString();
            if (string.IsNullOrEmpty(base64String))
            {
                return FileDataParseResult.Fail("file_data 'data' property is empty");
            }
            return FileDataParseResult.FromBase64(base64String!);
        }

        // Check for URL
        if (fileDataDict.TryGetValue("url", out var urlValue) && urlValue != null)
        {
            var url = urlValue.ToString();
            if (string.IsNullOrEmpty(url))
            {
                return FileDataParseResult.Fail("file_data 'url' property is empty");
            }

            string? sasToken = null;
            if (fileDataDict.TryGetValue("sasToken", out var sasValue) && sasValue != null)
            {
                sasToken = sasValue.ToString();
            }

            return FileDataParseResult.FromUrl(url!, sasToken);
        }

        return FileDataParseResult.Fail("file_data must provide either 'data' (base64) or 'url' property");
    }

    /// <summary>
    /// Gets the file bytes from either base64 data or a URL download.
    /// </summary>
    private async Task<byte[]> GetFileBytesAsync(FileDataParseResult parseResult, CancellationToken cancellationToken)
    {
        if (parseResult.Base64Data != null)
        {
            try
            {
                return Convert.FromBase64String(parseResult.Base64Data);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Invalid base64 data in file_data");
            }
        }

        if (parseResult.Url != null)
        {
            var client = _httpClientFactory.CreateClient();
            var downloadUrl = parseResult.Url;

            // Append SAS token if provided
            if (!string.IsNullOrEmpty(parseResult.SasToken))
            {
                var separator = downloadUrl.Contains('?') ? "&" : "?";
                downloadUrl = $"{downloadUrl}{separator}{parseResult.SasToken}";
            }

            _logger.LogDebug("Downloading file from URL: {Url}", parseResult.Url);
            var response = await client.GetAsync(downloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        throw new InvalidOperationException("No data source in file_data");
    }

    /// <summary>
    /// Extracts JSON content from file bytes.
    /// Parses and re-serializes to ensure valid JSON, then returns as string.
    /// </summary>
    private static string ExtractJsonContent(byte[] fileBytes)
    {
        var text = Encoding.UTF8.GetString(fileBytes);
        try
        {
            // Validate it's proper JSON by parsing and re-serializing
            var jsonDoc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(jsonDoc.RootElement, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // If it's not valid JSON, return as-is
            return text;
        }
    }

    /// <summary>
    /// Cracks a document using the document cracker factory.
    /// Detects content type from magic bytes and dispatches to the appropriate cracker.
    /// </summary>
    private async Task<(string content, CrackedDocument? crackedDoc, List<string> warnings)> CrackDocumentAsync(byte[] fileBytes)
    {
        var warnings = new List<string>();
        var (contentType, extension) = DetectContentType(fileBytes);

        _logger.LogDebug("Detected content type '{ContentType}' (extension: {Extension}) from file bytes ({Length} bytes)",
            contentType, extension, fileBytes.Length);

        if (_documentCrackerFactory.CanCrack(contentType, extension))
        {
            var crackedDoc = await _documentCrackerFactory.CrackDocumentAsync(
                fileBytes, $"document{extension}", contentType);

            if (crackedDoc.Success)
            {
                warnings.AddRange(crackedDoc.Warnings);
                return (crackedDoc.Content, crackedDoc, warnings);
            }
            else
            {
                _logger.LogWarning("Document cracker failed: {Error}", crackedDoc.ErrorMessage);
                warnings.Add($"Document cracker failed: {crackedDoc.ErrorMessage}");
            }
        }
        else
        {
            _logger.LogDebug("No cracker available for {ContentType}/{Extension}, falling back to UTF-8 text", contentType, extension);
            warnings.Add($"No document cracker available for content type '{contentType}', falling back to text extraction");
        }

        // Fallback: try to read as UTF-8 text
        try
        {
            var text = Encoding.UTF8.GetString(fileBytes);
            return (text, null, warnings);
        }
        catch
        {
            return (string.Empty, null, warnings);
        }
    }

    /// <summary>
    /// Sets the output values on the enriched document.
    /// </summary>
    private void SetOutputs(
        Skill skill,
        EnrichedDocument document,
        string context,
        string content,
        string dataToExtract,
        CrackedDocument? crackedDoc)
    {
        foreach (var output in skill.Outputs)
        {
            var targetName = output.TargetName ?? output.Name;
            var outputPath = $"{context}/{targetName}";

            switch (output.Name.ToLowerInvariant())
            {
                case "content":
                    if (dataToExtract == "allmetadata")
                    {
                        // allMetadata mode: no content, only metadata
                        document.SetValue(outputPath, string.Empty);
                    }
                    else
                    {
                        document.SetValue(outputPath, content);
                    }
                    break;

                case "normalized_images":
                    var imageAction = GetConfigValue(skill, "imageAction", "none");
                    if (imageAction != "none" && crackedDoc?.Images?.Count > 0)
                    {
                        var maxWidth = GetConfigValue(skill, "normalizedImageMaxWidth", 2000);
                        var maxHeight = GetConfigValue(skill, "normalizedImageMaxHeight", 2000);

                        var normalizedImages = new List<Dictionary<string, object>>();
                        foreach (var img in crackedDoc.Images)
                        {
                            try
                            {
                                normalizedImages.Add(ImageNormalizer.Normalize(img, maxWidth, maxHeight));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to normalize image (page {Page}, offset {Offset})",
                                    img.PageNumber, img.ContentOffset);
                            }
                        }
                        document.SetValue(outputPath, normalizedImages);
                    }
                    else
                    {
                        document.SetValue(outputPath, new List<object>());
                    }
                    break;

                default:
                    // Handle metadata outputs if the cracked document has them
                    if (crackedDoc != null)
                    {
                        SetMetadataOutput(document, outputPath, output.Name, crackedDoc);
                    }
                    break;
            }
        }

        // If dataToExtract includes metadata and we have cracked doc metadata,
        // set common metadata fields on the context
        if (dataToExtract != "allmetadata" || crackedDoc == null) return;

        SetMetadataOnContext(document, context, crackedDoc);
    }

    /// <summary>
    /// Sets metadata from the cracked document on the enriched document context.
    /// </summary>
    private static void SetMetadataOnContext(EnrichedDocument document, string context, CrackedDocument crackedDoc)
    {
        if (crackedDoc.Title != null)
            document.SetValue($"{context}/metadata_title", crackedDoc.Title);
        if (crackedDoc.Author != null)
            document.SetValue($"{context}/metadata_author", crackedDoc.Author);
        if (crackedDoc.PageCount.HasValue)
            document.SetValue($"{context}/metadata_page_count", crackedDoc.PageCount.Value);
        if (crackedDoc.WordCount.HasValue)
            document.SetValue($"{context}/metadata_word_count", crackedDoc.WordCount.Value);
        if (crackedDoc.Language != null)
            document.SetValue($"{context}/metadata_language", crackedDoc.Language);
        if (crackedDoc.CreatedDate.HasValue)
            document.SetValue($"{context}/metadata_creation_date", crackedDoc.CreatedDate.Value.ToString("o"));
        if (crackedDoc.ModifiedDate.HasValue)
            document.SetValue($"{context}/metadata_last_modified", crackedDoc.ModifiedDate.Value.ToString("o"));
    }

    /// <summary>
    /// Sets a specific metadata output from the cracked document.
    /// </summary>
    private static void SetMetadataOutput(EnrichedDocument document, string outputPath, string outputName, CrackedDocument crackedDoc)
    {
        object? value = outputName.ToLowerInvariant() switch
        {
            "metadata_title" or "title" => crackedDoc.Title,
            "metadata_author" or "author" => crackedDoc.Author,
            "metadata_page_count" or "page_count" => crackedDoc.PageCount,
            "metadata_word_count" or "word_count" => crackedDoc.WordCount,
            "metadata_language" or "language" => crackedDoc.Language,
            "metadata_creation_date" or "creation_date" => crackedDoc.CreatedDate?.ToString("o"),
            "metadata_last_modified" or "last_modified" => crackedDoc.ModifiedDate?.ToString("o"),
            _ => null
        };

        if (value != null)
        {
            document.SetValue(outputPath, value);
        }
    }

    /// <summary>
    /// Detects the content type and file extension from the raw file bytes using magic byte signatures.
    /// </summary>
    public static (string contentType, string extension) DetectContentType(byte[] bytes)
    {
        if (bytes.Length >= 4)
        {
            // PDF: %PDF (0x25 0x50 0x44 0x46)
            if (bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
                return ("application/pdf", ".pdf");

            // ZIP-based Office formats: PK\x03\x04 (0x50 0x4B 0x03 0x04)
            if (bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
                return ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx");

            // RTF: {\rtf
            if (bytes[0] == 0x7B && bytes[1] == 0x5C && bytes[2] == 0x72 && bytes[3] == 0x74)
                return ("application/rtf", ".rtf");
        }

        // Try to detect text-based formats from content
        var sampleLength = Math.Min(bytes.Length, 256);
        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart();

        if (sample.StartsWith("{") || sample.StartsWith("["))
            return ("application/json", ".json");

        if (sample.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            sample.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return ("text/html", ".html");

        if (sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            sample.StartsWith("<", StringComparison.OrdinalIgnoreCase))
            return ("text/xml", ".xml");

        // CSV heuristic: check for comma-separated values with consistent column count
        if (LooksCsv(sample))
            return ("text/csv", ".csv");

        // Fallback to plain text
        return ("text/plain", ".txt");
    }

    /// <summary>
    /// Simple heuristic to detect if content looks like CSV.
    /// </summary>
    /// <summary>
    /// Reads a configuration value from the skill's Configuration dictionary.
    /// Returns the default value if the key is not found or the conversion fails.
    /// </summary>
    private static T GetConfigValue<T>(Skill skill, string key, T defaultValue)
    {
        if (skill.SkillConfiguration?.TryGetValue(key, out var value) == true && value != null)
        {
            try
            {
                if (value is JsonElement je)
                {
                    if (typeof(T) == typeof(string))
                        return (T)(object)je.GetString()!;
                    if (typeof(T) == typeof(int))
                        return (T)(object)je.GetInt32();
                }
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    private static bool LooksCsv(string sample)
    {
        var lines = sample.Split('\n', 5);
        if (lines.Length < 2) return false;

        var firstLineCommas = lines[0].Count(c => c == ',');
        if (firstLineCommas == 0) return false;

        // Check if the second line has a similar comma count
        var secondLineCommas = lines[1].Count(c => c == ',');
        return Math.Abs(firstLineCommas - secondLineCommas) <= 1;
    }

    private static Dictionary<string, object?>? ConvertJsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.ToString()
            };
        }
        return dict;
    }

    private static string ResolveSourcePath(string context, string source)
    {
        if (source.StartsWith("/"))
        {
            return source;
        }
        return $"{context}/{source}";
    }

    /// <summary>
    /// Internal result of parsing file_data.
    /// </summary>
    private class FileDataParseResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public string? Base64Data { get; init; }
        public string? Url { get; init; }
        public string? SasToken { get; init; }

        public static FileDataParseResult FromBase64(string base64Data) =>
            new() { Success = true, Base64Data = base64Data };

        public static FileDataParseResult FromUrl(string url, string? sasToken = null) =>
            new() { Success = true, Url = url, SasToken = sasToken };

        public static FileDataParseResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
