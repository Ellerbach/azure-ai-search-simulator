using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Skills;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for DocumentExtractionSkillExecutor.
/// </summary>
public class DocumentExtractionSkillExecutorTests
{
    private readonly Mock<IDocumentCrackerFactory> _crackerFactoryMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<DocumentExtractionSkillExecutor>> _loggerMock;
    private readonly DocumentExtractionSkillExecutor _executor;

    public DocumentExtractionSkillExecutorTests()
    {
        _crackerFactoryMock = new Mock<IDocumentCrackerFactory>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<DocumentExtractionSkillExecutor>>();
        _executor = new DocumentExtractionSkillExecutor(
            _crackerFactoryMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void ODataType_ShouldBeCorrect()
    {
        Assert.Equal("#Microsoft.Skills.Util.DocumentExtractionSkill", _executor.ODataType);
    }

    // ─── Missing / invalid inputs ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingFileDataInput_ShouldFail()
    {
        var skill = CreateSkill(inputs: new List<SkillInput>());
        var document = new EnrichedDocument();

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("file_data"));
    }

    [Fact]
    public async Task ExecuteAsync_FileDataNotInDocument_ShouldWarn()
    {
        var skill = CreateSkill();
        var document = new EnrichedDocument();
        // Don't set /document/file_data — it's missing

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("No file_data found"));
    }

