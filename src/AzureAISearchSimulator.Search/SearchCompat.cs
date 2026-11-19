using System.Text.Json;
using AzureAISearchSimulator.Core.Models;
using Lucene.Net.Search;

namespace AzureAISearchSimulator.Search;

// Azure behaviours the simulator adds around upstream's search pipeline.
internal static class SearchCompat
{
    private const int DefaultPageSize = 50;

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
