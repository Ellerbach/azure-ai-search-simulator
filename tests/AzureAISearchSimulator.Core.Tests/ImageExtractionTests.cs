using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Moq;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for image extraction from documents and the ImageNormalizer.
/// Covers Steps 1-7 of the IMAGE-EXTRACTION-PLAN.
/// </summary>
public class ImageExtractionTests
{
    // ─── CrackedImage model ─────────────────────────────────────────

    [Fact]
    public void CrackedImage_DefaultValues_AreCorrect()
    {
        var img = new CrackedImage();

        Assert.Empty(img.Data);
        Assert.Equal("image/unknown", img.ContentType);
        Assert.Equal(0, img.Width);
        Assert.Equal(0, img.Height);
        Assert.Equal(0, img.PageNumber);
        Assert.Equal(0, img.ContentOffset);
        Assert.Null(img.Bounds);
    }

    [Fact]
    public void CrackedImage_CanSetAllProperties()
    {
        var data = new byte[] { 1, 2, 3 };
        var bounds = new ImageBounds { X = 10, Y = 20, Width = 100, Height = 50, PageWidth = 612, PageHeight = 792 };
        var img = new CrackedImage
        {
            Data = data,
            ContentType = "image/png",
            Width = 640,
            Height = 480,
            PageNumber = 3,
            ContentOffset = 1234,
            Bounds = bounds
        };

        Assert.Equal(data, img.Data);
        Assert.Equal("image/png", img.ContentType);
        Assert.Equal(640, img.Width);
        Assert.Equal(480, img.Height);
        Assert.Equal(3, img.PageNumber);
        Assert.Equal(1234, img.ContentOffset);
        Assert.Equal(10, img.Bounds!.X);
        Assert.Equal(20, img.Bounds.Y);
        Assert.Equal(100, img.Bounds.Width);
        Assert.Equal(50, img.Bounds.Height);
        Assert.Equal(612, img.Bounds.PageWidth);
        Assert.Equal(792, img.Bounds.PageHeight);
    }

    [Fact]
    public void CrackedDocument_Images_DefaultsToEmptyList()
    {
        var doc = new CrackedDocument();
        Assert.NotNull(doc.Images);
        Assert.Empty(doc.Images);
    }

    // ─── ReadImageDimensions tests ──────────────────────────────────

    [Fact]
    public void ReadImageDimensions_ParsesPngHeader()
    {
        // Minimal PNG header: signature (8) + IHDR chunk (4 length + 4 type + 4 width + 4 height)
        var png = new byte[24];
        // PNG signature
        png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
        png[4] = 0x0D; png[5] = 0x0A; png[6] = 0x1A; png[7] = 0x0A;
        // IHDR chunk: length (13) + "IHDR"
        png[8] = 0; png[9] = 0; png[10] = 0; png[11] = 13;
        png[12] = 0x49; png[13] = 0x48; png[14] = 0x44; png[15] = 0x52; // "IHDR"
        // Width: 800 (0x320) big-endian
        png[16] = 0x00; png[17] = 0x00; png[18] = 0x03; png[19] = 0x20;
        // Height: 600 (0x258) big-endian
        png[20] = 0x00; png[21] = 0x00; png[22] = 0x02; png[23] = 0x58;

        var img = new CrackedImage { Data = png };
        WordDocCracker.ReadImageDimensions(png, img);

        Assert.Equal(800, img.Width);
        Assert.Equal(600, img.Height);
    }

    [Fact]
    public void ReadImageDimensions_ParsesJpegHeader()
    {
        // Minimal JPEG with SOF0 marker — padded to >= 24 bytes
        // SOI (FFD8) + SOF0 marker (FFC0) + length (2 bytes) + precision (1) + height (2 BE) + width (2 BE) + padding
        var jpeg = new byte[24];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8;      // SOI
        jpeg[2] = 0xFF; jpeg[3] = 0xC0;      // SOF0
        jpeg[4] = 0x00; jpeg[5] = 0x11;      // length = 17
        jpeg[6] = 0x08;                      // precision = 8
        jpeg[7] = 0x01; jpeg[8] = 0xE0;      // height = 480
        jpeg[9] = 0x02; jpeg[10] = 0x80;     // width = 640

        var img = new CrackedImage { Data = jpeg };
        WordDocCracker.ReadImageDimensions(jpeg, img);

        Assert.Equal(640, img.Width);
        Assert.Equal(480, img.Height);
    }

