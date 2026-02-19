namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Represents an image extracted from a document during cracking.
/// </summary>
public class CrackedImage
{
    /// <summary>
    /// Raw image bytes (in original format: PNG, JPEG, etc.).
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// MIME content type of the image (e.g., "image/png", "image/jpeg").
    /// </summary>
    public string ContentType { get; set; } = "image/unknown";

    /// <summary>
    /// Width of the image in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the image in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Page number the image was extracted from (1-based for PDF, 0 for non-paged documents).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Character offset within the extracted text content where this image was located.
    /// For PDFs, this is the offset at the end of the page's text.
    /// </summary>
    public int ContentOffset { get; set; }

    /// <summary>
    /// Bounding box of the image on the page (if available).
    /// Values are in the coordinate system of the source document.
    /// </summary>
    public ImageBounds? Bounds { get; set; }
}

/// <summary>
/// Bounding box coordinates for an extracted image.
/// </summary>
public class ImageBounds
{
    /// <summary>
    /// Left edge X coordinate.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Bottom edge Y coordinate.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Width of the bounding box.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Height of the bounding box.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Width of the page containing the image.
    /// </summary>
    public double PageWidth { get; set; }

    /// <summary>
    /// Height of the page containing the image.
    /// </summary>
    public double PageHeight { get; set; }
}

/// <summary>
/// Result of document cracking operation.
/// </summary>
public class CrackedDocument
{
    /// <summary>
    /// Extracted text content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Document title if available.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Document author if available.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Creation date if available.
    /// </summary>
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>
    /// Last modified date if available.
    /// </summary>
    public DateTimeOffset? ModifiedDate { get; set; }

    /// <summary>
    /// Page count for paginated documents.
    /// </summary>
    public int? PageCount { get; set; }

    /// <summary>
    /// Word count.
    /// </summary>
    public int? WordCount { get; set; }

    /// <summary>
    /// Character count.
    /// </summary>
    public int? CharacterCount { get; set; }

    /// <summary>
    /// Language detected.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Additional metadata extracted from the document.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Warnings during extraction.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Whether extraction was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if extraction failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Images extracted from the document.
    /// </summary>
    public List<CrackedImage> Images { get; set; } = new();
}

/// <summary>
/// Interface for document content extraction (cracking).
/// </summary>
public interface IDocumentCracker
{
    /// <summary>
    /// Content types this cracker supports.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }

    /// <summary>
    /// File extensions this cracker supports.
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Extracts content and metadata from a document.
    /// </summary>
    /// <param name="content">Raw document bytes.</param>
    /// <param name="fileName">Original file name (for extension detection).</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>Cracked document with extracted content.</returns>
    Task<CrackedDocument> CrackAsync(byte[] content, string fileName, string contentType);

    /// <summary>
    /// Checks if this cracker can handle the given content type or extension.
    /// </summary>
    bool CanHandle(string contentType, string extension);
}
