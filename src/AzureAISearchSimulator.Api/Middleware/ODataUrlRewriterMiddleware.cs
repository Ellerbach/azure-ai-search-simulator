using System.Text.RegularExpressions;

namespace AzureAISearchSimulator.Api.Middleware;

/// <summary>
/// Middleware to rewrite OData-style entity key URLs to simple path parameter URLs.
/// Azure AI Search SDK uses OData entity syntax like /indexes('index-name') 
/// which needs to be converted to /indexes/index-name for ASP.NET Core routing.
/// </summary>
public partial class ODataUrlRewriterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ODataUrlRewriterMiddleware> _logger;

    // Regex pattern to match OData entity key syntax: collection('key') or collection("key")
    // Examples: /indexes('pdf-documents'), /indexers('my-indexer'), /datasources('my-source')
    [GeneratedRegex(@"^(/[a-zA-Z]+)\('([^']+)'\)(.*)$", RegexOptions.Compiled)]
    private static partial Regex ODataSingleQuotePattern();

    [GeneratedRegex(@"^(/[a-zA-Z]+)\(""([^""]+)""\)(.*)$", RegexOptions.Compiled)]
    private static partial Regex ODataDoubleQuotePattern();

    public ODataUrlRewriterMiddleware(RequestDelegate next, ILogger<ODataUrlRewriterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalPath = context.Request.Path.Value ?? string.Empty;

        // Try to match and rewrite OData-style URLs
        var rewrittenPath = RewriteODataUrl(originalPath);

        if (rewrittenPath != originalPath)
        {
            _logger.LogInformation("OData URL Rewrite: {OriginalPath} -> {RewrittenPath}", 
                originalPath, rewrittenPath);
            
            // Set the new path using PathString
            context.Request.Path = new PathString(rewrittenPath);
            
            _logger.LogInformation("OData rewrite complete. New path: {NewPath}", context.Request.Path.Value);
        }
        else
        {
            _logger.LogDebug("OData URL (no rewrite needed): {Method} {Path}", 
                context.Request.Method, originalPath);
        }

        await _next(context);
    }

    private static string RewriteODataUrl(string path)
    {
        var result = path;
        var changed = true;
        
        // Keep applying rewrites until no more matches (handles nested OData patterns)
        while (changed)
        {
            changed = false;
            
            // Try single quote pattern: /collection('key') -> /collection/key
            var singleQuoteMatch = ODataSingleQuotePattern().Match(result);
            if (singleQuoteMatch.Success)
            {
                var collection = singleQuoteMatch.Groups[1].Value;
                var key = singleQuoteMatch.Groups[2].Value;
                var suffix = singleQuoteMatch.Groups[3].Value;
                result = $"{collection}/{key}{suffix}";
                changed = true;
                continue;
            }

            // Try double quote pattern: /collection("key") -> /collection/key
            var doubleQuoteMatch = ODataDoubleQuotePattern().Match(result);
            if (doubleQuoteMatch.Success)
            {
                var collection = doubleQuoteMatch.Groups[1].Value;
                var key = doubleQuoteMatch.Groups[2].Value;
                var suffix = doubleQuoteMatch.Groups[3].Value;
                result = $"{collection}/{key}{suffix}";
                changed = true;
                continue;
            }
            
            // Also check for OData patterns in the middle of the path: /something/collection('key')/rest
            var middleSingleQuote = Regex.Match(result, @"^(.+/)([a-zA-Z]+)\('([^']+)'\)(.*)$");
            if (middleSingleQuote.Success)
            {
                var prefix = middleSingleQuote.Groups[1].Value;
                var collection = middleSingleQuote.Groups[2].Value;
                var key = middleSingleQuote.Groups[3].Value;
                var suffix = middleSingleQuote.Groups[4].Value;
                result = $"{prefix}{collection}/{key}{suffix}";
                changed = true;
                continue;
            }
            
            var middleDoubleQuote = Regex.Match(result, @"^(.+/)([a-zA-Z]+)\(""([^""]+)""\)(.*)$");
            if (middleDoubleQuote.Success)
            {
                var prefix = middleDoubleQuote.Groups[1].Value;
                var collection = middleDoubleQuote.Groups[2].Value;
                var key = middleDoubleQuote.Groups[3].Value;
                var suffix = middleDoubleQuote.Groups[4].Value;
                result = $"{prefix}{collection}/{key}{suffix}";
                changed = true;
            }
        }
        
        return result;
    }
}

/// <summary>
/// Extension methods for adding the OData URL rewriter middleware.
/// </summary>
public static class ODataUrlRewriterMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware to rewrite OData-style entity key URLs to standard path parameter URLs.
    /// This should be added early in the pipeline, before routing.
    /// </summary>
    public static IApplicationBuilder UseODataUrlRewriter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ODataUrlRewriterMiddleware>();
    }
}
