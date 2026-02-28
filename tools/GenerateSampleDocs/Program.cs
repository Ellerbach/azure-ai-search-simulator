using System.IO.Compression;
using System.IO.Hashing;
using System.Buffers.Binary;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;

// ───────────────────────────────────────────────────────────────────
// Generates sample documents with text + embedded images for testing
// the DocumentExtractionSkill's image extraction feature.
//
// Output directory: samples/sample-data/image-extraction/
// Produces:
//   - report-with-charts.docx   (Word doc with 2 paragraphs + 2 images)
//   - inventory-with-photos.xlsx (Excel with data + 1 image)
// ───────────────────────────────────────────────────────────────────

var outputDir = Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "sample-data", "image-extraction");
outputDir = Path.GetFullPath(outputDir);
Directory.CreateDirectory(outputDir);

// --- Create two distinct coloured PNG images ---

var redPng = CreateSolidPng(120, 80, r: 220, g: 50, b: 50);    // red-ish
var bluePng = CreateSolidPng(100, 100, r: 40, g: 80, b: 200);   // blue-ish
var greenPng = CreateSolidPng(150, 60, r: 30, g: 180, b: 60);   // green-ish

// ═══════════════════════════════════════════════════════════════════
// 1. Word document: report-with-charts.docx
// ═══════════════════════════════════════════════════════════════════

var docxPath = Path.Combine(outputDir, "report-with-charts.docx");
CreateWordDoc(docxPath, redPng, bluePng);
Console.WriteLine($"Created: {docxPath}  ({new FileInfo(docxPath).Length:N0} bytes)");

// ═══════════════════════════════════════════════════════════════════
// 2. Excel workbook: inventory-with-photos.xlsx
// ═══════════════════════════════════════════════════════════════════

var xlsxPath = Path.Combine(outputDir, "inventory-with-photos.xlsx");
CreateExcel(xlsxPath, greenPng);
Console.WriteLine($"Created: {xlsxPath}  ({new FileInfo(xlsxPath).Length:N0} bytes)");

Console.WriteLine("\nDone! Files are in: " + outputDir);

// ───────────────────────────────────────────────────────────────────
// Helper: Create a solid-colour PNG (RGB, no alpha) from scratch
// ───────────────────────────────────────────────────────────────────
static byte[] CreateSolidPng(int width, int height, byte r, byte g, byte b)
{
    using var ms = new MemoryStream();

    // PNG signature
    ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    // IHDR
    var ihdr = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
    BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
    ihdr[8] = 8;  // bit depth
    ihdr[9] = 2;  // RGB
    WritePngChunk(ms, "IHDR"u8, ihdr);

    // IDAT — zlib-compressed scanlines
    var raw = new byte[height * (1 + width * 3)];
    for (int row = 0; row < height; row++)
    {
        int offset = row * (1 + width * 3);
        raw[offset] = 0; // filter = None
        for (int col = 0; col < width; col++)
        {
            int px = offset + 1 + col * 3;
            raw[px] = r;
            raw[px + 1] = g;
            raw[px + 2] = b;
        }
    }
    using (var compMs = new MemoryStream())
    {
        using (var zlib = new ZLibStream(compMs, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);
        WritePngChunk(ms, "IDAT"u8, compMs.ToArray());
    }

    // IEND
    WritePngChunk(ms, "IEND"u8, Array.Empty<byte>());

    return ms.ToArray();
}

static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
{
    Span<byte> len = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
    stream.Write(len);
    stream.Write(type);
    if (data.Length > 0) stream.Write(data);

    // CRC32
    var crcBuf = new byte[4 + data.Length];
    type.CopyTo(crcBuf);
    data.CopyTo(crcBuf.AsSpan(4));
    var hash = Crc32.Hash(crcBuf);
    Array.Reverse(hash); // to big-endian
    stream.Write(hash);
}

// ───────────────────────────────────────────────────────────────────
// Helper: Create Word document with text + images
// ───────────────────────────────────────────────────────────────────
static void CreateWordDoc(string path, byte[] image1, byte[] image2)
{
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    var body = mainPart.Document.Body!;

    // Paragraph 1: Title
    body.AppendChild(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new DocumentFormat.OpenXml.Wordprocessing.Run(
            new DocumentFormat.OpenXml.Wordprocessing.Text("Quarterly Sales Report — Q4 2025"))));

    // Paragraph 2: Introduction text
    body.AppendChild(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(
        new DocumentFormat.OpenXml.Wordprocessing.Text(
            "This report summarizes our quarterly performance across all regions. " +
            "Revenue increased 15% year-over-year, driven primarily by the EMEA and APAC markets. " +
            "The following charts illustrate the trends in detail."))));

    // Image 1: red chart
    var imgPart1 = mainPart.AddImagePart(ImagePartType.Png);
    using (var s = new MemoryStream(image1)) imgPart1.FeedData(s);
    var relId1 = mainPart.GetIdOfPart(imgPart1);
    body.AppendChild(CreateImageParagraph(relId1, 120, 80, "Figure 1: Revenue by Region"));

    // Paragraph 3: More text
    body.AppendChild(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(
        new DocumentFormat.OpenXml.Wordprocessing.Text(
            "Customer satisfaction scores also improved significantly. " +
            "The Net Promoter Score rose from 42 to 58, exceeding our annual target of 50. " +
            "Key drivers included faster response times and the new self-service portal."))));

    // Image 2: blue chart
    var imgPart2 = mainPart.AddImagePart(ImagePartType.Png);
    using (var s = new MemoryStream(image2)) imgPart2.FeedData(s);
    var relId2 = mainPart.GetIdOfPart(imgPart2);
    body.AppendChild(CreateImageParagraph(relId2, 100, 100, "Figure 2: Customer Satisfaction Trend"));

    // Paragraph 4: Conclusion
    body.AppendChild(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(
        new DocumentFormat.OpenXml.Wordprocessing.Text(
            "Looking ahead to Q1 2026, we expect continued growth in APAC while maintaining " +
            "strong margins in North America. The product team is preparing three major feature " +
            "launches that should further improve retention metrics."))));

    mainPart.Document.Save();
}

