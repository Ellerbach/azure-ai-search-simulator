using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for PDF files using PdfPig.
/// </summary>
public class PdfCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "application/pdf"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".pdf"
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
            using var document = PdfDocument.Open(stream);

            var textBuilder = new StringBuilder();
            var pageCount = document.NumberOfPages;

            result.PageCount = pageCount;

            // Extract text from each page
            for (int i = 1; i <= pageCount; i++)
            {
                try
                {
                    var page = document.GetPage(i);
                    var pageText = page.Text;

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        textBuilder.AppendLine(pageText);
                        textBuilder.AppendLine(); // Page separator
                    }

                    // Extract images from this page
                    try
                    {
                        var images = page.GetImages();
                        foreach (var image in images)
                        {
                            var crackedImage = new CrackedImage
                            {
                                Width = image.WidthInSamples,
                                Height = image.HeightInSamples,
                                PageNumber = i, // 1-based page number
                                ContentOffset = textBuilder.Length // offset at end of page text
                            };

                            // Try to get PNG bytes first, fall back to raw bytes
                            if (image.TryGetPng(out var pngBytes))
                            {
                                crackedImage.Data = pngBytes;
                                crackedImage.ContentType = "image/png";
                            }
                            else
                            {
                                crackedImage.Data = image.RawMemory.ToArray();
                                crackedImage.ContentType = "image/unknown";
                            }

                            // Capture bounding box if available
                            var bounds = image.Bounds;
                            crackedImage.Bounds = new ImageBounds
                            {
                                X = bounds.Left,
                                Y = bounds.Bottom,
                                Width = bounds.Width,
                                Height = bounds.Height,
                                PageWidth = page.Width,
                                PageHeight = page.Height
                            };

                            result.Images.Add(crackedImage);

                            if (result.Images.Count >= 1000) break; // Azure limit
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to extract images from page {i}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to extract text from page {i}: {ex.Message}");
                }

                if (result.Images.Count >= 1000) break; // Azure limit across pages
            }

            result.Content = textBuilder.ToString().Trim();
            result.CharacterCount = result.Content.Length;
            result.WordCount = CountWords(result.Content);

            // Extract document metadata
            var info = document.Information;
            if (info != null)
            {
                if (!string.IsNullOrEmpty(info.Title))
                    result.Title = info.Title;

                if (!string.IsNullOrEmpty(info.Author))
                    result.Author = info.Author;

                // PdfPig returns dates as strings, try to parse them
                if (!string.IsNullOrEmpty(info.CreationDate) && 
                    TryParsePdfDate(info.CreationDate, out var createdDate))
                    result.CreatedDate = createdDate;

                if (!string.IsNullOrEmpty(info.ModifiedDate) && 
                    TryParsePdfDate(info.ModifiedDate, out var modifiedDate))
                    result.ModifiedDate = modifiedDate;

                if (!string.IsNullOrEmpty(info.Subject))
                    result.Metadata["subject"] = info.Subject;

                if (!string.IsNullOrEmpty(info.Keywords))
                    result.Metadata["keywords"] = info.Keywords;

                if (!string.IsNullOrEmpty(info.Creator))
                    result.Metadata["creator"] = info.Creator;

                if (!string.IsNullOrEmpty(info.Producer))
                    result.Metadata["producer"] = info.Producer;
            }

            result.Metadata["pdfVersion"] = document.Version.ToString();
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to extract PDF content: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static bool TryParsePdfDate(string pdfDate, out DateTimeOffset result)
    {
        result = default;

        // PDF dates are in format: D:YYYYMMDDHHmmSS+HH'mm' or similar
        if (string.IsNullOrEmpty(pdfDate))
            return false;

        // Remove D: prefix
        if (pdfDate.StartsWith("D:"))
            pdfDate = pdfDate.Substring(2);

        // Try standard parsing first
        if (DateTimeOffset.TryParse(pdfDate, out result))
            return true;

        // Try parsing PDF-specific format (minimum: YYYY)
        if (pdfDate.Length >= 4 && int.TryParse(pdfDate.Substring(0, 4), out var year))
        {
            var month = pdfDate.Length >= 6 && int.TryParse(pdfDate.Substring(4, 2), out var m) ? m : 1;
            var day = pdfDate.Length >= 8 && int.TryParse(pdfDate.Substring(6, 2), out var d) ? d : 1;
            var hour = pdfDate.Length >= 10 && int.TryParse(pdfDate.Substring(8, 2), out var h) ? h : 0;
            var minute = pdfDate.Length >= 12 && int.TryParse(pdfDate.Substring(10, 2), out var min) ? min : 0;
            var second = pdfDate.Length >= 14 && int.TryParse(pdfDate.Substring(12, 2), out var s) ? s : 0;

            try
            {
                result = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
