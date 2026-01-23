using HtmlAgilityPack;
using System.Text;
using System.Text.RegularExpressions;

namespace AzureAISearchSimulator.Search.DocumentCracking;

/// <summary>
/// Document cracker for HTML files.
/// </summary>
public class HtmlCracker : IDocumentCracker
{
    public IEnumerable<string> SupportedContentTypes => new[]
    {
        "text/html",
        "application/xhtml+xml"
    };

    public IEnumerable<string> SupportedExtensions => new[]
    {
        ".html",
        ".htm",
        ".xhtml"
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
            var html = Encoding.UTF8.GetString(content);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                result.Title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
            }

            // Extract meta tags
            ExtractMetaTags(doc, result);

            // Remove script and style elements
            var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style|//noscript|//head");
            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove.ToList())
                {
                    node.Remove();
                }
            }

            // Extract text content
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
            var text = ExtractTextContent(bodyNode);

            // Clean up whitespace
            text = NormalizeWhitespace(text);

            result.Content = text;
            result.CharacterCount = text.Length;
            result.WordCount = CountWords(text);
            result.Success = true;

            // Count links and images
            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            var images = doc.DocumentNode.SelectNodes("//img");
            result.Metadata["linkCount"] = links?.Count ?? 0;
            result.Metadata["imageCount"] = images?.Count ?? 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Failed to parse HTML: {ex.Message}";
        }

        return Task.FromResult(result);
    }

    private static void ExtractMetaTags(HtmlDocument doc, CrackedDocument result)
    {
        var metaTags = doc.DocumentNode.SelectNodes("//meta");
        if (metaTags == null) return;

        foreach (var meta in metaTags)
        {
            var name = meta.GetAttributeValue("name", "") ?? meta.GetAttributeValue("property", "");
            var content = meta.GetAttributeValue("content", "");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(content))
                continue;

            name = name.ToLowerInvariant();

            switch (name)
            {
                case "author":
                    result.Author = content;
                    break;
                case "description":
                    result.Metadata["description"] = content;
                    break;
                case "keywords":
                    result.Metadata["keywords"] = content;
                    break;
                case "language":
                case "content-language":
                    result.Language = content;
                    break;
                default:
                    if (name.StartsWith("og:") || name.StartsWith("twitter:"))
                    {
                        result.Metadata[$"meta_{name.Replace(":", "_")}"] = content;
                    }
                    break;
            }
        }
    }

    private static string ExtractTextContent(HtmlNode node)
    {
        var text = new StringBuilder();

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                var nodeText = HtmlEntity.DeEntitize(child.InnerText);
                if (!string.IsNullOrWhiteSpace(nodeText))
                {
                    text.Append(nodeText);
                    text.Append(' ');
                }
            }
            else if (child.NodeType == HtmlNodeType.Element)
            {
                // Add line breaks for block elements
                var tagName = child.Name.ToLowerInvariant();
                if (IsBlockElement(tagName))
                {
                    text.Append('\n');
                }

                text.Append(ExtractTextContent(child));

                if (IsBlockElement(tagName))
                {
                    text.Append('\n');
                }
            }
        }

        return text.ToString();
    }

    private static bool IsBlockElement(string tagName)
    {
        return tagName switch
        {
            "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
            "li" or "ul" or "ol" or "br" or "hr" or "blockquote" or
            "table" or "tr" or "article" or "section" or "header" or "footer" => true,
            _ => false
        };
    }

    private static string NormalizeWhitespace(string text)
    {
        // Replace multiple whitespace with single space
        text = Regex.Replace(text, @"[ \t]+", " ");
        // Replace multiple newlines with double newline
        text = Regex.Replace(text, @"\n\s*\n+", "\n\n");
        return text.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
