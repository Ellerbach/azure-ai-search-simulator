using System.Text.RegularExpressions;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Factory for applying normalizations to field values for filtering, faceting, and sorting.
/// Normalizers provide case-insensitive and accent-insensitive operations.
/// Added in API version 2025-09-01.
/// </summary>
public static class NormalizerFactory
{
    /// <summary>
    /// Default English elision articles to remove.
    /// </summary>
    private static readonly string[] DefaultElisionArticles = new[]
    {
        "'s", "'S", "'t", "'T", "'ll", "'LL", "'ve", "'VE", 
        "'re", "'RE", "'d", "'D", "'m", "'M"
    };

    /// <summary>
    /// Normalizes a string value using the specified normalizer.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <param name="normalizerName">The name of the normalizer.</param>
    /// <param name="customNormalizers">Custom normalizers defined in the index.</param>
    /// <param name="charFilters">Custom character filters defined in the index.</param>
    /// <returns>The normalized string value.</returns>
    public static string Normalize(
        string? value, 
        string? normalizerName, 
        IEnumerable<CustomNormalizer>? customNormalizers = null,
        IEnumerable<CustomCharFilter>? charFilters = null)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(normalizerName))
        {
            return value ?? string.Empty;
        }

        var normalizedName = normalizerName.ToLowerInvariant();

        // Check built-in normalizers first
        var result = normalizedName switch
        {
            "lowercase" => value.ToLowerInvariant(),
            "uppercase" => value.ToUpperInvariant(),
            "standard" => ApplyStandardNormalization(value),
            "asciifolding" => RemoveDiacritics(value),
            "elision" => ApplyElision(value),
            _ => null
        };

        if (result != null)
        {
            return result;
        }

        // Check custom normalizers
        var customNormalizer = customNormalizers?
            .FirstOrDefault(n => n.Name.Equals(normalizerName, StringComparison.OrdinalIgnoreCase));

        if (customNormalizer != null)
        {
            return ApplyCustomNormalizer(value, customNormalizer, charFilters);
        }

        // Unknown normalizer - return original value
        return value;
    }

    /// <summary>
    /// Applies the standard normalizer (lowercase + ASCII folding).
    /// </summary>
    private static string ApplyStandardNormalization(string value)
    {
        // Lowercase first
        var result = value.ToLowerInvariant();

        // Apply ASCII folding (remove diacritics/accents)
        result = RemoveDiacritics(result);

        return result;
    }

    /// <summary>
    /// Applies elision removal (removes articles like l', d' in French).
    /// </summary>
    private static string ApplyElision(string value)
    {
        var result = value;
        foreach (var article in DefaultElisionArticles)
        {
            result = result.Replace(article, string.Empty);
        }
        return result;
    }

    /// <summary>
    /// Applies a custom normalizer configuration.
    /// </summary>
    private static string ApplyCustomNormalizer(
        string value, 
        CustomNormalizer normalizer, 
        IEnumerable<CustomCharFilter>? indexCharFilters)
    {
        var result = value;

        // Apply character filters first
        if (normalizer.CharFilters != null)
        {
            foreach (var charFilterName in normalizer.CharFilters)
            {
                result = ApplyCharFilter(result, charFilterName, indexCharFilters);
            }
        }

        // Apply token filters
        if (normalizer.TokenFilters != null)
        {
            foreach (var tokenFilter in normalizer.TokenFilters)
            {
                result = ApplyTokenFilter(result, tokenFilter);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a character filter to the value.
    /// </summary>
    private static string ApplyCharFilter(
        string value, 
        string charFilterName, 
        IEnumerable<CustomCharFilter>? indexCharFilters)
    {
        // Check for built-in char filters first
        var builtInResult = charFilterName.ToLowerInvariant() switch
        {
            // HTML strip - remove HTML tags
            "html_strip" => Regex.Replace(value, "<[^>]+>", ""),
            _ => (string?)null
        };

        if (builtInResult != null)
        {
            return builtInResult;
        }

        // Check for custom char filters defined in the index
        var customFilter = indexCharFilters?
            .FirstOrDefault(f => f.Name.Equals(charFilterName, StringComparison.OrdinalIgnoreCase));

        if (customFilter != null)
        {
            return ApplyCustomCharFilter(value, customFilter);
        }

        return value;
    }

    /// <summary>
    /// Applies a custom character filter (mapping or pattern_replace).
    /// </summary>
    private static string ApplyCustomCharFilter(string value, CustomCharFilter filter)
    {
        var odataType = filter.ODataType?.ToLowerInvariant() ?? string.Empty;

        // Mapping char filter
        if (odataType.Contains("mappingcharfilter"))
        {
            return ApplyMappingCharFilter(value, filter.Mappings);
        }

        // Pattern replace char filter
        if (odataType.Contains("patternreplacecharfilter"))
        {
            return ApplyPatternReplaceCharFilter(value, filter.Pattern, filter.Replacement);
        }

        return value;
    }

    /// <summary>
    /// Applies a mapping character filter.
    /// Mappings are in format "source=>target" (e.g., "á=>a", "ü=>ue").
    /// </summary>
    private static string ApplyMappingCharFilter(string value, IEnumerable<string>? mappings)
    {
        if (mappings == null)
        {
            return value;
        }

        var result = value;
        foreach (var mapping in mappings)
        {
            var parts = mapping.Split("=>", 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var source = parts[0];
                var target = parts[1];
                result = result.Replace(source, target);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a pattern replace character filter using regex.
    /// </summary>
    private static string ApplyPatternReplaceCharFilter(string value, string? pattern, string? replacement)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return value;
        }

        try
        {
            return Regex.Replace(value, pattern, replacement ?? string.Empty);
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern - return original value
            return value;
        }
    }

    /// <summary>
    /// Applies a token filter to the value.
    /// </summary>
    private static string ApplyTokenFilter(string value, string tokenFilterName)
    {
        return tokenFilterName.ToLowerInvariant() switch
        {
            "lowercase" => value.ToLowerInvariant(),
            "uppercase" => value.ToUpperInvariant(),
            "asciifolding" => RemoveDiacritics(value),
            "trim" => value.Trim(),
            "elision" => ApplyElision(value),
            _ => value
        };
    }

    /// <summary>
    /// Removes diacritics (accents) from a string.
    /// Converts characters like é→e, ñ→n, ü→u, etc.
    /// </summary>
    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder();

        foreach (var c in normalized)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>
    /// Gets a list of supported built-in normalizer names.
    /// </summary>
    public static string[] GetSupportedNormalizers()
    {
        return new[]
        {
            "lowercase",
            "uppercase",
            "standard",
            "asciifolding",
            "elision"
        };
    }

    /// <summary>
    /// Gets a list of supported token filters for custom normalizers.
    /// </summary>
    public static string[] GetSupportedTokenFilters()
    {
        return new[]
        {
            "lowercase",
            "uppercase",
            "asciifolding",
            "trim",
            "elision"
        };
    }

    /// <summary>
    /// Gets a list of supported character filter types for custom normalizers.
    /// </summary>
    public static string[] GetSupportedCharFilters()
    {
        return new[]
        {
            "html_strip",
            "#Microsoft.Azure.Search.MappingCharFilter",
            "#Microsoft.Azure.Search.PatternReplaceCharFilter"
        };
    }
}
