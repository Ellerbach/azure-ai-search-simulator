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

    // One search per thousand keys (Lucene's clause limit), not one per key: the query may carry an expensive filter.
    public static int CountNotMatching(IndexSearcher searcher, string keyField, Query query, IReadOnlyCollection<string> keys)
    {
        var matching = 0;
        foreach (var chunk in keys.Chunk(1000))
        {
            var anyKey = new BooleanQuery();
            foreach (var key in chunk)
            {
                anyKey.Add(new TermQuery(new Lucene.Net.Index.Term(keyField, key)), Occur.SHOULD);
            }

            matching += searcher.Search(new BooleanQuery { { query, Occur.MUST }, { anyKey, Occur.MUST } }, 1).TotalHits;
        }

        return keys.Count - matching;
    }

    // Without top, Azure returns 50 results and the request for the next 50 (followed by the SDKs' paged iterables);
    // the API adds @odata.nextLink, which depends on the HTTP request.
    public static void AddNextPage(SearchResponse response, SearchRequest request, long available)
    {
        var skip = request.Skip ?? 0;
        if (request.Top != null || available <= skip + DefaultPageSize)
        {
            return;
        }

        var next = JsonSerializer.Deserialize<SearchRequest>(JsonSerializer.Serialize(request))!;
        next.Skip = skip + DefaultPageSize;
        response.NextPageParameters = next;
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
            .Take(maxCount == 0 ? int.MaxValue : maxCount)
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
            JsonValueKind.Number when type is not (SearchFieldDataType.Double or "Edm.Single" or "Edm.Half") && value.TryGetInt64(out var whole)
                => ("n" + whole, whole),
            JsonValueKind.Number => ("n" + value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.GetDouble()),
            _ => null,
        };
    }

    // Azure rejects vector queries without a vector or fields, on fields that aren't vector fields, vectors of the wrong length and unknown filter modes.
    public static void ValidateVectorQueries(SearchIndex index, SearchRequest request)
    {
        if (request.VectorFilterMode is not (null or "preFilter" or "postFilter" or "strictPostFilter"))
        {
            throw new ArgumentException($"Invalid vectorFilterMode '{request.VectorFilterMode}'. Use 'preFilter', 'postFilter' or 'strictPostFilter'.");
        }

        foreach (var query in request.VectorQueries!)
        {
            if (query.Kind != "vector")
            {
                throw new ArgumentException($"Vector queries of kind '{query.Kind}' aren't supported; only 'vector', with the vector given.");
            }

            if (query.Vector is not { Length: > 0 } || !VectorFields(query).Any())
            {
                throw new ArgumentException("A vector query needs a 'vector' and the 'fields' to search.");
            }

            foreach (var name in VectorFields(query))
            {
                var field = index.Fields.FirstOrDefault(f => f.Name == name)
                    ?? throw new ArgumentException($"Unknown field '{name}' in vector field list.");
                if (!field.IsVector || field.Dimensions is not > 0)
                {
                    throw new ArgumentException($"The field '{name}' in the vector field list is not a vector field.");
                }

                if (query.Vector!.Length != field.Dimensions)
                {
                    throw new ArgumentException(
                        $"The vector field '{name}', with dimension of '{field.Dimensions}', expects a length of '{field.Dimensions}'. However, the provided vector has a length of '{query.Vector.Length}'.");
                }
            }
        }
    }

    // Whether an unfiltered vector query must be searched exactly: only cosine fields can use the approximate search, and only when it is cosine too.
    public static bool NeedsExactSearch(SearchIndex index, VectorQuery query, string? approximateMetric) =>
        query.Exhaustive == true
        || !string.Equals(approximateMetric ?? "cosine", "cosine", StringComparison.OrdinalIgnoreCase)
        || VectorFields(query).Select(field => AlgorithmFor(index, field)).Any(a => a?.Kind == "exhaustiveKnn" || MetricOf(a) != "cosine");

    // Exact k nearest neighbours, over all documents or, with a filter, by its mode. preFilter (Azure's default) takes
    // them among the documents that pass the filter; postFilter and strictPostFilter (the same on one shard) take them
    // among all documents, then drop those that don't pass.
    public static Dictionary<string, double> ExactVectorSearch(
        IndexSearcher searcher,
        SearchIndex index,
        string keyField,
        Query? filterQuery,
        string? filterMode,
        List<VectorQuery> queries)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (queries.Count == 0)
        {
            return result;
        }

        var preFilter = filterMode is null or "preFilter";
        var sourceAndKey = new HashSet<string> { "_raw_json", keyField };
        var candidates = new List<(string Key, JsonElement Source)>();
        foreach (var hit in searcher.Search(preFilter ? filterQuery ?? new MatchAllDocsQuery() : new MatchAllDocsQuery(), Math.Max(1, searcher.IndexReader.MaxDoc)).ScoreDocs)
        {
            var doc = searcher.IndexReader.Document(hit.Doc, sourceAndKey);
            if (doc.Get(keyField) is { } key && doc.Get("_raw_json") is { } json)
            {
                using var source = JsonDocument.Parse(json);
                candidates.Add((key, source.RootElement.Clone()));
            }
        }

        var passing = preFilter || filterQuery is null
            ? null
            : searcher.Search(filterQuery, Math.Max(1, searcher.IndexReader.MaxDoc)).ScoreDocs
                .Select(hit => searcher.IndexReader.Document(hit.Doc, sourceAndKey).Get(keyField))
                .ToHashSet(StringComparer.Ordinal);

        foreach (var query in queries)
        {
            foreach (var field in VectorFields(query))
            {
                var metric = MetricOf(AlgorithmFor(index, field));
                var nearest = candidates
                    .Select(c => (c.Key, Vector: c.Source.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : null))
                    .Where(c => c.Vector is not null && c.Vector.Length == query.Vector!.Length)
                    .Select(c => (c.Key, Measure: Measure(metric, query.Vector!, c.Vector!)))
                    .OrderBy(c => c.Measure.Distance)
                    .ThenBy(c => c.Key, StringComparer.Ordinal)
                    .Take(query.K > 0 ? query.K : 50)
                    .Where(c => passing is null || passing.Contains(c.Key));
                foreach (var (key, (_, score)) in nearest)
                {
                    result[key] = result.TryGetValue(key, out var existing) ? Math.Max(existing, score) : score;
                }
            }
        }

        return result;
    }

    public static IEnumerable<string> VectorFields(VectorQuery query) =>
        (query.Fields ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static VectorSearchAlgorithm? AlgorithmFor(SearchIndex index, string fieldName)
    {
        var profile = index.Fields.FirstOrDefault(f => f.Name == fieldName)?.VectorSearchProfile;
        var algorithm = index.VectorSearch?.Profiles?.FirstOrDefault(p => p.Name == profile)?.Algorithm;
        return index.VectorSearch?.Algorithms?.FirstOrDefault(a => a.Name == algorithm);
    }

    private static string MetricOf(VectorSearchAlgorithm? definition) =>
        (definition?.Kind == "exhaustiveKnn" ? definition.ExhaustiveKnnParameters?.Metric : definition?.HnswParameters?.Metric) ?? "cosine";

    // Ranked by distance (lower is nearer); scored 1 / (1 + distance) like the simulator's HNSW search.
    private static (double Distance, double Score) Measure(string metric, float[] a, float[] b)
    {
        double dot = 0, aa = 0, bb = 0, l2 = 0;
        for (var i = 0; i < a.Length; i++)
        {
            // In double, so large components don't overflow float.
            double x = a[i], y = b[i];
            dot += x * y;
            aa += x * x;
            bb += y * y;
            l2 += (x - y) * (x - y);
        }

        switch (metric.ToLowerInvariant())
        {
            case "euclidean":
                return (Math.Sqrt(l2), 1 / (1 + Math.Sqrt(l2)));
            case "dotproduct":
                // The dot product is unbounded, so 1 / (2 - dot) (the cosine score on unit vectors) only applies up to 1.
                return (-dot, dot <= 1 ? 1 / (2 - dot) : dot);
            default:
                var cosineDistance = aa == 0 || bb == 0 ? 1 : 1 - dot / Math.Sqrt(aa * bb);
                return (cosineDistance, 1 / (1 + cosineDistance));
        }
    }
}
