using System.Text;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for CSV files.
/// </summary>
public class CsvCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "text/csv",
        "text/comma-separated-values",
        "application/csv"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".csv",
        ".tsv"
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
            var text = Encoding.UTF8.GetString(content);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            // Detect delimiter
            var delimiter = DetectDelimiter(lines.FirstOrDefault() ?? "", fileName);
            
            // Parse CSV
            var rows = new List<string[]>();
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(ParseCsvLine(line, delimiter));
                }
            }

            // Extract headers (first row)
            string[]? headers = null;
            if (rows.Count > 0)
            {
                headers = rows[0];
                result.Metadata["columns"] = string.Join(", ", headers);
                result.Metadata["columnCount"] = headers.Length;
            }

            // Build content from all text values
            var contentBuilder = new StringBuilder();
            foreach (var row in rows)
            {
                contentBuilder.AppendLine(string.Join(" ", row));
            }

            result.Content = contentBuilder.ToString().Trim();
            result.CharacterCount = result.Content.Length;
            result.WordCount = CountWords(result.Content);
            result.Success = true;

            // Additional metadata
            result.Metadata["rowCount"] = rows.Count;
            result.Metadata["delimiter"] = delimiter == '\t' ? "tab" : delimiter.ToString();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to parse CSV: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static char DetectDelimiter(string firstLine, string fileName)
    {
        // TSV files
        if (fileName.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return '\t';
        }

        // Count occurrences of common delimiters
        var commaCount = firstLine.Count(c => c == ',');
        var tabCount = firstLine.Count(c => c == '\t');
        var semicolonCount = firstLine.Count(c => c == ';');

        if (tabCount > commaCount && tabCount > semicolonCount)
            return '\t';
        if (semicolonCount > commaCount)
            return ';';
        
        return ',';
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var currentValue = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentValue.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                values.Add(currentValue.ToString().Trim());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }

        values.Add(currentValue.ToString().Trim());
        return values.ToArray();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
