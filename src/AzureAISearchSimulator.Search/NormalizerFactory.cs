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
            "arabic_normalization" => ApplyArabicNormalization(value),
            "cjk_width" => ApplyCjkWidthNormalization(value),
            "german_normalization" => ApplyGermanNormalization(value),
            "hindi_normalization" => ApplyHindiNormalization(value),
            "indic_normalization" => ApplyIndicNormalization(value),
            "persian_normalization" => ApplyPersianNormalization(value),
            "scandinavian_folding" => ApplyScandinavianFolding(value),
            "scandinavian_normalization" => ApplyScandinavianNormalization(value),
            "sorani_normalization" => ApplySoraniNormalization(value),
            _ => value
        };
    }

    /// <summary>
    /// Applies Arabic normalization.
    /// Normalizes orthographic variations in Arabic text:
    /// - Removes tatweel (kashida), diacritics (tashkeel)
    /// - Normalizes alef variants (آ أ إ) to bare alef (ا)
    /// - Normalizes teh marbuta (ة) to heh (ه)
    /// - Normalizes alef maksura (ى) to yeh (ي)
    /// </summary>
    private static string ApplyArabicNormalization(string value)
    {
        var result = value;

        // Remove tatweel (kashida) U+0640
        result = result.Replace("\u0640", "");

        // Remove Arabic diacritics (tashkeel) U+064B-U+065F, U+0670
        result = Regex.Replace(result, "[\u064B-\u065F\u0670]", "");

        // Normalize alef variants to bare alef
        result = result.Replace('\u0622', '\u0627'); // آ → ا
        result = result.Replace('\u0623', '\u0627'); // أ → ا
        result = result.Replace('\u0625', '\u0627'); // إ → ا

        // Normalize teh marbuta to heh
        result = result.Replace('\u0629', '\u0647'); // ة → ه

        // Normalize alef maksura to yeh
        result = result.Replace('\u0649', '\u064A'); // ى → ي

        return result;
    }

    /// <summary>
    /// Applies CJK width normalization.
    /// - Fullwidth ASCII variants (Ａ-Ｚ, ａ-ｚ, ０-９) → halfwidth ASCII (A-Z, a-z, 0-9)
    /// - Halfwidth Katakana → fullwidth Katakana
    /// </summary>
    private static string ApplyCjkWidthNormalization(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var c in value)
        {
            // Fullwidth ASCII (U+FF01-U+FF5E) → halfwidth ASCII (U+0021-U+007E)
            if (c >= '\uFF01' && c <= '\uFF5E')
            {
                builder.Append((char)(c - 0xFEE0));
            }
            // Fullwidth space → normal space
            else if (c == '\u3000')
            {
                builder.Append(' ');
            }
            // Halfwidth Katakana (U+FF65-U+FF9F) → fullwidth Katakana
            else if (c >= '\uFF65' && c <= '\uFF9F')
            {
                var katakanaOffset = c - '\uFF65';
                // Map halfwidth katakana to fullwidth equivalents
                char[] fullwidthKatakana = {
                    '\u30FB', '\u30F2', '\u30A1', '\u30A3', '\u30A5', '\u30A7', '\u30A9',
                    '\u30E3', '\u30E5', '\u30E7', '\u30C3', '\u30FC', '\u30A2', '\u30A4',
                    '\u30A6', '\u30A8', '\u30AA', '\u30AB', '\u30AD', '\u30AF', '\u30B1',
                    '\u30B3', '\u30B5', '\u30B7', '\u30B9', '\u30BB', '\u30BD', '\u30BF',
                    '\u30C1', '\u30C4', '\u30C6', '\u30C8', '\u30CA', '\u30CB', '\u30CC',
                    '\u30CD', '\u30CE', '\u30CF', '\u30D2', '\u30D5', '\u30D8', '\u30DB',
                    '\u30DE', '\u30DF', '\u30E0', '\u30E1', '\u30E2', '\u30E4', '\u30E6',
                    '\u30E8', '\u30E9', '\u30EA', '\u30EB', '\u30EC', '\u30ED', '\u30EF',
                    '\u30F3', '\u3099', '\u309A'
                };
                if (katakanaOffset < fullwidthKatakana.Length)
                {
                    builder.Append(fullwidthKatakana[katakanaOffset]);
                }
                else
                {
                    builder.Append(c);
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Applies German normalization.
    /// - ä → a, ö → o, ü → u
    /// - Ä → A, Ö → O, Ü → U
    /// - ß → ss
    /// </summary>
    private static string ApplyGermanNormalization(string value)
    {
        var result = value;
        result = result.Replace("ä", "a").Replace("Ä", "A");
        result = result.Replace("ö", "o").Replace("Ö", "O");
        result = result.Replace("ü", "u").Replace("Ü", "U");
        result = result.Replace("ß", "ss");
        return result;
    }

    /// <summary>
    /// Applies Hindi normalization.
    /// Normalizes Devanagari text by standardizing Unicode representations:
    /// - Normalizes nukta-based composites to their canonical forms
    /// - Normalizes visarga to aha
    /// - Removes Chandrabindu when followed by vowel signs
    /// </summary>
    private static string ApplyHindiNormalization(string value)
    {
        var result = value;

        // Normalize nukta composites: letter + nukta → pre-composed form
        // क़ (क + ़) → क
        result = result.Replace("\u0915\u093C", "\u0915"); // क़ → क
        result = result.Replace("\u0916\u093C", "\u0916"); // ख़ → ख
        result = result.Replace("\u0917\u093C", "\u0917"); // ग़ → ग
        result = result.Replace("\u091C\u093C", "\u091C"); // ज़ → ज
        result = result.Replace("\u0921\u093C", "\u0921"); // ड़ → ड
        result = result.Replace("\u0922\u093C", "\u0922"); // ढ़ → ढ
        result = result.Replace("\u092B\u093C", "\u092B"); // फ़ → फ
        result = result.Replace("\u092F\u093C", "\u092F"); // य़ → य

        // Normalize chandra vowels
        result = result.Replace('\u0929', '\u0928'); // ऩ → न
        result = result.Replace('\u0931', '\u0930'); // ऱ → र
        result = result.Replace('\u0934', '\u0933'); // ऴ → ळ

        // Remove nukta (independent)
        result = result.Replace("\u093C", "");

        return result;
    }

    /// <summary>
    /// Applies Indic normalization.
    /// Normalizes Unicode representations across Indic scripts (Devanagari, Bengali, etc.).
    /// Primarily handles zero-width joiners/non-joiners and common normalization.
    /// </summary>
    private static string ApplyIndicNormalization(string value)
    {
        var result = value;

        // Remove zero-width joiner and non-joiner
        result = result.Replace("\u200D", ""); // Zero-width joiner
        result = result.Replace("\u200C", ""); // Zero-width non-joiner

        // Remove zero-width space
        result = result.Replace("\u200B", "");

        return result;
    }

    /// <summary>
    /// Applies Persian normalization.
    /// - Normalizes Arabic keh (ك U+0643) to Persian keheh (ک U+06A9)
    /// - Normalizes Arabic yeh (ي U+064A) to Persian yeh (ی U+06CC)
    /// - Removes Arabic diacritics (tashkeel)
    /// - Removes tatweel (kashida)
    /// </summary>
    private static string ApplyPersianNormalization(string value)
    {
        var result = value;

        // Normalize keh: Arabic keh → Persian keheh
        result = result.Replace('\u0643', '\u06A9');

        // Normalize yeh: Arabic yeh → Persian yeh
        result = result.Replace('\u064A', '\u06CC');

        // Remove tatweel (kashida)
        result = result.Replace("\u0640", "");

        // Remove Arabic diacritics (tashkeel)
        result = Regex.Replace(result, "[\u064B-\u065F\u0670]", "");

        // Normalize heh+hamza (ۀ U+06C0) to heh+yeh (هٔ)
        result = result.Replace('\u06C0', '\u06D5');

        return result;
    }

    /// <summary>
    /// Applies Scandinavian folding.
    /// Folds Scandinavian-specific characters to simpler forms:
    /// - å → a, Å → A
    /// - ä, æ → a; Ä, Æ → A
    /// - ö, ø → o; Ö, Ø → O
    /// </summary>
    private static string ApplyScandinavianFolding(string value)
    {
        var result = value;
        result = result.Replace('å', 'a').Replace('Å', 'A');
        result = result.Replace('ä', 'a').Replace('Ä', 'A');
        result = result.Replace('æ', 'a').Replace('Æ', 'A');
        result = result.Replace('ö', 'o').Replace('Ö', 'O');
        result = result.Replace('ø', 'o').Replace('Ø', 'O');
        return result;
    }

    /// <summary>
    /// Applies Scandinavian normalization.
    /// Normalizes interchangeable Scandinavian characters:
    /// - ä, æ → å (interchangeable in some Scandinavian contexts)
    /// - ö, ø → ö (interchangeable in some Scandinavian contexts)
    /// </summary>
    private static string ApplyScandinavianNormalization(string value)
    {
        var result = value;
        // Normalize æ → ä (both are interchangeable)
        result = result.Replace('æ', 'ä').Replace('Æ', 'Ä');
        // Normalize ø → ö (both are interchangeable)
        result = result.Replace('ø', 'ö').Replace('Ø', 'Ö');
        return result;
    }

    /// <summary>
    /// Applies Sorani (Kurdish) normalization.
    /// Normalizes Unicode representations for Sorani Kurdish:
    /// - ي (Arabic yeh U+064A) → ی (Farsi yeh U+06CC)
    /// - ك (Arabic keh U+0643) → ک (keheh U+06A9)
    /// - Normalizes heh variations
    /// </summary>
    private static string ApplySoraniNormalization(string value)
    {
        var result = value;

        // Normalize yeh
        result = result.Replace('\u064A', '\u06CC'); // Arabic yeh → Farsi yeh
        result = result.Replace('\u0649', '\u06CC'); // Alef maksura → Farsi yeh

        // Normalize keh
        result = result.Replace('\u0643', '\u06A9'); // Arabic keh → keheh

        // Normalize heh
        result = result.Replace('\u0647', '\u06D5'); // Heh → Kurdish heh

        // Remove tatweel
        result = result.Replace("\u0640", "");

        return result;
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
            "arabic_normalization",
            "asciifolding",
            "cjk_width",
            "elision",
            "german_normalization",
            "hindi_normalization",
            "indic_normalization",
            "lowercase",
            "persian_normalization",
            "scandinavian_folding",
            "scandinavian_normalization",
            "sorani_normalization",
            "trim",
            "uppercase"
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
