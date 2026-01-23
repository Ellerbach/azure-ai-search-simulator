using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Factory for creating and managing document crackers.
/// </summary>
public interface IDocumentCrackerFactory
{
    /// <summary>
    /// Gets a cracker that can handle the given content type and extension.
    /// </summary>
    IDocumentCracker? GetCracker(string contentType, string extension);

    /// <summary>
    /// Checks if any cracker can handle the given content type or extension.
    /// </summary>
    bool CanCrack(string contentType, string extension);

    /// <summary>
    /// Gets all supported content types.
    /// </summary>
    IEnumerable<string> SupportedContentTypes { get; }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }
}

/// <summary>
/// Default implementation of document cracker factory.
/// </summary>
public class DocumentCrackerFactory : IDocumentCrackerFactory
{
    private readonly ILogger<DocumentCrackerFactory> _logger;
    private readonly IEnumerable<IDocumentCracker> _crackers;

    public DocumentCrackerFactory(
        ILogger<DocumentCrackerFactory> logger,
        IEnumerable<IDocumentCracker> crackers)
    {
        _logger = logger;
        _crackers = crackers;
    }

    public IEnumerable<string> SupportedContentTypes =>
        _crackers.SelectMany(c => c.SupportedContentTypes).Distinct();

    public IEnumerable<string> SupportedExtensions =>
        _crackers.SelectMany(c => c.SupportedExtensions).Distinct();

    public IDocumentCracker? GetCracker(string contentType, string extension)
    {
        var cracker = _crackers.FirstOrDefault(c => c.CanHandle(contentType, extension));

        if (cracker == null)
        {
            _logger.LogDebug(
                "No document cracker found for content type '{ContentType}' or extension '{Extension}'",
                contentType, extension);
        }
        else
        {
            _logger.LogDebug(
                "Using {CrackerType} for content type '{ContentType}', extension '{Extension}'",
                cracker.GetType().Name, contentType, extension);
        }

        return cracker;
    }

    public bool CanCrack(string contentType, string extension)
    {
        return _crackers.Any(c => c.CanHandle(contentType, extension));
    }
}

/// <summary>
/// Extension methods for registering document crackers.
/// </summary>
public static class DocumentCrackerExtensions
{
    /// <summary>
    /// Cracks a document using the appropriate cracker.
    /// </summary>
    public static async Task<CrackedDocument> CrackDocumentAsync(
        this IDocumentCrackerFactory factory,
        byte[] content,
        string fileName,
        string contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var cracker = factory.GetCracker(contentType, extension);

        if (cracker == null)
        {
            return new CrackedDocument
            {
                Success = false,
                ErrorMessage = $"No cracker available for content type '{contentType}' or extension '{extension}'"
            };
        }

        return await cracker.CrackAsync(content, fileName, contentType);
    }
}
