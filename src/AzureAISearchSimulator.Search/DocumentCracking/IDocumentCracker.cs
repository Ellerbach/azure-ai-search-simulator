namespace AzureAISearchSimulator.Search.DocumentCracking;

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
