using System.Text.RegularExpressions;
using CustomSkillSample.Models;
using Microsoft.AspNetCore.Mvc;

namespace CustomSkillSample.Controllers;

/// <summary>
/// Sample custom skills that can be used with Azure AI Search skillsets.
/// These demonstrate the request/response format expected by the WebApiSkill.
/// </summary>
[ApiController]
[Route("api/skills")]
public class SkillsController : ControllerBase
{
    private readonly ILogger<SkillsController> _logger;

    public SkillsController(ILogger<SkillsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// A simple text length skill that counts characters and words.
    /// Input: text (string)
    /// Output: characterCount (int), wordCount (int), sentenceCount (int)
    /// </summary>
    [HttpPost("text-stats")]
    public ActionResult<CustomSkillResponse> TextStats([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("TextStats skill received {Count} records", request.Values.Count);

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var text = GetStringValue(record.Data, "text") ?? string.Empty;

                outputRecord.Data["characterCount"] = text.Length;
                outputRecord.Data["wordCount"] = string.IsNullOrWhiteSpace(text) 
                    ? 0 
                    : text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                outputRecord.Data["sentenceCount"] = string.IsNullOrWhiteSpace(text)
                    ? 0
                    : Regex.Matches(text, @"[.!?]+").Count;
            }
            catch (Exception ex)
            {
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error processing text stats: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// A simple keyword extraction skill using basic word frequency analysis.
    /// Input: text (string), maxKeywords (int, optional, default 5)
    /// Output: keywords (string[])
    /// </summary>
    [HttpPost("extract-keywords")]
    public ActionResult<CustomSkillResponse> ExtractKeywords([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("ExtractKeywords skill received {Count} records", request.Values.Count);

        // Common stop words to filter out
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "been",
            "be", "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "shall", "can", "need", "dare", "ought",
            "used", "it", "its", "this", "that", "these", "those", "i", "you", "he",
            "she", "we", "they", "what", "which", "who", "whom", "when", "where", "why",
            "how", "all", "each", "every", "both", "few", "more", "most", "other", "some",
            "such", "no", "not", "only", "same", "so", "than", "too", "very"
        };

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var text = GetStringValue(record.Data, "text") ?? string.Empty;
                var maxKeywords = GetIntValue(record.Data, "maxKeywords") ?? 5;

                // Extract words and count frequency
                var words = Regex.Matches(text.ToLower(), @"\b[a-z]{3,}\b")
                    .Select(m => m.Value)
                    .Where(w => !stopWords.Contains(w))
                    .GroupBy(w => w)
                    .OrderByDescending(g => g.Count())
                    .Take(maxKeywords)
                    .Select(g => g.Key)
                    .ToArray();

                outputRecord.Data["keywords"] = words;
            }
            catch (Exception ex)
            {
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error extracting keywords: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// A simple sentiment analysis skill using keyword-based scoring.
    /// Input: text (string)
    /// Output: sentiment (string: positive/negative/neutral), score (double: -1 to 1)
    /// </summary>
    [HttpPost("analyze-sentiment")]
    public ActionResult<CustomSkillResponse> AnalyzeSentiment([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("AnalyzeSentiment skill received {Count} records", request.Values.Count);

        var positiveWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "good", "great", "excellent", "amazing", "wonderful", "fantastic", "awesome",
            "love", "loved", "loving", "like", "liked", "happy", "joy", "joyful",
            "best", "better", "perfect", "beautiful", "brilliant", "outstanding",
            "positive", "success", "successful", "win", "winner", "winning", "benefit"
        };

        var negativeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bad", "terrible", "awful", "horrible", "poor", "worse", "worst",
            "hate", "hated", "hating", "dislike", "sad", "angry", "upset",
            "fail", "failed", "failure", "problem", "issue", "error", "bug",
            "negative", "loss", "lose", "losing", "damage", "broken", "wrong"
        };

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var text = GetStringValue(record.Data, "text") ?? string.Empty;
                var words = Regex.Matches(text.ToLower(), @"\b[a-z]+\b").Select(m => m.Value).ToList();

                var positiveCount = words.Count(w => positiveWords.Contains(w));
                var negativeCount = words.Count(w => negativeWords.Contains(w));
                var totalSentimentWords = positiveCount + negativeCount;

                double score = 0;
                string sentiment = "neutral";

                if (totalSentimentWords > 0)
                {
                    score = (double)(positiveCount - negativeCount) / totalSentimentWords;
                    sentiment = score > 0.1 ? "positive" : score < -0.1 ? "negative" : "neutral";
                }

                outputRecord.Data["sentiment"] = sentiment;
                outputRecord.Data["score"] = Math.Round(score, 2);
                outputRecord.Data["positiveWordCount"] = positiveCount;
                outputRecord.Data["negativeWordCount"] = negativeCount;
            }
            catch (Exception ex)
            {
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error analyzing sentiment: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// A PII detection skill that finds potential personal information.
    /// Input: text (string)
    /// Output: piiDetected (bool), piiTypes (string[]), maskedText (string)
    /// </summary>
    [HttpPost("detect-pii")]
    public ActionResult<CustomSkillResponse> DetectPii([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("DetectPii skill received {Count} records", request.Values.Count);

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var text = GetStringValue(record.Data, "text") ?? string.Empty;
                var piiTypes = new List<string>();
                var maskedText = text;

                // Email detection
                var emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
                if (Regex.IsMatch(text, emailPattern))
                {
                    piiTypes.Add("email");
                    maskedText = Regex.Replace(maskedText, emailPattern, "[EMAIL]");
                }

                // Phone number detection (US format)
                var phonePattern = @"\b(\+?1[-.\s]?)?(\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}\b";
                if (Regex.IsMatch(text, phonePattern))
                {
                    piiTypes.Add("phone");
                    maskedText = Regex.Replace(maskedText, phonePattern, "[PHONE]");
                }

                // SSN detection (US format)
                var ssnPattern = @"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b";
                if (Regex.IsMatch(text, ssnPattern))
                {
                    piiTypes.Add("ssn");
                    maskedText = Regex.Replace(maskedText, ssnPattern, "[SSN]");
                }

                // Credit card detection (basic)
                var ccPattern = @"\b(?:\d{4}[-\s]?){3}\d{4}\b";
                if (Regex.IsMatch(text, ccPattern))
                {
                    piiTypes.Add("creditCard");
                    maskedText = Regex.Replace(maskedText, ccPattern, "[CREDIT_CARD]");
                }

                outputRecord.Data["piiDetected"] = piiTypes.Count > 0;
                outputRecord.Data["piiTypes"] = piiTypes.ToArray();
                outputRecord.Data["maskedText"] = maskedText;
                outputRecord.Data["piiCount"] = piiTypes.Count;
            }
            catch (Exception ex)
            {
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error detecting PII: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// A text summarization skill that creates a simple extractive summary.
    /// Input: text (string), maxSentences (int, optional, default 3)
    /// Output: summary (string)
    /// </summary>
    [HttpPost("summarize")]
    public ActionResult<CustomSkillResponse> Summarize([FromBody] CustomSkillRequest request)
    {
        _logger.LogInformation("Summarize skill received {Count} records", request.Values.Count);

        var response = new CustomSkillResponse();

        foreach (var record in request.Values)
        {
            var outputRecord = new CustomSkillOutputRecord { RecordId = record.RecordId };

            try
            {
                var text = GetStringValue(record.Data, "text") ?? string.Empty;
                var maxSentences = GetIntValue(record.Data, "maxSentences") ?? 3;

                // Simple extractive summarization: take first N sentences
                var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Take(maxSentences)
                    .ToArray();

                outputRecord.Data["summary"] = string.Join(" ", sentences);
                outputRecord.Data["originalLength"] = text.Length;
                outputRecord.Data["summaryLength"] = string.Join(" ", sentences).Length;
            }
            catch (Exception ex)
            {
                outputRecord.Errors.Add(new CustomSkillMessage
                {
                    Message = $"Error summarizing text: {ex.Message}",
                    StatusCode = 500
                });
            }

            response.Values.Add(outputRecord);
        }

        return Ok(response);
    }

    /// <summary>
    /// Health check endpoint for the custom skill service.
    /// </summary>
    [HttpGet("health")]
    public ActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    #region Helper Methods

    private static string? GetStringValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    private static int? GetIntValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is int intValue) return intValue;
            if (value is long longValue) return (int)longValue;
            if (value is double doubleValue) return (int)doubleValue;
            if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    #endregion
}