static Paragraph CreateImageParagraph(string relationshipId, int widthPx, int heightPx, string altText)
{
    long emuW = widthPx * 9525L;
    long emuH = heightPx * 9525L;

    var drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing(
        new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = emuW, Cy = emuH },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = 1, Name = altText },
            new DocumentFormat.OpenXml.Drawing.Graphic(
                new DocumentFormat.OpenXml.Drawing.GraphicData(
                    new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                            new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties { Id = 0, Name = altText },
                            new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                        new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                            new DocumentFormat.OpenXml.Drawing.Blip { Embed = relationshipId },
                            new DocumentFormat.OpenXml.Drawing.Stretch(
                                new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                        new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                            new DocumentFormat.OpenXml.Drawing.Transform2D(
                                new DocumentFormat.OpenXml.Drawing.Offset { X = 0, Y = 0 },
                                new DocumentFormat.OpenXml.Drawing.Extents { Cx = emuW, Cy = emuH }),
                            new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                                new DocumentFormat.OpenXml.Drawing.AdjustValueList())
                            { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle })
                    )
                ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
            )
        )
        {
            DistanceFromTop = 0,
            DistanceFromBottom = 0,
            DistanceFromLeft = 0,
            DistanceFromRight = 0
        });

    return new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(drawing));
}

// ───────────────────────────────────────────────────────────────────
// Helper: Create Excel workbook with data + embedded image
// ───────────────────────────────────────────────────────────────────
static void CreateExcel(string path, byte[] image)
{
    using var workbook = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var wbPart = workbook.AddWorkbookPart();
    wbPart.Workbook = new Workbook(new Sheets());

    var wsPart = wbPart.AddNewPart<WorksheetPart>();
    wsPart.Worksheet = new Worksheet(new SheetData());

    var sheets = wbPart.Workbook.GetFirstChild<Sheets>()!;
    sheets.Append(new Sheet
    {
        Id = wbPart.GetIdOfPart(wsPart),
        SheetId = 1,
        Name = "Inventory"
    });

    // Add data rows
    var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>()!;

    AppendRow(sheetData, 1, new[] { "Product ID", "Name", "Category", "Quantity", "Status" });
    AppendRow(sheetData, 2, new[] { "P001", "Wireless Mouse", "Electronics", "150", "In Stock" });
    AppendRow(sheetData, 3, new[] { "P002", "USB-C Hub", "Electronics", "75", "Low Stock" });
    AppendRow(sheetData, 4, new[] { "P003", "Ergonomic Keyboard", "Electronics", "200", "In Stock" });
    AppendRow(sheetData, 5, new[] { "P004", "Monitor Stand", "Furniture", "30", "Low Stock" });
    AppendRow(sheetData, 6, new[] { "P005", "Desk Lamp", "Furniture", "90", "In Stock" });

    // Add image via DrawingsPart
    var drawingsPart = wsPart.AddNewPart<DrawingsPart>();
    var imgPart = drawingsPart.AddImagePart(ImagePartType.Png);
    using (var s = new MemoryStream(image)) imgPart.FeedData(s);

    // Minimal drawings XML
    var drawingsNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    var aNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    var rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    var relId = drawingsPart.GetIdOfPart(imgPart);

    var xml = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<xdr:wsDr xmlns:xdr=""{drawingsNs}"" xmlns:a=""{aNs}"" xmlns:r=""{rNs}"">
  <xdr:twoCellAnchor>
    <xdr:from><xdr:col>6</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
    <xdr:to><xdr:col>9</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>6</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
    <xdr:pic>
      <xdr:nvPicPr>
        <xdr:cNvPr id=""1"" name=""Product Photo""/>
        <xdr:cNvPicPr><a:picLocks noChangeAspect=""1""/></xdr:cNvPicPr>
      </xdr:nvPicPr>
      <xdr:blipFill>
        <a:blip r:embed=""{relId}""/>
        <a:stretch><a:fillRect/></a:stretch>
      </xdr:blipFill>
      <xdr:spPr>
        <a:xfrm><a:off x=""0"" y=""0""/><a:ext cx=""1905000"" cy=""762000""/></a:xfrm>
        <a:prstGeom prst=""rect""><a:avLst/></a:prstGeom>
      </xdr:spPr>
    </xdr:pic>
    <xdr:clientData/>
  </xdr:twoCellAnchor>
</xdr:wsDr>";

    using (var writer = new StreamWriter(drawingsPart.GetStream()))
        writer.Write(xml);

    // Reference the drawings from the worksheet
    wsPart.Worksheet.Append(new DocumentFormat.OpenXml.Spreadsheet.Drawing
    {
        Id = wsPart.GetIdOfPart(drawingsPart)
    });

    wsPart.Worksheet.Save();
    wbPart.Workbook.Save();
}

static void AppendRow(SheetData sheetData, uint rowIndex, string[] values)
{
    var row = new DocumentFormat.OpenXml.Spreadsheet.Row { RowIndex = rowIndex };
    char col = 'A';
    foreach (var val in values)
    {
        row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell
        {
            CellReference = $"{col}{rowIndex}",
            DataType = CellValues.String,
            CellValue = new CellValue(val)
        });
        col++;
    }
    sheetData.Append(row);
}
