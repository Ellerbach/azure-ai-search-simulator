using System.Text.Json;
using AzureAISearchSimulator.Core.Models;
using Lucene.Net.Search;

namespace AzureAISearchSimulator.Search;

// Azure behaviours the simulator adds around upstream's search pipeline.
internal static class SearchCompat
{
    private const int DefaultPageSize = 50;

    // Azure's default hybridSearch.maxTextRecallSize: how many text matches a hybrid query fuses with its vector neighbours.
    public const int MaxTextRecallSize = 1000;

    public static int CountNotMatching(IndexSearcher searcher, string keyField, Query query, IEnumerable<string> keys) =>
        keys.Count(key => searcher.Search(
            new BooleanQuery { { query, Occur.MUST }, { new TermQuery(new Lucene.Net.Index.Term(keyField, key)), Occur.MUST } }, 1).TotalHits == 0);

    // Without top, Azure returns 50 results and the request for the next 50 (followed by the SDKs' paged iterables).
    public static void AddNextPage(SearchResponse response, SearchRequest request, long available, string indexName)
    {
        var skip = request.Skip ?? 0;
        if (request.Top != null || available <= skip + DefaultPageSize)
        {
            return;
        }

        var next = JsonSerializer.Deserialize<SearchRequest>(JsonSerializer.Serialize(request))!;
        next.Skip = skip + DefaultPageSize;
        response.NextPageParameters = next;
        response.ODataNextLink = $"https://localhost/indexes('{indexName}')/docs/search.post.search";
    }

    // The sortable field a $orderby clause sorts on, or null for search.score().
    public static SearchField? ValidateOrderBy(SearchIndex index, string[] tokens)
    {
        if (tokens.Length is 0 or > 2 || (tokens.Length == 2 && tokens[1] is not ("asc" or "desc")))
        {
            throw new ArgumentException($"Invalid expression: '{string.Join(' ', tokens)}' isn't a valid $orderby clause.\r\nParameter name: $orderby");
        }

        var fieldName = tokens[0];
        if (fieldName == "search.score()")
        {
            return null;
        }

        SearchField? field = null;
        var candidates = index.Fields;
        foreach (var segment in fieldName.Split('/'))
        {
            if (field?.IsCollection == true)
            {
                candidates = null;
            }

            field = candidates?.FirstOrDefault(f => f.Name == segment);
            candidates = field?.Fields;
        }

        if (field is null || field.Sortable != true || field.IsCollection || field.IsComplex)
        {
            throw new ArgumentException($"Invalid expression: '{fieldName}' isn't a sortable field.\r\nParameter name: $orderby");
        }

        return field;
    }

    // Value facets from each matching document's source: exact, case-sensitive values (after the field's normalizer), as Azure counts them.
    public static List<FacetResult> ValueFacet(IndexSearcher searcher, SearchIndex schema, SearchField field, string path, int maxCount, IEnumerable<int> docs)
    {
        var counts = new Dictionary<string, (object Value, long Count)>(StringComparer.Ordinal);
        var sourceOnly = new HashSet<string> { "_raw_json" };
        foreach (var doc in docs)
        {
            var json = searcher.IndexReader.Document(doc, sourceOnly).Get("_raw_json");
            if (json is null)
            {
                continue;
            }

            using var source = JsonDocument.Parse(json);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in Values(source.RootElement, path.Split('/'), 0))
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

        return counts.Values
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Value is string s ? s : Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .Take(maxCount)
            .Select(c => new FacetResult { Value = c.Value, Count = c.Count })
            .ToList();
    }

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

    private static (string Key, object Value)? FacetValue(SearchIndex schema, SearchField field, JsonElement value)
    {
        var type = field.Type.StartsWith("Collection(", StringComparison.Ordinal) ? field.Type["Collection(".Length..^1] : field.Type;
        return value.ValueKind switch
        {
            JsonValueKind.String when type == SearchFieldDataType.DateTimeOffset
                && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
                => ("d" + DocumentCompat.FormatDate(date), DocumentCompat.FormatDate(date)),
            JsonValueKind.String when NormalizerFactory.Normalize(value.GetString(), field.Normalizer, schema.Normalizers, schema.CharFilters) is { } text
                => ("s" + text, text),
            JsonValueKind.True or JsonValueKind.False => ("b" + value.GetBoolean(), value.GetBoolean()),
            JsonValueKind.Number when value.TryGetInt64(out var whole) => ("n" + whole, whole),
            JsonValueKind.Number => ("n" + value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.GetDouble()),
            _ => null,
        };
    }

    // Exact k nearest neighbours among the documents that pass the filter, scored like the simulator's HNSW (1 / (1 + distance)).
    public static Dictionary<string, double> PreFilteredVectorSearch(
        IndexSearcher searcher,
        SearchIndex index,
        string keyField,
        Query filterQuery,
        List<VectorQuery> queries)
    {
        var sourceAndKey = new HashSet<string> { "_raw_json", keyField };
        var candidates = new List<(string Key, JsonElement Source)>();
        foreach (var hit in searcher.Search(filterQuery, Math.Max(1, searcher.IndexReader.MaxDoc)).ScoreDocs)
        {
            var doc = searcher.IndexReader.Document(hit.Doc, sourceAndKey);
            if (doc.Get(keyField) is { } key && doc.Get("_raw_json") is { } json)
            {
                using var source = JsonDocument.Parse(json);
                candidates.Add((key, source.RootElement.Clone()));
            }
        }

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var query in queries.Where(q => q.Vector is { Length: > 0 }))
        {
            foreach (var field in (query.Fields ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var metric = MetricFor(index, field);
                var nearest = candidates
                    .Select(c => (c.Key, Vector: c.Source.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null))
                    .Where(c => c.Vector is not null && c.Vector.Length == query.Vector!.Length)
                    .Select(c => (c.Key, Score: 1.0 / (1.0 + Distance(metric, query.Vector!, c.Vector!))))
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.Key, StringComparer.Ordinal)
                    .Take(query.K > 0 ? query.K : 50);
                foreach (var (key, score) in nearest)
                {
                    result[key] = result.TryGetValue(key, out var existing) ? Math.Max(existing, score) : score;
                }
            }
        }

        return result;
    }

    private static string MetricFor(SearchIndex index, string fieldName)
    {
        var profile = index.Fields.FirstOrDefault(f => f.Name == fieldName)?.VectorSearchProfile;
        var algorithm = index.VectorSearch?.Profiles?.FirstOrDefault(p => p.Name == profile)?.Algorithm;
        return index.VectorSearch?.Algorithms?.FirstOrDefault(a => a.Name == algorithm)?.HnswParameters?.Metric ?? "cosine";
    }

    private static double Distance(string metric, float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0, l2 = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
            l2 += (a[i] - b[i]) * (a[i] - b[i]);
        }

        return metric.ToLowerInvariant() switch
        {
            "euclidean" => Math.Sqrt(l2),
            "dotproduct" => 1 - dot,
            _ => aa == 0 || bb == 0 ? 1 : 1 - dot / Math.Sqrt(aa * bb),
        };
    }
}
