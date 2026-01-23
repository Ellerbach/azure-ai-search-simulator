using System.Text.Json;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for JSON files.
/// </summary>
public class JsonCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "application/json",
        "text/json"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".json"
    };

    public bool CanHandle(string contentType, string extension)
    {
        return SupportedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase) ||
               SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public Task<CrackedDocument> CrackAsync(byte[] content, string fileName, string contentType)
    {
        var result = new CrackedDocument();

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(content);
            
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract text content from JSON
            var textContent = new List<string>();
            ExtractTextFromElement(root, textContent);
            
            result.Content = string.Join("\n", textContent);
            result.CharacterCount = result.Content.Length;
            result.WordCount = CountWords(result.Content);
            result.Success = true;

            // Try to extract common metadata fields
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("title", out var title))
                    result.Title = title.GetString();
                
                if (root.TryGetProperty("author", out var author))
                    result.Author = author.GetString();
                
                if (root.TryGetProperty("language", out var language))
                    result.Language = language.GetString();

                // Store all root-level properties as metadata
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        result.Metadata[$"json_{property.Name}"] = property.Value.GetString() ?? "";
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        result.Metadata[$"json_{property.Name}"] = property.Value.GetDouble();
                    }
                    else if (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False)
                    {
                        result.Metadata[$"json_{property.Name}"] = property.Value.GetBoolean();
                    }
                }
            }

            result.Metadata["jsonType"] = root.ValueKind.ToString();
        }
        catch (JsonException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to parse JSON: {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to extract JSON content: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static void ExtractTextFromElement(JsonElement element, List<string> textContent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textContent.Add(text);
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ExtractTextFromElement(property.Value, textContent);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractTextFromElement(item, textContent);
                }
                break;
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
