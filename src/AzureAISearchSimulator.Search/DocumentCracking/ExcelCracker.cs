using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Text;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for Excel files (.xlsx) using OpenXML.
/// </summary>
public class ExcelCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".xlsx"
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
            using var doc = SpreadsheetDocument.Open(stream, false);

            var textBuilder = new StringBuilder();
            var workbookPart = doc.WorkbookPart;

            if (workbookPart == null)
            {
                result.Success = false;
                result.ErrorMessage = "Invalid Excel file: no workbook found.";
                return Task.FromResult(result);
            }

            // Get shared strings table for string lookups
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

            // Process each worksheet
            var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>();
            var sheetNames = new List<string>();
            var totalRows = 0;

            foreach (var sheet in sheets)
            {
                var sheetId = sheet.Id?.Value;
                var sheetName = sheet.Name?.Value ?? "Sheet";
                sheetNames.Add(sheetName);

                if (string.IsNullOrEmpty(sheetId))
                    continue;

                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);
                var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                if (sheetData == null)
                    continue;

                textBuilder.AppendLine($"--- {sheetName} ---");

                foreach (var row in sheetData.Elements<Row>())
                {
                    var cellValues = new List<string>();
                    
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var cellValue = GetCellValue(cell, sharedStrings);
                        cellValues.Add(cellValue);
                    }

                    if (cellValues.Any(v => !string.IsNullOrWhiteSpace(v)))
                    {
                        textBuilder.AppendLine(string.Join("\t", cellValues));
                        totalRows++;
                    }
                }

                textBuilder.AppendLine();
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
            }

            // Metadata
            result.Metadata["sheetCount"] = sheetNames.Count;
            result.Metadata["sheetNames"] = string.Join(", ", sheetNames);
            result.Metadata["totalRows"] = totalRows;

            // Extract embedded images from all worksheets
            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                var drawingsPart = worksheetPart.DrawingsPart;
                if (drawingsPart == null) continue;

                foreach (var imagePart in drawingsPart.ImageParts)
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
                            PageNumber = 0 // Excel is not page-based
                        };

                        // Try to read pixel dimensions from image header
                        WordDocCracker.ReadImageDimensions(imageBytes, crackedImage);
                        result.Images.Add(crackedImage);

                        if (result.Images.Count >= 1000) break; // Azure limit
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to extract image: {ex.Message}");
                    }
                }

                if (result.Images.Count >= 1000) break; // Azure limit across sheets
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to extract Excel content: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static string GetCellValue(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        // If the cell is a shared string, look up the value
        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings != null)
        {
            if (int.TryParse(value, out var index))
            {
                var sharedStringItem = sharedStrings.ElementAtOrDefault(index);
                if (sharedStringItem != null)
                {
                    value = sharedStringItem.InnerText;
                }
            }
        }

        return value;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
