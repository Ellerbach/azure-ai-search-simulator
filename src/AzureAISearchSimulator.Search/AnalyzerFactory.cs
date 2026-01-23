using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Fr;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Factory for creating Lucene analyzers based on Azure AI Search analyzer names.
/// </summary>
public static class AnalyzerFactory
{
    /// <summary>
    /// Lucene version used by all analyzers.
    /// </summary>
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Creates an analyzer based on the Azure AI Search analyzer name.
    /// </summary>
    /// <param name="analyzerName">The name of the analyzer (e.g., "standard.lucene", "en.microsoft").</param>
    /// <returns>A Lucene analyzer instance.</returns>
    public static Analyzer Create(string? analyzerName)
    {
        if (string.IsNullOrEmpty(analyzerName))
        {
            return new StandardAnalyzer(Version);
        }

        // Normalize analyzer name
        var normalizedName = analyzerName.ToLowerInvariant();

        return normalizedName switch
        {
            // Standard analyzers
            "standard" or "standard.lucene" => new StandardAnalyzer(Version),
            "simple" or "simple.lucene" => new SimpleAnalyzer(Version),
            "whitespace" or "whitespace.lucene" => new WhitespaceAnalyzer(Version),
            "keyword" or "keyword.lucene" => new KeywordAnalyzer(),
            "stop" or "stop.lucene" => new StopAnalyzer(Version),

            // Language analyzers - Microsoft style names
            "en.microsoft" or "en.lucene" or "english" => new EnglishAnalyzer(Version),
            "fr.microsoft" or "fr.lucene" or "french" => new FrenchAnalyzer(Version),
            "de.microsoft" or "de.lucene" or "german" => new GermanAnalyzer(Version),

            // Language analyzers - Lucene names
            "english.lucene" => new EnglishAnalyzer(Version),
            "french.lucene" => new FrenchAnalyzer(Version),
            "german.lucene" => new GermanAnalyzer(Version),

            // Pattern analyzer (simplified - uses standard tokenization)
            "pattern" => new StandardAnalyzer(Version),

            // Default to standard analyzer for unknown names
            _ => new StandardAnalyzer(Version)
        };
    }

    /// <summary>
    /// Gets a list of supported analyzer names.
    /// </summary>
    /// <returns>Array of supported analyzer names.</returns>
    public static string[] GetSupportedAnalyzers()
    {
        return new[]
        {
            // Standard analyzers
            "standard.lucene",
            "simple.lucene",
            "whitespace.lucene",
            "keyword.lucene",
            "stop.lucene",

            // English
            "en.microsoft",
            "en.lucene",

            // French
            "fr.microsoft",
            "fr.lucene",

            // German
            "de.microsoft",
            "de.lucene"
        };
    }

    /// <summary>
    /// Checks if an analyzer name is supported.
    /// </summary>
    /// <param name="analyzerName">The analyzer name to check.</param>
    /// <returns>True if the analyzer is supported.</returns>
    public static bool IsSupported(string analyzerName)
    {
        if (string.IsNullOrEmpty(analyzerName))
        {
            return true; // Default analyzer is always supported
        }

        var normalizedName = analyzerName.ToLowerInvariant();
        
        return normalizedName switch
        {
            "standard" or "standard.lucene" => true,
            "simple" or "simple.lucene" => true,
            "whitespace" or "whitespace.lucene" => true,
            "keyword" or "keyword.lucene" => true,
            "stop" or "stop.lucene" => true,
            "en.microsoft" or "en.lucene" or "english" or "english.lucene" => true,
            "fr.microsoft" or "fr.lucene" or "french" or "french.lucene" => true,
            "de.microsoft" or "de.lucene" or "german" or "german.lucene" => true,
            "pattern" => true,
            _ => false
        };
    }

    /// <summary>
    /// Creates an analyzer for a specific field based on its configuration.
    /// </summary>
    /// <param name="searchAnalyzer">The search-time analyzer name.</param>
    /// <param name="indexAnalyzer">The index-time analyzer name.</param>
    /// <param name="analyzer">The default analyzer name.</param>
    /// <param name="forSearch">True if creating for search, false for indexing.</param>
    /// <returns>A Lucene analyzer instance.</returns>
    public static Analyzer CreateForField(string? searchAnalyzer, string? indexAnalyzer, string? analyzer, bool forSearch)
    {
        // Priority: specific analyzer > default analyzer > standard
        var analyzerName = forSearch
            ? (searchAnalyzer ?? analyzer)
            : (indexAnalyzer ?? analyzer);

        return Create(analyzerName);
    }
}