    [Fact]
    public void ReadImageDimensions_ParsesGifHeader()
    {
        // GIF header: GIF89a + width (LE 16-bit) + height (LE 16-bit) — padded to >= 24 bytes
        var gif = new byte[24];
        gif[0] = 0x47; gif[1] = 0x49; gif[2] = 0x46; gif[3] = 0x38; gif[4] = 0x39; gif[5] = 0x61; // "GIF89a"
        gif[6] = 0x80; gif[7] = 0x02;              // width = 640 (LE)
        gif[8] = 0xE0; gif[9] = 0x01;              // height = 480 (LE)

        var img = new CrackedImage { Data = gif };
        WordDocCracker.ReadImageDimensions(gif, img);

        Assert.Equal(640, img.Width);
        Assert.Equal(480, img.Height);
    }

    [Fact]
    public void ReadImageDimensions_ParsesBmpHeader()
    {
        // BMP header: BM + 4 bytes file size + 4 reserved + 4 offset + 4 dib size + 4 width (LE) + 4 height (LE)
        var bmp = new byte[26];
        bmp[0] = 0x42; bmp[1] = 0x4D; // "BM"
        // Width at offset 18-21 (LE): 320
        bmp[18] = 0x40; bmp[19] = 0x01; bmp[20] = 0x00; bmp[21] = 0x00;
        // Height at offset 22-25 (LE): 240
        bmp[22] = 0xF0; bmp[23] = 0x00; bmp[24] = 0x00; bmp[25] = 0x00;

        var img = new CrackedImage { Data = bmp };
        WordDocCracker.ReadImageDimensions(bmp, img);

        Assert.Equal(320, img.Width);
        Assert.Equal(240, img.Height);
    }

    [Fact]
    public void ReadImageDimensions_UnknownFormat_LeavesZeros()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var img = new CrackedImage { Data = data };
        WordDocCracker.ReadImageDimensions(data, img);

