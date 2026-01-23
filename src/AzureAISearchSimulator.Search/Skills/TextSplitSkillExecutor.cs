using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search.Skills;

/// <summary>
/// TextSplitSkill - Splits text into chunks (pages or sentences).
/// </summary>
public class TextSplitSkillExecutor : ISkillExecutor
{
    public string ODataType => "#Microsoft.Skills.Text.SplitSkill";

    public Task<SkillExecutionResult> ExecuteAsync(
        Skill skill, 
        EnrichedDocument document, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the input text
            var textInput = skill.Inputs.FirstOrDefault(i => i.Name == "text");
            if (textInput?.Source == null)
            {
                return Task.FromResult(SkillExecutionResult.Failed("TextSplitSkill requires 'text' input"));
            }

            var context = skill.Context ?? "/document";
            var contexts = document.GetMatchingPaths(context).ToList();

            foreach (var ctx in contexts)
            {
                // Resolve the input source relative to context
                var sourcePath = ResolveSourcePath(ctx, textInput.Source);
                var text = document.GetValue<string>(sourcePath);

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                // Split the text based on mode
                var splitMode = skill.TextSplitMode?.ToLowerInvariant() ?? "pages";
                var maxLength = skill.MaximumPageLength ?? 2000;
                var overlap = skill.PageOverlapLength ?? 0;

                var chunks = splitMode switch
                {
                    "sentences" => SplitBySentences(text),
                    _ => SplitByPages(text, maxLength, overlap)
                };

                // Get output target
                var textItemsOutput = skill.Outputs.FirstOrDefault(o => o.Name == "textItems");
                var targetName = textItemsOutput?.TargetName ?? "textItems";
                var outputPath = $"{ctx}/{targetName}";

                document.SetValue(outputPath, chunks);
            }

            return Task.FromResult(SkillExecutionResult.Succeeded());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SkillExecutionResult.Failed($"TextSplitSkill error: {ex.Message}"));
        }
    }

    private static List<string> SplitByPages(string text, int maxLength, int overlap)
    {
        var chunks = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var length = Math.Min(maxLength, text.Length - position);
            
            // Try to find a natural break point
            if (position + length < text.Length)
            {
                var searchStart = Math.Max(0, length - 100);
                var chunk = text.Substring(position, length);
                
                // Look for paragraph break
                var breakPoint = chunk.LastIndexOf("\n\n", StringComparison.Ordinal);
                if (breakPoint < searchStart)
                {
                    // Look for sentence break
                    breakPoint = chunk.LastIndexOf(". ", StringComparison.Ordinal);
                }
                if (breakPoint < searchStart)
                {
                    // Look for word break
                    breakPoint = chunk.LastIndexOf(' ');
                }
                
                if (breakPoint > searchStart)
                {
                    length = breakPoint + 1;
                }
            }

            chunks.Add(text.Substring(position, length).Trim());
            position += length - overlap;
            if (position < length) position = length; // Prevent infinite loop
        }

        return chunks;
    }

    private static List<string> SplitBySentences(string text)
    {
        var sentences = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            current.Append(ch);
            if (ch is '.' or '!' or '?')
            {
                var sentence = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    sentences.Add(sentence);
                }
                current.Clear();
            }
        }

        // Add remaining text
        var remaining = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            sentences.Add(remaining);
        }

        return sentences;
    }

    private static string ResolveSourcePath(string context, string source)
    {
        // If source starts with /, it's absolute
        if (source.StartsWith("/"))
        {
            return source;
        }
        // Otherwise, it's relative to context
        return $"{context}/{source}";
    }
}