    [Fact]
    public async Task ExecuteAsync_FileDataMissingTypeAndDataAndUrl_ShouldFail()
    {
        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?> { ["foo"] = "bar" });

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("$type") || e.Contains("data") || e.Contains("url"));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidBase64_ShouldFail()
    {
        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = "!!!NOT_VALID_BASE64!!!"
        });

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("base64") || e.Contains("Base64"));
    }

    // ─── Base64 plain text extraction ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Base64PlainText_ExtractsContent()
    {
        var text = "Hello, world! This is a test document.";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(text);

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        var content = document.GetValue<string>("/document/extractedText");
        Assert.Equal(text, content);
    }

    [Fact]
    public async Task ExecuteAsync_Base64PlainText_NormalizedImagesIsEmptyArray()
    {
        var text = "Some content";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(text);

        await _executor.ExecuteAsync(skill, document);

        var images = document.GetValue("/document/normalized_images");
        Assert.NotNull(images);
        // Should be an empty list
        Assert.IsAssignableFrom<IEnumerable<object?>>(images);
        Assert.Empty((IEnumerable<object?>)images!);
    }

    // ─── Parsing mode: text ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ParsingModeText_ReturnsRawUtf8()
    {
        var text = "Raw text with <html> tags that should be preserved";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill(parsingMode: "text");
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        // No cracker should be called in text mode
        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        var content = document.GetValue<string>("/document/extractedText");
        Assert.Equal(text, content);

        // Verify cracker was NOT called
        _crackerFactoryMock.Verify(
            f => f.CanCrack(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // ─── Parsing mode: json ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ParsingModeJson_ReturnsJsonString()
    {
        var jsonObj = new { name = "test", value = 42 };
        var jsonText = JsonSerializer.Serialize(jsonObj);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonText));

        var skill = CreateSkill(parsingMode: "json");
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        var content = document.GetValue<string>("/document/extractedText");
        Assert.NotNull(content);
        // Should be valid JSON
        var parsed = JsonDocument.Parse(content!);
        Assert.Equal("test", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, parsed.RootElement.GetProperty("value").GetInt32());
    }

    // ─── dataToExtract: allMetadata ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AllMetadata_ReturnsEmptyContent()
    {
        var text = "This text should not appear in output";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill(dataToExtract: "allMetadata");
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(text);

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        var content = document.GetValue<string>("/document/extractedText");
        Assert.Equal(string.Empty, content);
    }

    // ─── file_data without $type but with data ──────────────────────

    [Fact]
    public async Task ExecuteAsync_FileDataWithoutType_ButWithData_StillWorks()
    {
        var text = "Lenient parsing test";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        // No $type field, just data
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["data"] = base64
        });

        SetupPlainTextCracker(text);

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        var content = document.GetValue<string>("/document/extractedText");
        Assert.Equal(text, content);
    }

    // ─── Output written to correct paths ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OutputsWrittenToCorrectTargetPaths()
    {
        var text = "Test content";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var skill = new Skill
        {
            ODataType = "#Microsoft.Skills.Util.DocumentExtractionSkill",
            Context = "/document",
            Inputs = new List<SkillInput>
            {
                new() { Name = "file_data", Source = "/document/file_data" }
            },
            Outputs = new List<SkillOutput>
            {
                new() { Name = "content", TargetName = "myCustomContent" },
                new() { Name = "normalized_images", TargetName = "myImages" }
            }
        };

        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        SetupPlainTextCracker(text);

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        Assert.Equal(text, document.GetValue<string>("/document/myCustomContent"));
        var images = document.GetValue("/document/myImages");
        Assert.NotNull(images);
    }

    // ─── Content type detection ─────────────────────────────────────

    [Fact]
    public void DetectContentType_PdfBytes_ReturnsPdf()
    {
        // %PDF magic bytes
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("application/pdf", contentType);
        Assert.Equal(".pdf", extension);
    }

    [Fact]
    public void DetectContentType_ZipBytes_ReturnsDocx()
    {
        // PK\x03\x04 magic bytes (ZIP / Office Open XML)
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", contentType);
        Assert.Equal(".docx", extension);
    }

    [Fact]
    public void DetectContentType_JsonBytes_ReturnsJson()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"key\": \"value\"}");
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("application/json", contentType);
        Assert.Equal(".json", extension);
    }

    [Fact]
    public void DetectContentType_HtmlBytes_ReturnsHtml()
    {
        var bytes = Encoding.UTF8.GetBytes("<html><body>Hello</body></html>");
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("text/html", contentType);
        Assert.Equal(".html", extension);
    }

    [Fact]
    public void DetectContentType_PlainTextBytes_ReturnsPlainText()
    {
        var bytes = Encoding.UTF8.GetBytes("Just some plain text without any special markers.");
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("text/plain", contentType);
        Assert.Equal(".txt", extension);
    }

    [Fact]
    public void DetectContentType_JsonArrayBytes_ReturnsJson()
    {
        var bytes = Encoding.UTF8.GetBytes("[{\"a\":1},{\"b\":2}]");
        var (contentType, extension) = DocumentExtractionSkillExecutor.DetectContentType(bytes);
        Assert.Equal("application/json", contentType);
        Assert.Equal(".json", extension);
    }

    // ─── Empty file_data.data ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyBase64Data_ShouldFail()
    {
        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = ""
        });

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    // ─── PDF cracking via base64 ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Base64Pdf_UsesPdfCracker()
    {
        // Build a fake PDF (just magic bytes + text — the cracker mock handles it)
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var base64 = Convert.ToBase64String(pdfBytes);

        var skill = CreateSkill();
        var document = new EnrichedDocument();
        document.SetValue("/document/file_data", new Dictionary<string, object?>
        {
            ["$type"] = "file",
            ["data"] = base64
        });

        var pdfCrackerMock = new Mock<IDocumentCracker>();
        pdfCrackerMock
            .Setup(c => c.CrackAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CrackedDocument
            {
                Success = true,
                Content = "Extracted PDF text",
                PageCount = 3,
                Title = "Test PDF"
            });

        _crackerFactoryMock
            .Setup(f => f.CanCrack("application/pdf", ".pdf"))
            .Returns(true);

        _crackerFactoryMock
            .Setup(f => f.GetCracker("application/pdf", ".pdf"))
            .Returns(pdfCrackerMock.Object);

        var result = await _executor.ExecuteAsync(skill, document);

        Assert.True(result.Success);
        Assert.Equal("Extracted PDF text", document.GetValue<string>("/document/extractedText"));
    }

    // ─── Skill model properties deserialization ─────────────────────

    [Fact]
    public void Skill_DocumentExtraction_ShouldDeserializeAllProperties()
    {
        var json = """
        {
            "@odata.type": "#Microsoft.Skills.Util.DocumentExtractionSkill",
            "name": "extract-text",
            "context": "/document",
            "parsingMode": "default",
            "dataToExtract": "contentAndMetadata",
            "configuration": {
                "imageAction": "generateNormalizedImages",
                "normalizedImageMaxWidth": 2000,
                "normalizedImageMaxHeight": 2000
            },
            "inputs": [
                { "name": "file_data", "source": "/document/file_data" }
            ],
            "outputs": [
                { "name": "content", "targetName": "extractedText" },
                { "name": "normalized_images", "targetName": "normalized_images" }
            ]
        }
        """;

        var skill = JsonSerializer.Deserialize<Skill>(json);

        Assert.NotNull(skill);
        Assert.Equal("#Microsoft.Skills.Util.DocumentExtractionSkill", skill!.ODataType);
        Assert.Equal("default", skill.SkillParsingMode);
        Assert.Equal("contentAndMetadata", skill.DataToExtract);
        Assert.NotNull(skill.SkillConfiguration);
        Assert.True(skill.SkillConfiguration!.ContainsKey("imageAction"));
        Assert.Single(skill.Inputs);
        Assert.Equal("file_data", skill.Inputs[0].Name);
        Assert.Equal(2, skill.Outputs.Count);
    }

    [Fact]
    public void Skill_DocumentExtraction_FullSkillsetDeserialization()
    {
        var json = """
        {
            "name": "pdf-extraction-skillset",
            "description": "Retrieve PDFs and extract text",
            "skills": [
                {
                    "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
                    "name": "retrieve-pdf",
                    "context": "/document",
                    "uri": "https://my-func/api/retrieve-document",
                    "httpMethod": "POST",
                    "timeout": "PT90S",
                    "batchSize": 1,
                    "inputs": [
                        { "name": "documentId", "source": "/document/documentId" }
                    ],
                    "outputs": [
                        { "name": "file_data", "targetName": "file_data" }
                    ]
                },
                {
                    "@odata.type": "#Microsoft.Skills.Util.DocumentExtractionSkill",
                    "name": "extract-text-from-pdf",
                    "context": "/document",
                    "parsingMode": "default",
                    "dataToExtract": "contentAndMetadata",
                    "configuration": {
                        "imageAction": "generateNormalizedImages"
                    },
                    "inputs": [
                        { "name": "file_data", "source": "/document/file_data" }
                    ],
                    "outputs": [
                        { "name": "content", "targetName": "extractedText" },
                        { "name": "normalized_images", "targetName": "normalized_images" }
                    ]
                }
            ]
        }
        """;

        var skillset = JsonSerializer.Deserialize<Skillset>(json);

        Assert.NotNull(skillset);
        Assert.Equal(2, skillset!.Skills.Count);

        var webApiSkill = skillset.Skills[0];
        Assert.Equal("#Microsoft.Skills.Custom.WebApiSkill", webApiSkill.ODataType);
        Assert.Equal("https://my-func/api/retrieve-document", webApiSkill.Uri);

        var docExtrSkill = skillset.Skills[1];
        Assert.Equal("#Microsoft.Skills.Util.DocumentExtractionSkill", docExtrSkill.ODataType);
        Assert.Equal("default", docExtrSkill.SkillParsingMode);
        Assert.Equal("contentAndMetadata", docExtrSkill.DataToExtract);
        Assert.NotNull(docExtrSkill.SkillConfiguration);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static Skill CreateSkill(
        string? parsingMode = null,
        string? dataToExtract = null,
        List<SkillInput>? inputs = null)
    {
        return new Skill
        {
            ODataType = "#Microsoft.Skills.Util.DocumentExtractionSkill",
            Context = "/document",
            SkillParsingMode = parsingMode,
            DataToExtract = dataToExtract,
            Inputs = inputs ?? new List<SkillInput>
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

    private void SetupPlainTextCracker(string expectedContent)
    {
        var crackerMock = new Mock<IDocumentCracker>();
        crackerMock
            .Setup(c => c.CrackAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CrackedDocument
            {
                Success = true,
                Content = expectedContent
            });

        _crackerFactoryMock
            .Setup(f => f.CanCrack(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        _crackerFactoryMock
            .Setup(f => f.GetCracker(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(crackerMock.Object);
    }
}
