using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for Word documents (.docx) using OpenXML.
/// </summary>
public class WordDocCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".docx"
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
            using var stream = new MemoryStream(content);
            using var doc = WordprocessingDocument.Open(stream, false);

            var textBuilder = new StringBuilder();

            // Extract main document body
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                ExtractTextFromElement(body, textBuilder);
            }

            result.Content = textBuilder.ToString().Trim();
            result.CharacterCount = result.Content.Length;
            result.WordCount = CountWords(result.Content);

            // Extract core properties
            var coreProps = doc.PackageProperties;
            if (coreProps != null)
            {
                if (!string.IsNullOrEmpty(coreProps.Title))
                    result.Title = coreProps.Title;

                if (!string.IsNullOrEmpty(coreProps.Creator))
                    result.Author = coreProps.Creator;

                if (coreProps.Created.HasValue)
                    result.CreatedDate = new DateTimeOffset(coreProps.Created.Value);

                if (coreProps.Modified.HasValue)
                    result.ModifiedDate = new DateTimeOffset(coreProps.Modified.Value);

                if (!string.IsNullOrEmpty(coreProps.Subject))
                    result.Metadata["subject"] = coreProps.Subject;

                if (!string.IsNullOrEmpty(coreProps.Keywords))
                    result.Metadata["keywords"] = coreProps.Keywords;

                if (!string.IsNullOrEmpty(coreProps.Description))
                    result.Metadata["description"] = coreProps.Description;

                if (!string.IsNullOrEmpty(coreProps.Category))
                    result.Metadata["category"] = coreProps.Category;

                if (!string.IsNullOrEmpty(coreProps.LastModifiedBy))
                    result.Metadata["lastModifiedBy"] = coreProps.LastModifiedBy;
            }

            // Count pages (approximation based on section breaks)
            var sectionProps = body?.Descendants<SectionProperties>().Count() ?? 0;
            if (sectionProps > 0)
            {
                result.PageCount = sectionProps;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to extract Word document content: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static void ExtractTextFromElement(OpenXmlElement element, StringBuilder textBuilder)
    {
        foreach (var child in element.ChildElements)
        {
            if (child is Paragraph para)
            {
                var paraText = para.InnerText;
                if (!string.IsNullOrWhiteSpace(paraText))
                {
                    textBuilder.AppendLine(paraText);
                }
            }
            else if (child is Table table)
            {
                foreach (var row in table.Descendants<TableRow>())
                {
                    var cellTexts = new List<string>();
                    foreach (var cell in row.Descendants<TableCell>())
                    {
                        cellTexts.Add(cell.InnerText);
                    }
                    textBuilder.AppendLine(string.Join("\t", cellTexts));
                }
                textBuilder.AppendLine();
            }
            else if (child.HasChildren)
            {
                ExtractTextFromElement(child, textBuilder);
            }
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
