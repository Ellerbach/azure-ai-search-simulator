using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ar;
using Lucene.Net.Analysis.Bg;
using Lucene.Net.Analysis.Br;
using Lucene.Net.Analysis.Ca;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Cz;
using Lucene.Net.Analysis.Da;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.El;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Es;
using Lucene.Net.Analysis.Eu;
using Lucene.Net.Analysis.Fa;
using Lucene.Net.Analysis.Fi;
using Lucene.Net.Analysis.Fr;
using Lucene.Net.Analysis.Ga;
using Lucene.Net.Analysis.Gl;
using Lucene.Net.Analysis.Hi;
using Lucene.Net.Analysis.Hu;
using Lucene.Net.Analysis.Hy;
using Lucene.Net.Analysis.Id;
using Lucene.Net.Analysis.It;
using Lucene.Net.Analysis.Lv;
using Lucene.Net.Analysis.Nl;
using Lucene.Net.Analysis.No;
using Lucene.Net.Analysis.Pt;
using Lucene.Net.Analysis.Ro;
using Lucene.Net.Analysis.Ru;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Sv;
using Lucene.Net.Analysis.Tr;
using Lucene.Net.Util;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Factory for creating Lucene analyzers based on Azure AI Search analyzer names.
/// Supports all language analyzers available in Lucene.NET Analysis.Common.
/// </summary>
public static class AnalyzerFactory
{
    /// <summary>
    /// Lucene version used by all analyzers.
    /// </summary>
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Creates an analyzer based on the Azure AI Search analyzer name.
    /// Both ".lucene" and ".microsoft" suffixed names are supported;
    /// ".microsoft" variants are mapped to the closest Lucene equivalent.
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

            // Pattern analyzer (simplified - uses standard tokenization)
            "pattern" => new StandardAnalyzer(Version),

            // Arabic
            "ar.lucene" or "ar.microsoft" or "arabic" => new ArabicAnalyzer(Version),

            // Armenian
            "hy.microsoft" or "armenian" => new ArmenianAnalyzer(Version),

            // Basque
            "eu.lucene" or "eu.microsoft" or "basque" => new BasqueAnalyzer(Version),

            // Brazilian Portuguese
            "pt-br.lucene" or "pt-br.microsoft" or "brazilian" => new BrazilianAnalyzer(Version),

            // Bulgarian
            "bg.lucene" or "bg.microsoft" or "bulgarian" => new BulgarianAnalyzer(Version),

            // Catalan
            "ca.lucene" or "ca.microsoft" or "catalan" => new CatalanAnalyzer(Version),

            // Chinese / CJK
            "zh-hans.lucene" or "zh-hans.microsoft" or "zh-hant.lucene" or "zh-hant.microsoft"
                or "ja.lucene" or "ja.microsoft" or "ko.lucene" or "ko.microsoft"
                or "cjk" => new CJKAnalyzer(Version),

            // Czech
            "cs.lucene" or "cs.microsoft" or "czech" => new CzechAnalyzer(Version),

            // Danish
            "da.lucene" or "da.microsoft" or "danish" => new DanishAnalyzer(Version),

            // Dutch
            "nl.lucene" or "nl.microsoft" or "dutch" => new DutchAnalyzer(Version),

            // English
            "en.lucene" or "en.microsoft" or "english" => new EnglishAnalyzer(Version),

            // Finnish
            "fi.lucene" or "fi.microsoft" or "finnish" => new FinnishAnalyzer(Version),

            // French
            "fr.lucene" or "fr.microsoft" or "french" => new FrenchAnalyzer(Version),

            // Galician
            "gl.lucene" or "gl.microsoft" or "galician" => new GalicianAnalyzer(Version),

            // German
            "de.lucene" or "de.microsoft" or "german" => new GermanAnalyzer(Version),

            // Greek
            "el.lucene" or "el.microsoft" or "greek" => new GreekAnalyzer(Version),

            // Hindi
            "hi.lucene" or "hi.microsoft" or "hindi" => new HindiAnalyzer(Version),

            // Hungarian
            "hu.lucene" or "hu.microsoft" or "hungarian" => new HungarianAnalyzer(Version),

            // Indonesian
            "id.lucene" or "id.microsoft" or "indonesian" => new IndonesianAnalyzer(Version),

            // Irish
            "ga.lucene" or "ga.microsoft" or "irish" => new IrishAnalyzer(Version),

            // Italian
            "it.lucene" or "it.microsoft" or "italian" => new ItalianAnalyzer(Version),

            // Latvian
            "lv.lucene" or "lv.microsoft" or "latvian" => new LatvianAnalyzer(Version),

            // Norwegian
            "no.lucene" or "nb.microsoft" or "norwegian" => new NorwegianAnalyzer(Version),

            // Persian
            "fa.lucene" or "fa.microsoft" or "persian" => new PersianAnalyzer(Version),

            // Portuguese
            "pt-pt.lucene" or "pt-pt.microsoft" or "portuguese" => new PortugueseAnalyzer(Version),

            // Romanian
            "ro.lucene" or "ro.microsoft" or "romanian" => new RomanianAnalyzer(Version),

            // Russian
            "ru.lucene" or "ru.microsoft" or "russian" => new RussianAnalyzer(Version),

