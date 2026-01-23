using System.Text;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for plain text files.
/// </summary>
public class PlainTextCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "text/plain",
        "text/markdown",
        "text/x-markdown"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".txt",
        ".md",
        ".markdown",
        ".text"
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
            // Detect encoding and decode content
            var encoding = DetectEncoding(content);
            var text = encoding.GetString(content);

            // Remove BOM if present
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            result.Content = text;
            result.CharacterCount = text.Length;
            result.WordCount = CountWords(text);
            result.Success = true;

            // Basic metadata
            result.Metadata["encoding"] = encoding.EncodingName;
            result.Metadata["lineCount"] = text.Split('\n').Length;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to extract text: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static Encoding DetectEncoding(byte[] content)
    {
        // Check for BOM
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode; // UTF-16 LE
        }
        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        // Default to UTF-8
        return Encoding.UTF8;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
