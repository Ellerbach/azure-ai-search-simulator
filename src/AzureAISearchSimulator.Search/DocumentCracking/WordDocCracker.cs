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

            // Extract embedded images
            var mainPart = doc.MainDocumentPart;
            if (mainPart != null)
            {
                int imageIndex = 0;
                foreach (var imagePart in mainPart.ImageParts)
                {
                    try
                    {
                        using var imageStream = imagePart.GetStream();
                        using var ms = new MemoryStream();
                        imageStream.CopyTo(ms);
                        var imageBytes = ms.ToArray();

                        var crackedImage = new CrackedImage
                        {
                            Data = imageBytes,
                            ContentType = imagePart.ContentType,
                            PageNumber = 0 // Word is not page-based
                        };

                        // Try to read pixel dimensions from image header
                        ReadImageDimensions(imageBytes, crackedImage);

                        result.Images.Add(crackedImage);
                        imageIndex++;

                        if (result.Images.Count >= 1000) break; // Azure limit
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to extract image {imageIndex}: {ex.Message}");
                    }
                }
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

    /// <summary>
    /// Reads pixel dimensions from PNG or JPEG image headers.
    /// </summary>
    internal static void ReadImageDimensions(byte[] data, CrackedImage image)
    {
        if (data.Length < 24) return;

        // PNG: bytes 16-23 contain width (4 bytes) and height (4 bytes) in IHDR chunk
        if (data[0] == 0x89 && data[1] == 0x50) // PNG signature
        {
            image.Width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            image.Height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return;
        }

        // JPEG: scan for SOF0/SOF2 marker (0xFF 0xC0 or 0xFF 0xC2)
        if (data[0] == 0xFF && data[1] == 0xD8) // JPEG signature
        {
            for (int i = 2; i < data.Length - 9; i++)
            {
                if (data[i] == 0xFF && (data[i + 1] == 0xC0 || data[i + 1] == 0xC2))
                {
                    image.Height = (data[i + 5] << 8) | data[i + 6];
                    image.Width = (data[i + 7] << 8) | data[i + 8];
                    return;
                }
            }
        }

        // GIF: bytes 6-9 contain width (2 bytes LE) and height (2 bytes LE)
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) // "GIF"
        {
            image.Width = data[6] | (data[7] << 8);
            image.Height = data[8] | (data[9] << 8);
            return;
        }

        // BMP: bytes 18-25 contain width (4 bytes LE) and height (4 bytes LE)
        if (data[0] == 0x42 && data[1] == 0x4D && data.Length >= 26) // "BM"
        {
            image.Width = data[18] | (data[19] << 8) | (data[20] << 16) | (data[21] << 24);
            image.Height = Math.Abs(data[22] | (data[23] << 8) | (data[24] << 16) | (data[25] << 24));
            return;
        }
    }
}
