using System.Globalization;
using System.Text;
using System.Text.Json;
using AzureAISearchSimulator.Core.Models;
using Lucene.Net.Search;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Facets as Azure AI Search computes them: value, interval, range (<c>values</c>) and metric facets, read from each
/// matching document's source so that fields, complex sub-fields and collections all facet, and validated as Azure
/// validates them.
/// </summary>
internal static class FacetCompat
{
    private static readonly string[] KnownParameters =
        ["count", "sort", "interval", "values", "metric", "default", "precisionThreshold", "timeoffset", "includeTermFilter", "excludeTermFilter"];

    private static readonly string[] DateUnits = ["year", "quarter", "month", "week", "day", "hour", "minute"];

    private static readonly System.Text.RegularExpressions.Regex TimeOffset = new(
        @"\A(?<sign>[+-])(?<hours>\d{2})(:?(?<minutes>\d{2}))?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Computes the facets of a search.
    /// </summary>
    /// <param name="searcher">The index searcher.</param>
    /// <param name="schema">The index definition.</param>
    /// <param name="expressions">The facet expressions, e.g. <c>category,count:5</c> or <c>rating,interval:1</c>.</param>
    /// <param name="docs">The matching documents.</param>
    /// <param name="resolve">Resolves a field path to its field (or <see langword="null"/>) and canonical path.</param>
    /// <returns>The buckets by field path; expressions on the same field share its list.</returns>
    /// <exception cref="ArgumentException">An expression is invalid; Azure returns 400.</exception>
    public static Dictionary<string, List<FacetResult>> Calculate(
        IndexSearcher searcher,
        SearchIndex schema,
        IEnumerable<string> expressions,
        IReadOnlyCollection<int> docs,
        Func<string, (SearchField? Field, string Path)> resolve)
    {
        var facets = new Dictionary<string, List<FacetResult>>();
        var faceted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            var (name, parameters) = Parse(expression);
            if (parameters.ContainsKey("includeTermFilter") || parameters.ContainsKey("excludeTermFilter"))
            {
                throw Error($"Regular expression facet filtering (includeTermFilter, excludeTermFilter) isn't supported by the simulator: '{expression}'.");
            }

            var (field, path) = resolve(name);
            if (field is null)
            {
                throw Error($"Invalid facet expression: '{name}' isn't a facetable field.");
            }

            if (field.Facetable != true)
            {
                throw Error($"The field '{field.Name}' has not been marked as facetable in the schema.");
            }

            var type = field.IsCollection ? field.Type["Collection(".Length..^1] : field.Type;
            var numeric = type is SearchFieldDataType.Int32 or SearchFieldDataType.Int64 or SearchFieldDataType.Double;
            var date = type == SearchFieldDataType.DateTimeOffset;
            parameters.TryGetValue("metric", out var metric);
            parameters.TryGetValue("interval", out var interval);
            parameters.TryGetValue("values", out var values);
            var counted = parameters.ContainsKey("count") || parameters.ContainsKey("sort");
            if (metric is not null && (counted || interval is not null || values is not null))
            {
                throw Error($"Error in facet specification for field '{name}': The 'metric' faceting option cannot be used with other faceting options in the same specification.");
            }

            if (counted && (interval is not null || values is not null))
            {
                throw Error($"Error in facet specification for field '{name}': The 'sort' and 'count' faceting options cannot be used with the 'interval' or 'values' options.");
            }

            if (interval is not null && values is not null)
            {
                throw Error($"Error in facet specification for field '{name}': Only one of the 'interval' and 'values' faceting options can be used for each field.");
            }

            var offset = TimeSpan.Zero;
            if (parameters.TryGetValue("timeoffset", out var offsetText))
            {
                if (interval is null)
                {
                    throw Error($"Error in facet specification for field '{name}': The 'timeoffset' faceting option must only be used with the 'interval' option.");
                }

                var match = TimeOffset.Match(offsetText);
                if (!match.Success)
                {
                    throw Error($"Invalid timeoffset value '{offsetText}' for field '{name}'. The timeoffset value must given in the form [+-]hh:mm, [+-]hhmm, or [+-]hh.");
                }

                offset = new TimeSpan(int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture), match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0, 0);
                offset = match.Groups["sign"].Value == "-" ? -offset : offset;
            }

            if (parameters.TryGetValue("precisionThreshold", out var threshold) && metric is null)
            {
                throw Error($"Error in facet specification for field '{name}': The 'precisionThreshold' faceting option must only be used when the 'metric: cardinality' option is specified.");
            }

            List<FacetResult> results;
            if (metric is not null)
            {
                if (metric is not ("sum" or "min" or "max" or "avg" or "cardinality"))
                {
                    throw Error($"Invalid metric parameter in facet for '{name}'.");
                }

                if (metric != "cardinality" && !numeric)
                {
                    throw Error($"The metric parameter '{metric}' is only supported in numeric fields.");
                }

                if (threshold is not null && metric != "cardinality")
                {
                    throw Error($"Error in facet specification for field '{name}': The 'precisionThreshold' faceting option must only be used when the 'metric: cardinality' option is specified.");
                }

                // The simulator counts distinct values exactly, so the threshold is only validated.
                if (threshold is not null && !(int.TryParse(threshold, NumberStyles.Integer, CultureInfo.InvariantCulture, out var precision) && precision is >= 0 and <= 40000))
                {
                    throw Error($"Invalid 'precisionThreshold' parameter in facet for '{name}'. The value must be an integer between 0 and 40000.");
                }

                var fallback = parameters.TryGetValue("default", out var text)
                    && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null;
                results = Metric(searcher, schema, field, path, metric, fallback, docs) is { } result ? [result] : [];
            }
            else if (interval is not null)
            {
                if (!numeric && !date)
                {
                    throw Error($"Intervals are not supported in facets for '{name}'. They are only supported on integer and dateTime fields.");
                }

                if (date && !DateUnits.Contains(interval))
                {
                    throw Error($"The value '{interval}' in facet specification for field '{name}' is incorrect. Please use one of the following values for date histogram intervals: {string.Join(", ", DateUnits)}.");
                }

                var size = 0L;
                if (numeric && !(long.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) && size > 0))
                {
                    throw Error($"The value '{interval}' in facet specification for field '{name}' is not a valid integer greater than zero.");
                }

                results = Interval(SourceValues(searcher, path, docs), date ? interval : null, size, offset);
            }
            else if (values is not null)
            {
                if (!numeric && !date)
                {
                    throw Error($"Invalid facet parameter for field '{name}'. Values can only be specified in facets for fields of type Edm.Int32, Edm.Int64, Edm.Double, or Edm.DateTimeOffset.");
                }

                var bounds = values.Split('|').Select(b => b.Trim()).ToList();
                if (bounds.FirstOrDefault(b => !IsBound(type, b)) is { } invalid)
                {
                    throw Error($"The value '{invalid}' in facet specification for field '{name}' is not a valid '{type}' value.");
                }

                results = date
                    ? Range(SourceValues(searcher, path, docs), bounds.Select(ParseDate).ToList(), ReadDate, d => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+0000'", CultureInfo.InvariantCulture))
                    : Range(SourceValues(searcher, path, docs), bounds.Select(b => double.Parse(b, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList(), ReadNumber, n => DocumentCompat.DoubleJson(n));
            }
            else
            {
                var count = 10;
                if (parameters.TryGetValue("count", out var countText)
                    && !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count))
                {
                    throw Error($"Invalid count parameter in facet for '{name}'.");
                }

                var sort = parameters.GetValueOrDefault("sort", "count");
                if (sort is not ("count" or "-count" or "value" or "-value"))
                {
                    throw Error($"Invalid value parameter in facet for '{name}'.");
                }

                results = Value(SourceValues(searcher, path, docs), schema, field, count, sort);
            }

            if (!faceted.Add(metric is null ? path : $"{path}\0{metric}"))
            {
                throw Error(metric is null
                    ? $"Evaluation error: Duplicate facet specifications are specified for field: '{name}'."
                    : $"Evaluation error: Duplicate metric facet specifications are specified for fields: '{name}', type: '{metric}'.");
            }

            if (facets.TryGetValue(path, out var existing))
            {
                existing.AddRange(results);
            }
            else
            {
                facets[path] = results;
            }
        }

        return facets;
    }

