using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Search;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for NormalizerFactory - API version 2025-09-01 feature.
/// </summary>
public class NormalizerFactoryTests
{
    [Fact]
    public void Normalize_WithLowercase_ShouldConvertToLower()
    {
        // Arrange
        var value = "Hello WORLD";

        // Act
        var result = NormalizerFactory.Normalize(value, "lowercase");

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Normalize_WithUppercase_ShouldConvertToUpper()
    {
        // Arrange
        var value = "Hello World";

        // Act
        var result = NormalizerFactory.Normalize(value, "uppercase");

        // Assert
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void Normalize_WithStandard_ShouldLowercaseAndRemoveAccents()
    {
        // Arrange
        var value = "Café Résumé MÜNCHEN";

        // Act
        var result = NormalizerFactory.Normalize(value, "standard");

        // Assert
        Assert.Equal("cafe resume munchen", result);
    }

    [Fact]
    public void Normalize_WithNullValue_ShouldReturnEmpty()
    {
        // Act
        var result = NormalizerFactory.Normalize(null, "lowercase");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_WithNullNormalizer_ShouldReturnOriginal()
    {
        // Arrange
        var value = "Hello World";

        // Act
        var result = NormalizerFactory.Normalize(value, null);

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Normalize_WithUnknownNormalizer_ShouldReturnOriginal()
    {
        // Arrange
        var value = "Hello World";

        // Act
        var result = NormalizerFactory.Normalize(value, "unknown_normalizer");

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Normalize_WithCustomNormalizer_ShouldApplyTokenFilters()
    {
        // Arrange
        var value = "Café WORLD";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "my_custom",
                TokenFilters = new List<string> { "lowercase", "asciifolding" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "my_custom", customNormalizers);

        // Assert
        Assert.Equal("cafe world", result);
    }

    [Fact]
    public void Normalize_WithCustomNormalizer_TrimFilter_ShouldTrim()
    {
        // Arrange
        var value = "  Hello World  ";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "trim_normalizer",
                TokenFilters = new List<string> { "trim" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "trim_normalizer", customNormalizers);

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Normalize_AccentedCharacters_ShouldBeNormalized()
    {
        // Arrange - test various accented characters
        var testCases = new Dictionary<string, string>
        {
            { "café", "cafe" },
            { "naïve", "naive" },
            { "résumé", "resume" },
            { "piñata", "pinata" },
            { "über", "uber" },
            { "Ångström", "angstrom" },
            { "日本語", "日本語" } // Non-latin should pass through
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = NormalizerFactory.Normalize(input, "standard");
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void GetSupportedNormalizers_ShouldReturnBuiltInList()
    {
        // Act
        var normalizers = NormalizerFactory.GetSupportedNormalizers();

        // Assert
        Assert.Contains("lowercase", normalizers);
        Assert.Contains("uppercase", normalizers);
        Assert.Contains("standard", normalizers);
        Assert.Contains("asciifolding", normalizers);
        Assert.Contains("elision", normalizers);
    }

    [Fact]
    public void GetSupportedTokenFilters_ShouldReturnFilterList()
    {
        // Act
        var filters = NormalizerFactory.GetSupportedTokenFilters();

        // Assert
        Assert.Contains("lowercase", filters);
        Assert.Contains("uppercase", filters);
        Assert.Contains("asciifolding", filters);
        Assert.Contains("trim", filters);
        Assert.Contains("elision", filters);
    }

    [Fact]
    public void GetSupportedCharFilters_ShouldReturnCharFilterList()
    {
        // Act
        var filters = NormalizerFactory.GetSupportedCharFilters();

        // Assert
        Assert.Contains("html_strip", filters);
        Assert.Contains("#Microsoft.Azure.Search.MappingCharFilter", filters);
        Assert.Contains("#Microsoft.Azure.Search.PatternReplaceCharFilter", filters);
    }

    [Fact]
    public void Normalize_WithAsciiFolding_ShouldRemoveAccents()
    {
        // Arrange
        var value = "Café Résumé MÜNCHEN";

        // Act
        var result = NormalizerFactory.Normalize(value, "asciifolding");

        // Assert - should remove accents but keep case
        Assert.Equal("Cafe Resume MUNCHEN", result);
    }

    [Fact]
    public void Normalize_WithElision_ShouldRemoveEnglishContractions()
    {
        // Arrange
        var value = "it's don't I'll we've";

        // Act
        var result = NormalizerFactory.Normalize(value, "elision");

        // Assert
        Assert.Equal("it don I we", result);
    }

    [Fact]
    public void Normalize_WithElision_ShouldHandleMixedCase()
    {
        // Arrange
        var value = "He's She'll They've";

        // Act
        var result = NormalizerFactory.Normalize(value, "elision");

        // Assert
        Assert.Equal("He She They", result);
    }

    [Fact]
    public void Normalize_WithCustomMappingCharFilter_ShouldApplyMappings()
    {
        // Arrange
        var value = "Hello ü World ö";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "german_mapper",
                CharFilters = new List<string> { "german_chars" },
                TokenFilters = new List<string>()
            }
        };
        var charFilters = new List<CustomCharFilter>
        {
            new()
            {
                Name = "german_chars",
                ODataType = "#Microsoft.Azure.Search.MappingCharFilter",
                Mappings = new List<string> { "ü=>ue", "ö=>oe" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "german_mapper", customNormalizers, charFilters);

        // Assert
        Assert.Equal("Hello ue World oe", result);
    }

    [Fact]
    public void Normalize_WithPatternReplaceCharFilter_ShouldApplyRegex()
    {
        // Arrange
        var value = "Hello123World456";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "remove_numbers",
                CharFilters = new List<string> { "digit_remover" },
                TokenFilters = new List<string>()
            }
        };
        var charFilters = new List<CustomCharFilter>
        {
            new()
            {
                Name = "digit_remover",
                ODataType = "#Microsoft.Azure.Search.PatternReplaceCharFilter",
                Pattern = @"\d+",
                Replacement = ""
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "remove_numbers", customNormalizers, charFilters);

        // Assert
        Assert.Equal("HelloWorld", result);
    }

    [Fact]
    public void Normalize_WithPatternReplaceCharFilter_ShouldReplacePatterns()
    {
        // Arrange
        var value = "foo_bar_baz";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "underscore_to_dash",
                CharFilters = new List<string> { "dash_replacer" },
                TokenFilters = new List<string>()
            }
        };
        var charFilters = new List<CustomCharFilter>
        {
            new()
            {
                Name = "dash_replacer",
                ODataType = "#Microsoft.Azure.Search.PatternReplaceCharFilter",
                Pattern = "_",
                Replacement = "-"
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "underscore_to_dash", customNormalizers, charFilters);

        // Assert
        Assert.Equal("foo-bar-baz", result);
    }

    [Fact]
    public void Normalize_WithCharFiltersAndTokenFilters_ShouldApplyInOrder()
    {
        // Arrange - char filters first, then token filters
        var value = "HELLO_WORLD";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "complex_normalizer",
                CharFilters = new List<string> { "underscore_to_space" },
                TokenFilters = new List<string> { "lowercase" }
            }
        };
        var charFilters = new List<CustomCharFilter>
        {
            new()
            {
                Name = "underscore_to_space",
                ODataType = "#Microsoft.Azure.Search.PatternReplaceCharFilter",
                Pattern = "_",
                Replacement = " "
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "complex_normalizer", customNormalizers, charFilters);

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Normalize_WithInvalidRegexPattern_ShouldReturnOriginal()
    {
        // Arrange - invalid regex pattern
        var value = "Hello World";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "bad_pattern",
                CharFilters = new List<string> { "invalid_filter" },
                TokenFilters = new List<string>()
            }
        };
        var charFilters = new List<CustomCharFilter>
        {
            new()
            {
                Name = "invalid_filter",
                ODataType = "#Microsoft.Azure.Search.PatternReplaceCharFilter",
                Pattern = "[invalid(regex", // Invalid regex
                Replacement = ""
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "bad_pattern", customNormalizers, charFilters);

        // Assert - should return original value when regex is invalid
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Normalize_ElisionTokenFilter_InCustomNormalizer()
    {
        // Arrange
        var value = "It's THE best we've SEEN";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "english_normalizer",
                TokenFilters = new List<string> { "elision", "lowercase" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "english_normalizer", customNormalizers);

        // Assert
        Assert.Equal("it the best we seen", result);
    }

    [Fact]
    public void Normalize_CaseInsensitiveNormalizerName_ShouldWork()
    {
        // Arrange
        var value = "Hello World";

        // Act & Assert - normalizer names should be case insensitive
        Assert.Equal("hello world", NormalizerFactory.Normalize(value, "lowercase"));
        Assert.Equal("hello world", NormalizerFactory.Normalize(value, "LOWERCASE"));
        Assert.Equal("hello world", NormalizerFactory.Normalize(value, "Lowercase"));
    }

    [Fact]
    public void Normalize_CustomNormalizer_MultipleFiltersAppliedInOrder()
    {
        // Arrange
        var value = "  Café  ";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "complex_normalizer",
                TokenFilters = new List<string> { "trim", "lowercase", "asciifolding" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "complex_normalizer", customNormalizers);

        // Assert
        Assert.Equal("cafe", result);
    }

    [Fact]
    public void Normalize_EmptyString_ShouldReturnEmpty()
    {
        // Act
        var result = NormalizerFactory.Normalize("", "lowercase");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TokenFilter_ArabicNormalization_ShouldNormalizeAlefVariants()
    {
        // Arrange - Alef with hamza above (U+0623) should become bare alef (U+0627)
        var value = "\u0623\u0647\u0644\u0627";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "arabic_norm",
                TokenFilters = new List<string> { "arabic_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "arabic_norm", customNormalizers);

        // Assert - alef hamza should be normalized to bare alef
        Assert.Equal("\u0627\u0647\u0644\u0627", result);
    }

    [Fact]
    public void TokenFilter_ArabicNormalization_ShouldRemoveTatweel()
    {
        // Arrange - word with tatweel/kashida (U+0640) between characters
        var value = "ab\u0640cd";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "arabic_norm",
                TokenFilters = new List<string> { "arabic_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "arabic_norm", customNormalizers);

        // Assert - tatweel should be removed
        Assert.Equal("abcd", result);
    }

    [Fact]
    public void TokenFilter_CjkWidth_ShouldNormalizeFullwidthToHalfwidth()
    {
        // Arrange - fullwidth "ABC123"
        var value = "\uFF21\uFF22\uFF23\uFF11\uFF12\uFF13";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "cjk_norm",
                TokenFilters = new List<string> { "cjk_width" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "cjk_norm", customNormalizers);

        // Assert
        Assert.Equal("ABC123", result);
    }

    [Fact]
    public void TokenFilter_CjkWidth_ShouldNormalizeFullwidthSpace()
    {
        // Arrange - fullwidth space (U+3000)
        var value = "Hello\u3000World";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "cjk_norm",
                TokenFilters = new List<string> { "cjk_width" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "cjk_norm", customNormalizers);

        // Assert
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void TokenFilter_GermanNormalization_ShouldNormalizeUmlauts()
    {
        // Arrange
        var value = "Stra\u00dfe \u00fcber Br\u00fccke M\u00fcnchen";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "german_norm",
                TokenFilters = new List<string> { "german_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "german_norm", customNormalizers);

        // Assert
        Assert.Equal("Strasse uber Brucke Munchen", result);
    }

    [Fact]
    public void TokenFilter_GermanNormalization_ShouldHandleAllUmlauts()
    {
        // Arrange
        var value = "\u00e4\u00f6\u00fc\u00c4\u00d6\u00dc\u00df";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "german_norm",
                TokenFilters = new List<string> { "german_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "german_norm", customNormalizers);

        // Assert
        Assert.Equal("aouAOUss", result);
    }

    [Fact]
    public void TokenFilter_HindiNormalization_ShouldRemoveNukta()
    {
        // Arrange - letter with nukta (U+093C)
        var value = "\u0915\u093C";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "hindi_norm",
                TokenFilters = new List<string> { "hindi_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "hindi_norm", customNormalizers);

        // Assert - nukta should be removed
        Assert.Equal("\u0915", result);
    }

    [Fact]
    public void TokenFilter_IndicNormalization_ShouldRemoveZeroWidthChars()
    {
        // Arrange - text with zero-width joiner and non-joiner
        var value = "Hello\u200DWorld\u200C!";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "indic_norm",
                TokenFilters = new List<string> { "indic_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "indic_norm", customNormalizers);

        // Assert
        Assert.Equal("HelloWorld!", result);
    }

    [Fact]
    public void TokenFilter_PersianNormalization_ShouldNormalizeKehAndYeh()
    {
        // Arrange - Arabic keh (U+0643) and Arabic yeh (U+064A)
        var value = "\u0643\u062A\u0627\u0628 \u0639\u0631\u0628\u064A";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "persian_norm",
                TokenFilters = new List<string> { "persian_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "persian_norm", customNormalizers);

        // Assert - keh should become keheh, yeh should become Persian yeh
        Assert.Contains("\u06A9", result); // Persian keheh
        Assert.Contains("\u06CC", result); // Persian yeh
    }

    [Fact]
    public void TokenFilter_ScandinavianFolding_ShouldFoldCharacters()
    {
        // Arrange
        var value = "\u00e5\u00e4\u00e6\u00f6\u00f8";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "scand_fold",
                TokenFilters = new List<string> { "scandinavian_folding" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "scand_fold", customNormalizers);

        // Assert - all should fold to simple a/o
        Assert.Equal("aaaoo", result);
    }

    [Fact]
    public void TokenFilter_ScandinavianFolding_ShouldHandleUppercase()
    {
        // Arrange
        var value = "\u00c5\u00c4\u00c6\u00d6\u00d8";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "scand_fold",
                TokenFilters = new List<string> { "scandinavian_folding" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "scand_fold", customNormalizers);

        // Assert
        Assert.Equal("AAAOO", result);
    }

    [Fact]
    public void TokenFilter_ScandinavianNormalization_ShouldNormalizeInterchangeable()
    {
        // Arrange - \u00e6 and \u00f8 should normalize to \u00e4 and \u00f6
        var value = "\u00e6\u00f8";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "scand_norm",
                TokenFilters = new List<string> { "scandinavian_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "scand_norm", customNormalizers);

        // Assert
        Assert.Equal("\u00e4\u00f6", result);
    }

    [Fact]
    public void TokenFilter_SoraniNormalization_ShouldNormalizeYehAndKeh()
    {
        // Arrange - Arabic yeh and keh
        var value = "\u064A\u0643";
        var customNormalizers = new List<CustomNormalizer>
        {
            new()
            {
                Name = "sorani_norm",
                TokenFilters = new List<string> { "sorani_normalization" }
            }
        };

        // Act
        var result = NormalizerFactory.Normalize(value, "sorani_norm", customNormalizers);

        // Assert
        Assert.Contains("\u06CC", result); // Farsi yeh
        Assert.Contains("\u06A9", result); // Keheh
    }

    [Fact]
    public void GetSupportedTokenFilters_ShouldIncludeAllFilters()
    {
        // Act
        var filters = NormalizerFactory.GetSupportedTokenFilters();

        // Assert - check all 14 filters
        Assert.Contains("arabic_normalization", filters);
        Assert.Contains("asciifolding", filters);
        Assert.Contains("cjk_width", filters);
        Assert.Contains("elision", filters);
        Assert.Contains("german_normalization", filters);
        Assert.Contains("hindi_normalization", filters);
        Assert.Contains("indic_normalization", filters);
        Assert.Contains("lowercase", filters);
        Assert.Contains("persian_normalization", filters);
        Assert.Contains("scandinavian_folding", filters);
        Assert.Contains("scandinavian_normalization", filters);
        Assert.Contains("sorani_normalization", filters);
        Assert.Contains("trim", filters);
        Assert.Contains("uppercase", filters);
        Assert.Equal(14, filters.Length);
    }
}