        Assert.Equal(0, img.Width);
        Assert.Equal(0, img.Height);
    }

    // ─── ImageNormalizer: FitDimensions ─────────────────────────────

    [Fact]
    public void FitDimensions_LargerThanMax_ScalesProportionally()
    {
        var (w, h) = ImageNormalizer.FitDimensions(4000, 3000, 2000, 2000);
        Assert.Equal(2000, w);
        Assert.Equal(1500, h);
    }

    [Fact]
    public void FitDimensions_SmallerThanMax_NoUpscale()
    {
        var (w, h) = ImageNormalizer.FitDimensions(800, 600, 2000, 2000);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    [Fact]
    public void FitDimensions_ExactlyAtMax_NoChange()
    {
        var (w, h) = ImageNormalizer.FitDimensions(2000, 2000, 2000, 2000);
        Assert.Equal(2000, w);
        Assert.Equal(2000, h);
    }

    [Fact]
    public void FitDimensions_TallImage_ScalesByHeight()
    {
        var (w, h) = ImageNormalizer.FitDimensions(1000, 4000, 2000, 2000);
        Assert.Equal(500, w);
        Assert.Equal(2000, h);
    }

    [Fact]
    public void FitDimensions_ZeroDimensions_ReturnsZero()
    {
        var (w, h) = ImageNormalizer.FitDimensions(0, 0, 2000, 2000);
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    // ─── ImageNormalizer: BuildBoundingPolygon ──────────────────────

    [Fact]
    public void BuildBoundingPolygon_ReturnsCorrectFormat()
    {
        var polygon = ImageNormalizer.BuildBoundingPolygon(640, 480);
        Assert.Contains("\"x\":0.0", polygon);
        Assert.Contains("\"y\":0.0", polygon);
        Assert.Contains("\"x\":640.0", polygon);
        Assert.Contains("\"y\":480.0", polygon);
    }

    // ─── ImageNormalizer: NormalizeFallback ─────────────────────────

    [Fact]
    public void NormalizeFallback_ProducesCorrectSchema()
    {
        var imageData = new byte[] { 1, 2, 3, 4, 5 };
        var crackedImage = new CrackedImage
        {
            Data = imageData,
            Width = 100,
            Height = 200,
            PageNumber = 2,
            ContentOffset = 500
        };

        var result = ImageNormalizer.NormalizeFallback(crackedImage);

        Assert.Equal(Convert.ToBase64String(imageData), result["data"]);
        Assert.Equal(100, result["width"]);
        Assert.Equal(200, result["height"]);
        Assert.Equal(100, result["originalWidth"]);
        Assert.Equal(200, result["originalHeight"]);
        Assert.Equal(0, result["rotationFromOriginal"]);
        Assert.Equal(500, result["contentOffset"]);
        Assert.Equal(2, result["pageNumber"]);
        Assert.IsType<string>(result["boundingPolygon"]);
    }

    // ─── ImageNormalizer: Normalize with real image ─────────────────

    [Fact]
    public void Normalize_WithValidPng_ProducesAllRequiredKeys()
    {
        // Create a minimal valid 2x2 PNG
        var pngBytes = CreateMinimalPng(2, 2);
        var crackedImage = new CrackedImage
        {
            Data = pngBytes,
            ContentType = "image/png",
            Width = 2,
            Height = 2,
            PageNumber = 1,
            ContentOffset = 0
        };

        var result = ImageNormalizer.Normalize(crackedImage, 2000, 2000);

        // All required keys
        Assert.True(result.ContainsKey("data"));
        Assert.True(result.ContainsKey("width"));
        Assert.True(result.ContainsKey("height"));
        Assert.True(result.ContainsKey("originalWidth"));
        Assert.True(result.ContainsKey("originalHeight"));
        Assert.True(result.ContainsKey("rotationFromOriginal"));
        Assert.True(result.ContainsKey("contentOffset"));
        Assert.True(result.ContainsKey("pageNumber"));
        Assert.True(result.ContainsKey("boundingPolygon"));

        // Data should be base64 JPEG
        Assert.IsType<string>(result["data"]);
        var jpegData = Convert.FromBase64String((string)result["data"]);
        // JPEG starts with FF D8
        Assert.True(jpegData.Length >= 2);
        Assert.Equal(0xFF, jpegData[0]);
        Assert.Equal(0xD8, jpegData[1]);
    }

    [Fact]
    public void Normalize_WithLargeImage_ResizesToMaxDimensions()
    {
        // Create a 200x100 PNG, normalize to max 50x50 (50 is Azure's minimum)
        var pngBytes = CreateMinimalPng(200, 100);
        var crackedImage = new CrackedImage
        {
            Data = pngBytes,
            ContentType = "image/png",
            Width = 200,
            Height = 100,
            PageNumber = 0,
            ContentOffset = 0
        };

        var result = ImageNormalizer.Normalize(crackedImage, 50, 50);

        // Should have been resized proportionally to fit 50x50 → 50x25
        Assert.Equal(50, result["width"]);
        Assert.Equal(25, result["height"]);
        Assert.Equal(200, result["originalWidth"]);
        Assert.Equal(100, result["originalHeight"]);
    }

    // ─── Executor: normalized_images wiring ─────────────────────────

    [Fact]
    public async Task Executor_ReturnsEmptyImages_WhenNoConfig()
    {
        // Default: no imageAction configured → empty array
        var (executor, crackerFactory) = CreateExecutorWithMocks();
        var text = "Hello world";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(crackerFactory, text);

        await executor.ExecuteAsync(skill, document);

        var images = document.GetValue("/document/normalized_images");
        Assert.NotNull(images);
        Assert.IsAssignableFrom<IEnumerable<object?>>(images);
        Assert.Empty((IEnumerable<object?>)images!);
    }

    [Fact]
    public async Task Executor_ReturnsEmptyImages_WhenImageActionNone()
    {
        var (executor, crackerFactory) = CreateExecutorWithMocks();
        var text = "Hello world";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var config = new Dictionary<string, object> { ["imageAction"] = "none" };
        var skill = CreateSkill(configuration: config);
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(crackerFactory, text);

        await executor.ExecuteAsync(skill, document);

        var images = document.GetValue("/document/normalized_images");
        Assert.NotNull(images);
        Assert.IsAssignableFrom<IEnumerable<object?>>(images);
        Assert.Empty((IEnumerable<object?>)images!);
    }

    [Fact]
    public async Task Executor_ReturnsNormalizedImages_WhenImageActionSet()
    {
        var (executor, crackerFactory) = CreateExecutorWithMocks();
        var text = "Document with images";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        // Use JsonElement for configuration values (as deserialized from JSON)
        var configJson = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{"imageAction": "generateNormalizedImages"}""")!;
        var skill = CreateSkill(configuration: configJson);

        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        // Setup cracker that returns a document with images
        var pngBytes = CreateMinimalPng(10, 10);
        SetupCrackerWithImages(crackerFactory, text, new List<CrackedImage>
        {
            new() { Data = pngBytes, ContentType = "image/png", Width = 10, Height = 10, PageNumber = 1 },
            new() { Data = pngBytes, ContentType = "image/png", Width = 10, Height = 10, PageNumber = 2 }
        });

        await executor.ExecuteAsync(skill, document);

        var images = document.GetValue("/document/normalized_images");
        Assert.NotNull(images);
        var imageList = images as System.Collections.IList;
        Assert.NotNull(imageList);
        Assert.Equal(2, imageList!.Count);

        // Each image should be a dictionary with required keys
        foreach (var item in imageList)
        {
            var img = Assert.IsType<Dictionary<string, object>>(item);
            Assert.True(img.ContainsKey("data"));
            Assert.True(img.ContainsKey("width"));
            Assert.True(img.ContainsKey("height"));
            Assert.True(img.ContainsKey("pageNumber"));
        }
    }

    [Fact]
    public async Task Executor_ReturnsEmptyImages_WhenImageActionSetButNoImages()
    {
        var (executor, crackerFactory) = CreateExecutorWithMocks();
        var text = "No images here";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var configJson = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{"imageAction": "generateNormalizedImages"}""")!;
        var skill = CreateSkill(configuration: configJson);

        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(crackerFactory, text);

        await executor.ExecuteAsync(skill, document);

        var images = document.GetValue("/document/normalized_images");
        Assert.NotNull(images);
        Assert.IsAssignableFrom<IEnumerable<object?>>(images);
        Assert.Empty((IEnumerable<object?>)images!);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static (DocumentExtractionSkillExecutor executor, Mock<IDocumentCrackerFactory> crackerFactory)
        CreateExecutorWithMocks()
    {
        var crackerFactory = new Mock<IDocumentCrackerFactory>();
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var logger = new Mock<ILogger<DocumentExtractionSkillExecutor>>();
        var executor = new DocumentExtractionSkillExecutor(
            crackerFactory.Object,
            httpClientFactory.Object,
            logger.Object);
        return (executor, crackerFactory);
    }

    private static Skill CreateSkill(
        string? parsingMode = null,
        string? dataToExtract = null,
        Dictionary<string, object>? configuration = null)
    {
        return new Skill
        {
            ODataType = "#Microsoft.Skills.Util.DocumentExtractionSkill",
            Context = "/document",
            SkillParsingMode = parsingMode,
            DataToExtract = dataToExtract,
            SkillConfiguration = configuration,
            Inputs = new List<SkillInput>
            {
                new() { Name = "file_data", Source = "/document/file_data" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "content", TargetName = "extractedText" },
                new() { Name = "normalized_images", TargetName = "normalized_images" }
            }
        };
    }

    private static void SetupPlainTextCracker(Mock<IDocumentCrackerFactory> factory, string content)
    {
        var crackerMock = new Mock<IDocumentCracker>();
        crackerMock
            .Setup(c => c.CrackAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CrackedDocument
            {
                Success = true,
                Content = content
            });

        factory.Setup(f => f.CanCrack(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        factory.Setup(f => f.GetCracker(It.IsAny<string>(), It.IsAny<string>())).Returns(crackerMock.Object);
    }

    private static void SetupCrackerWithImages(
        Mock<IDocumentCrackerFactory> factory,
        string content,
        List<CrackedImage> images)
    {
        var crackerMock = new Mock<IDocumentCracker>();
        crackerMock
            .Setup(c => c.CrackAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CrackedDocument
            {
                Success = true,
                Content = content,
                Images = images
            });

        factory.Setup(f => f.CanCrack(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        factory.Setup(f => f.GetCracker(It.IsAny<string>(), It.IsAny<string>())).Returns(crackerMock.Object);
    }

    /// <summary>
    /// Creates a minimal valid PNG image of the requested dimensions.
    /// Constructs PNG bytes manually (signature + IHDR + IDAT + IEND) without any image library.
    /// </summary>
    private static byte[] CreateMinimalPng(int width, int height)
    {
        using var ms = new MemoryStream();

        // PNG signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        // IHDR chunk (13 bytes of data)
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // color type: RGB
        ihdr[10] = 0;  // compression method
        ihdr[11] = 0;  // filter method
        ihdr[12] = 0;  // interlace method
        WritePngChunk(ms, new byte[] { 0x49, 0x48, 0x44, 0x52 }, ihdr); // "IHDR"

        // IDAT chunk — zlib-compressed scanlines (all black)
        // Each row: 1 filter byte (0 = None) + width * 3 RGB bytes
        var rawData = new byte[height * (1 + width * 3)];
        byte[] compressedData;
        using (var compMs = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compMs, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(rawData, 0, rawData.Length);
            compressedData = compMs.ToArray();
        }
        WritePngChunk(ms, new byte[] { 0x49, 0x44, 0x41, 0x54 }, compressedData); // "IDAT"

        // IEND chunk
        WritePngChunk(ms, new byte[] { 0x49, 0x45, 0x4E, 0x44 }, Array.Empty<byte>()); // "IEND"

        return ms.ToArray();
    }

    /// <summary>Writes a single PNG chunk (length + type + data + CRC32).</summary>
    private static void WritePngChunk(Stream stream, byte[] type, byte[] data)
    {
        // Length (4 bytes, big-endian)
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len, 0, 4);

        // Type (4 bytes)
        stream.Write(type, 0, 4);

        // Data
        if (data.Length > 0) stream.Write(data, 0, data.Length);

        // CRC32 of type + data (big-endian)
        var crcInput = new byte[4 + data.Length];
        Buffer.BlockCopy(type, 0, crcInput, 0, 4);
        if (data.Length > 0) Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
        var hash = Crc32.Hash(crcInput);
        // Crc32.Hash returns LE bytes; PNG requires big-endian
        Array.Reverse(hash);
        stream.Write(hash, 0, 4);
    }
}