    /// <summary>
    /// Splits an expression into its field and its parameters: names are case-sensitive, spaces around names and
    /// values are ignored, a repeated parameter keeps its last value, and commas inside a quoted default don't split.
    /// </summary>
    private static (string Field, Dictionary<string, string> Parameters) Parse(string expression)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '\\' && i + 1 < expression.Length && expression[i + 1] == '\'')
            {
                current.Append('\'');
                i++;
            }
            else if (c == ',' && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                quoted ^= c == '\'';
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        var field = parts[0].Trim();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts.Skip(1))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            var name = (colon < 0 ? part : part[..colon]).Trim();
            if (!KnownParameters.Contains(name))
            {
                throw Error($"Unknown parameter '{name}' in facet specification for field '{field}'.");
            }

            var value = colon < 0 ? string.Empty : part[(colon + 1)..].Trim();
            parameters[name] = value;
        }

        return (field, parameters);
    }

    /// <summary>
    /// Counts a value facet: exact, case-sensitive values (after the field's normalizer), each counted once per
    /// document, sorted by count (the default) or value, either way round.
    /// </summary>
    private static List<FacetResult> Value(IEnumerable<List<JsonElement>> sources, SearchIndex schema, SearchField field, int count, string sort)
    {
        var counts = new Dictionary<string, (object Value, long Count)>(StringComparer.Ordinal);
        foreach (var values in sources)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (FacetValue(schema, field, value) is not { } facetValue || !seen.Add(facetValue.Key))
                {
                    continue;
                }

                counts[facetValue.Key] = counts.TryGetValue(facetValue.Key, out var existing)
                    ? (existing.Value, existing.Count + 1)
                    : (facetValue.Value, 1);
            }
        }

        var buckets = counts.Values.ToList();
        buckets.Sort((a, b) => sort switch
        {
            "value" => CompareValues(a.Value, b.Value),
            "-value" => CompareValues(b.Value, a.Value),
            "-count" => a.Count != b.Count ? a.Count.CompareTo(b.Count) : CompareValues(a.Value, b.Value),
            _ => a.Count != b.Count ? b.Count.CompareTo(a.Count) : CompareValues(a.Value, b.Value),
        });

        return buckets
            .Take(count == 0 ? int.MaxValue : count)
            .Select(c => new FacetResult { Value = c.Value, Count = c.Count })
            .ToList();
    }

    /// <summary>
    /// Counts an interval facet: the non-empty buckets in ascending order, each a value's lower bound (a multiple of the
    /// interval, or the start of its minute, hour, day, Monday-based week, month, quarter or year at the time offset,
    /// returned in UTC), each counted once per document.
    /// </summary>
    private static List<FacetResult> Interval(IEnumerable<List<JsonElement>> sources, string? unit, long size, TimeSpan offset)
    {
        var counts = new SortedDictionary<long, (object Value, long Count)>();
        foreach (var values in sources)
        {
            var seen = new HashSet<long>();
            foreach (var value in values)
            {
                if (Bucket(value, unit, size, offset) is not { } bucket || !seen.Add(bucket.Key))
                {
                    continue;
                }

                counts[bucket.Key] = counts.TryGetValue(bucket.Key, out var existing)
                    ? (existing.Value, existing.Count + 1)
                    : (bucket.Value, 1);
            }
        }

        return counts.Values.Select(c => new FacetResult { Value = c.Value, Count = c.Count }).ToList();
    }

    /// <summary>
    /// A value's interval bucket: its sort key and the lower bound Azure returns.
    /// </summary>
    private static (long Key, object Value)? Bucket(JsonElement value, string? unit, long size, TimeSpan offset)
    {
        if (unit is null)
        {
            if (ReadNumber(value) is not { } number || !double.IsFinite(number))
            {
                return null;
            }

            var lower = (long)Math.Floor(number / size) * size;
            return (lower, lower);
        }

        if (ReadDate(value) is not { } date)
        {
            return null;
        }

        var local = date.ToOffset(offset).DateTime;
        var start = unit switch
        {
            "minute" => new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0),
            "hour" => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
            "day" => local.Date,
            "week" => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)),
            "month" => new DateTime(local.Year, local.Month, 1),
            "quarter" => new DateTime(local.Year, ((local.Month - 1) / 3 * 3) + 1, 1),
            _ => new DateTime(local.Year, 1, 1),
        };
        var bucket = new DateTimeOffset(start, offset);
        return (bucket.UtcTicks, DocumentCompat.FormatDate(bucket));
    }

    /// <summary>
    /// Counts a range facet: below the first bound, from each bound to the next, and from the last bound, empty ranges
    /// included, listed by their lower bound; each document counts once per range.
    /// </summary>
    private static List<FacetResult> Range<T>(IEnumerable<List<JsonElement>> sources, List<T> bounds, Func<JsonElement, T?> read, Func<T, object> output)
        where T : struct, IComparable<T>
    {
        var ranges = new List<(T? From, T? To)> { (null, bounds[0]) };
        ranges.AddRange(bounds.Zip(bounds.Skip(1), (from, to) => ((T?)from, (T?)to)));
        ranges.Add((bounds[^1], null));
        var counts = new long[ranges.Count];
        foreach (var values in sources)
        {
            var found = values.Select(read).OfType<T>().ToList();
            for (var i = 0; i < ranges.Count; i++)
            {
                var (from, to) = ranges[i];
                if (found.Any(v => (from is not { } f || v.CompareTo(f) >= 0) && (to is not { } t || v.CompareTo(t) < 0)))
                {
                    counts[i]++;
                }
            }
        }

        return ranges
            .Select((range, i) => (range.From, range.To, Count: counts[i]))
            .OrderBy(r => r.From.HasValue)
            .ThenBy(r => r.From.GetValueOrDefault())
            .Select(r => new FacetResult
            {
                From = r.From is { } from ? output(from) : null,
                To = r.To is { } to ? output(to) : null,
                Count = r.Count,
            })
            .ToList();
    }

    /// <summary>
    /// Computes a metric facet: <c>sum</c>, <c>min</c>, <c>max</c> or <c>avg</c> over every value (each element of a
    /// collection), a document without one counting as the default if given; or <c>cardinality</c>, the number of
    /// distinct values. Returns <see langword="null"/> when <c>min</c>, <c>max</c> or <c>avg</c> have no value.
    /// </summary>
    private static FacetResult? Metric(IndexSearcher searcher, SearchIndex schema, SearchField field, string path, string metric, double? fallback, IEnumerable<int> docs)
    {
        if (metric == "cardinality")
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in SourceValues(searcher, path, docs).SelectMany(values => values))
            {
                if (FacetValue(schema, field, value) is { } facetValue)
                {
                    distinct.Add(facetValue.Key);
                }
            }

            return new FacetResult { Cardinality = distinct.Count };
        }

        var numbers = new List<double>();
        foreach (var values in SourceValues(searcher, path, docs))
        {
            var found = values.Select(ReadNumber).OfType<double>().ToList();
            numbers.AddRange(found.Count > 0 ? found : fallback is { } value ? [value] : []);
        }

        return metric switch
        {
            "sum" => new FacetResult { Sum = numbers.Sum() },
            _ when numbers.Count == 0 => null,
            "min" => new FacetResult { Min = numbers.Min() },
            "max" => new FacetResult { Max = numbers.Max() },
            _ => new FacetResult { Avg = numbers.Average() },
        };
    }

    /// <summary>
    /// The values at a path in each matching document's source, through complex objects and collections.
    /// </summary>
    private static IEnumerable<List<JsonElement>> SourceValues(IndexSearcher searcher, string path, IEnumerable<int> docs)
    {
        var sourceOnly = new HashSet<string> { "_raw_json" };
        var segments = path.Split('/');
        foreach (var doc in docs)
        {
            if (searcher.IndexReader.Document(doc, sourceOnly).Get("_raw_json") is not { } json)
            {
                continue;
            }

            using var source = JsonDocument.Parse(json);
            yield return Values(source.RootElement, segments, 0).Select(value => value.Clone()).ToList();
        }
    }

    /// <summary>
    /// The non-null values at a path in a document's source, through any arrays on the way.
    /// </summary>
    private static IEnumerable<JsonElement> Values(JsonElement element, string[] path, int depth)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().SelectMany(item => Values(item, path, depth)))
            {
                yield return item;
            }
        }
        else if (depth == path.Length)
        {
            if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                yield return element;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(path[depth], out var child))
        {
            foreach (var item in Values(child, path, depth + 1))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// A facet bucket's key and returned value for one source value, typed by the field, or null if it can't be faceted.
    /// </summary>
    private static (string Key, object Value)? FacetValue(SearchIndex schema, SearchField field, JsonElement value)
    {
        var type = field.IsCollection ? field.Type["Collection(".Length..^1] : field.Type;
        return value.ValueKind switch
        {
            JsonValueKind.String when type == SearchFieldDataType.DateTimeOffset && ReadDate(value) is { } date
                => ("d" + DocumentCompat.FormatDate(date), DocumentCompat.FormatDate(date)),
            JsonValueKind.String when NormalizerFactory.Normalize(value.GetString(), field.Normalizer, schema.Normalizers, schema.CharFilters) is { } text
                => ("s" + text, text),
            JsonValueKind.True or JsonValueKind.False => ("b" + value.GetBoolean(), value.GetBoolean()),
            JsonValueKind.Number when type is not (SearchFieldDataType.Double or "Edm.Single" or "Edm.Half") && value.TryGetInt64(out var whole)
                => ("n" + whole, whole),
            JsonValueKind.Number => ("n" + value.GetDouble().ToString("R", CultureInfo.InvariantCulture), value.GetDouble()),
            _ => null,
        };
    }

    /// <summary>
    /// Orders facet values: strings ordinally, numbers numerically.
    /// </summary>
    private static int CompareValues(object a, object b) => (a, b) switch
    {
        (string x, string y) => string.CompareOrdinal(x, y),
        (bool x, bool y) => x.CompareTo(y),
        _ => Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(b, CultureInfo.InvariantCulture)),
    };

    /// <summary>
    /// A stored number: a JSON number, or the strings Azure stores for NaN and the infinities.
    /// </summary>
    private static double? ReadNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString() switch
        {
            "NaN" => double.NaN,
            "INF" => double.PositiveInfinity,
            "-INF" => double.NegativeInfinity,
            _ => null,
        },
        _ => null,
    };

    private static DateTimeOffset? ReadDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static DateTimeOffset ParseDate(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>
    /// Whether a range bound is a value of the field's type.
    /// </summary>
    private static bool IsBound(string type, string text) => type switch
    {
        SearchFieldDataType.Int32 => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        SearchFieldDataType.Int64 => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        SearchFieldDataType.Double => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        _ => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
    };

    private static ArgumentException Error(string message) => new($"{message}\r\nParameter name: facet");
}