            // Spanish
            "es.lucene" or "es.microsoft" or "spanish" => new SpanishAnalyzer(Version),

            // Swedish
            "sv.lucene" or "sv.microsoft" or "swedish" => new SwedishAnalyzer(Version),

            // Turkish
            "tr.lucene" or "tr.microsoft" or "turkish" => new TurkishAnalyzer(Version),

            // Languages without a dedicated Lucene analyzer — fall back to Standard
            "th.lucene" or "th.microsoft"   // Thai
                or "bn.microsoft"   // Bengali
                or "hr.microsoft"   // Croatian
                or "et.microsoft"   // Estonian
                or "gu.microsoft"   // Gujarati
                or "he.microsoft"   // Hebrew
                or "is.microsoft"   // Icelandic
                or "kn.microsoft"   // Kannada
                or "lt.microsoft"   // Lithuanian
                or "ml.microsoft"   // Malayalam
                or "ms.microsoft"   // Malay
                or "mr.microsoft"   // Marathi
                or "pl.microsoft" or "pl.lucene"   // Polish
                or "pa.microsoft"   // Punjabi
                or "sr-cyrillic.microsoft" or "sr-latin.microsoft"   // Serbian
                or "sk.microsoft"   // Slovak
                or "sl.microsoft"   // Slovenian
                or "ta.microsoft"   // Tamil
                or "te.microsoft"   // Telugu
                or "uk.microsoft"   // Ukrainian
                or "ur.microsoft"   // Urdu
                or "vi.microsoft"   // Vietnamese
                => new StandardAnalyzer(Version),

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

            // Language analyzers (Lucene-backed)
            "ar.lucene", "ar.microsoft",
            "hy.microsoft",
            "eu.lucene", "eu.microsoft",
            "pt-br.lucene", "pt-br.microsoft",
            "bg.lucene", "bg.microsoft",
            "ca.lucene", "ca.microsoft",
            "zh-hans.lucene", "zh-hans.microsoft",
            "zh-hant.lucene", "zh-hant.microsoft",
            "cs.lucene", "cs.microsoft",
            "da.lucene", "da.microsoft",
            "nl.lucene", "nl.microsoft",
            "en.lucene", "en.microsoft",
            "fi.lucene", "fi.microsoft",
            "fr.lucene", "fr.microsoft",
            "gl.lucene", "gl.microsoft",
            "de.lucene", "de.microsoft",
            "el.lucene", "el.microsoft",
            "hi.lucene", "hi.microsoft",
            "hu.lucene", "hu.microsoft",
            "id.lucene", "id.microsoft",
            "ga.lucene", "ga.microsoft",
            "it.lucene", "it.microsoft",
            "ja.lucene", "ja.microsoft",
            "ko.lucene", "ko.microsoft",
            "lv.lucene", "lv.microsoft",
            "no.lucene", "nb.microsoft",
            "fa.lucene", "fa.microsoft",
            "pt-pt.lucene", "pt-pt.microsoft",
            "ro.lucene", "ro.microsoft",
            "ru.lucene", "ru.microsoft",
            "es.lucene", "es.microsoft",
            "sv.lucene", "sv.microsoft",
            "th.lucene", "th.microsoft",
            "tr.lucene", "tr.microsoft",

            // Microsoft-only languages (StandardAnalyzer fallback)
            "bn.microsoft",
            "hr.microsoft",
            "et.microsoft",
            "gu.microsoft",
            "he.microsoft",
            "is.microsoft",
            "kn.microsoft",
            "lt.microsoft",
            "ml.microsoft",
            "ms.microsoft",
            "mr.microsoft",
            "pl.microsoft", "pl.lucene",
            "pa.microsoft",
            "sr-cyrillic.microsoft", "sr-latin.microsoft",
            "sk.microsoft",
            "sl.microsoft",
            "ta.microsoft",
            "te.microsoft",
            "uk.microsoft",
            "ur.microsoft",
            "vi.microsoft"
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

        // All names handled in Create() are supported
        var supported = new HashSet<string>(GetSupportedAnalyzers(), StringComparer.OrdinalIgnoreCase);

        // Also accept plain language names and other aliases
        supported.Add("standard");
        supported.Add("simple");
        supported.Add("whitespace");
        supported.Add("keyword");
        supported.Add("stop");
        supported.Add("pattern");
        supported.Add("arabic");
        supported.Add("armenian");
        supported.Add("basque");
        supported.Add("brazilian");
        supported.Add("bulgarian");
        supported.Add("catalan");
        supported.Add("cjk");
        supported.Add("czech");
        supported.Add("danish");
        supported.Add("dutch");
        supported.Add("english");
        supported.Add("finnish");
        supported.Add("french");
        supported.Add("galician");
        supported.Add("german");
        supported.Add("greek");
        supported.Add("hindi");
        supported.Add("hungarian");
        supported.Add("indonesian");
        supported.Add("irish");
        supported.Add("italian");
        supported.Add("latvian");
        supported.Add("norwegian");
        supported.Add("persian");
        supported.Add("portuguese");
        supported.Add("romanian");
        supported.Add("russian");
        supported.Add("spanish");
        supported.Add("swedish");
        supported.Add("thai");
        supported.Add("turkish");

        return supported.Contains(analyzerName);
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
