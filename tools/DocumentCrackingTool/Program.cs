using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzureAISearchSimulator.Search.DocumentCracking;

namespace DocumentCrackingTool;

/// <summary>
/// CLI tool that runs all document crackers on a given file and outputs JSON results.
/// 
/// Usage:
///   DocumentCrackingTool &lt;file-path&gt; [--crackers cracker1,cracker2] [--content-preview 500]
///   DocumentCrackingTool --list
/// 
/// Examples:
///   DocumentCrackingTool document.pdf
///   DocumentCrackingTool document.pdf --crackers PdfCracker
///   DocumentCrackingTool --list
/// </summary>
public class Program
{
    private static readonly Dictionary<string, IDocumentCracker> AllCrackers = new()
    {
        ["PdfCracker"] = new PdfCracker(),
        ["PlainTextCracker"] = new PlainTextCracker(),
        ["HtmlCracker"] = new HtmlCracker(),
        ["JsonCracker"] = new JsonCracker(),
        ["CsvCracker"] = new CsvCracker(),
        ["ExcelCracker"] = new ExcelCracker(),
        ["WordDocCracker"] = new WordDocCracker(),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--list"))
        {
            return ListCrackers();
        }

        var filePath = args[0];
        if (!File.Exists(filePath))
        {
            WriteError($"File not found: {filePath}");
            return 1;
        }

        // Parse optional arguments
        var requestedCrackers = ParseOption(args, "--crackers")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();

        var contentPreview = int.TryParse(ParseOption(args, "--content-preview"), out var cp) ? cp : -1;

        return await CrackFile(filePath, requestedCrackers, contentPreview);
    }

    private static async Task<int> CrackFile(string filePath, string[]? requestedCrackers, int contentPreview)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = GuessContentType(extension);

        var results = new Dictionary<string, object>();
        results["file"] = fileName;
        results["filePath"] = Path.GetFullPath(filePath);
        results["fileSize"] = fileBytes.Length;
        results["extension"] = extension;
        results["detectedContentType"] = contentType;

        var crackerResults = new List<object>();

        // Determine which crackers to run
        var crackersToRun = AllCrackers.AsEnumerable();
        if (requestedCrackers != null && requestedCrackers.Length > 0)
        {
            crackersToRun = AllCrackers.Where(kv =>
                requestedCrackers.Any(rc => kv.Key.Equals(rc, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var (crackerName, cracker) in crackersToRun)
        {
            var canHandle = cracker.CanHandle(contentType, extension);

            var crackerResult = new Dictionary<string, object?>
            {
                ["crackerName"] = crackerName,
                ["canHandle"] = canHandle,
                ["supportedContentTypes"] = cracker.SupportedContentTypes.ToList(),
                ["supportedExtensions"] = cracker.SupportedExtensions.ToList(),
            };

            if (canHandle)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var cracked = await cracker.CrackAsync(fileBytes, fileName, contentType);
                    sw.Stop();
                    crackerResult["extractionTimeMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    crackerResult["success"] = cracked.Success;
                    crackerResult["errorMessage"] = cracked.ErrorMessage;

                    if (cracked.Success)
                    {
                        // Content (optionally truncated)
                        if (contentPreview >= 0 && cracked.Content.Length > contentPreview)
                        {
                            crackerResult["content"] = cracked.Content.Substring(0, contentPreview);
                            crackerResult["contentTruncated"] = true;
                            crackerResult["fullContentLength"] = cracked.Content.Length;
                        }
                        else
                        {
                            crackerResult["content"] = cracked.Content;
                            crackerResult["contentTruncated"] = false;
                        }

                        // Metrics
                        crackerResult["characterCount"] = cracked.CharacterCount;
                        crackerResult["wordCount"] = cracked.WordCount;
                        crackerResult["pageCount"] = cracked.PageCount;

                        // Document metadata
                        crackerResult["title"] = cracked.Title;
                        crackerResult["author"] = cracked.Author;
                        crackerResult["createdDate"] = cracked.CreatedDate?.ToString("o");
                        crackerResult["modifiedDate"] = cracked.ModifiedDate?.ToString("o");
                        crackerResult["language"] = cracked.Language;

                        // Additional metadata
                        if (cracked.Metadata.Count > 0)
                        {
                            crackerResult["metadata"] = cracked.Metadata;
                        }

                        // Warnings
                        if (cracked.Warnings.Count > 0)
                        {
                            crackerResult["warnings"] = cracked.Warnings;
                        }
                    }
                }
                catch (Exception ex)
                {
                    crackerResult["success"] = false;
                    crackerResult["errorMessage"] = ex.Message;
                    crackerResult["exceptionType"] = ex.GetType().Name;
                }
            }

            crackerResults.Add(crackerResult);
        }

        results["crackers"] = crackerResults;

        // Output JSON
        var json = JsonSerializer.Serialize(results, JsonOptions);
        Console.Write(json);

        return 0;
    }

    private static int ListCrackers()
    {
        var info = AllCrackers.Select(kv => new
        {
            name = kv.Key,
            supportedContentTypes = kv.Value.SupportedContentTypes.ToList(),
            supportedExtensions = kv.Value.SupportedExtensions.ToList(),
        });

        var json = JsonSerializer.Serialize(new { crackers = info }, JsonOptions);
        Console.Write(json);
        return 0;
    }

    private static string GuessContentType(string extension)
    {
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" or ".text" => "text/plain",
            ".md" or ".markdown" => "text/markdown",
            ".html" or ".htm" => "text/html",
            ".xhtml" => "application/xhtml+xml",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".tsv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            _ => "application/octet-stream",
        };
    }

    private static string? ParseOption(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            DocumentCrackingTool - Run document crackers on files
            
            Usage:
              DocumentCrackingTool <file-path> [options]
              DocumentCrackingTool --list
            
            Options:
              --crackers <names>      Comma-separated list of crackers to run (default: all)
              --content-preview <n>   Max chars of content to include (default: full content)
              --list                  List all available crackers
              --help, -h              Show this help
            
            Available crackers:
              PdfCracker, PlainTextCracker, HtmlCracker, JsonCracker,
              CsvCracker, ExcelCracker, WordDocCracker
            
            Output: JSON to stdout
            """);
    }

    private static void WriteError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
    }
}
