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
}
