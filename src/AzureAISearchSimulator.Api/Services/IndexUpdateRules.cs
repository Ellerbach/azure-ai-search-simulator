using System.Text.Json;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Azure's rules for updating an existing index, with Azure's messages: fields can be added but not deleted or changed
/// (except <c>retrievable</c>, <c>searchAnalyzer</c> and <c>synonymMaps</c>), suggesters only gain new fields, analysis
/// components can't change and are only added with downtime, and vector algorithms in use can't change.
/// </summary>
internal static class IndexUpdateRules
{
    /// <summary>
    /// Refuses an index update that Azure would refuse.
    /// </summary>
    /// <param name="existing">The current index definition.</param>
    /// <param name="updated">The requested index definition.</param>
    /// <param name="allowIndexDowntime">The request's <c>allowIndexDowntime</c> parameter.</param>
    /// <exception cref="InvalidOperationException">Azure would refuse the update.</exception>
    public static void Validate(SearchIndex existing, SearchIndex updated, bool allowIndexDowntime)
    {
        var before = Flatten(existing.Fields, "");
        var after = Flatten(updated.Fields, "");

        var deleted = before.Keys.Where(path => !after.ContainsKey(path)).ToList();
        if (deleted.Count > 0)
        {
            throw Refused($"Existing field(s) {string.Join(", ", deleted.Select(p => $"'{p}'"))} cannot be deleted.");
        }

        foreach (var (path, field) in before)
        {
            if (!SameImmutableAttributes(field, after[path]))
            {
                throw Refused($"Existing field '{path}' cannot be changed.");
            }
        }

        ValidateSuggesters(existing, updated, before);
        ValidateAnalysis(existing, updated, allowIndexDowntime);
        ValidateVectorSearch(existing, updated, before, after);
    }

    /// <summary>
    /// Suggesters can't be removed or lose fields, and can only gain fields added in the same update.
    /// </summary>
    private static void ValidateSuggesters(SearchIndex existing, SearchIndex updated, Dictionary<string, SearchField> before)
    {
        var current = (existing.Suggesters ?? []).ToDictionary(s => s.Name, StringComparer.Ordinal);
        var next = (updated.Suggesters ?? []).ToDictionary(s => s.Name, StringComparer.Ordinal);
        foreach (var suggester in next.Values)
        {
            if (!current.TryGetValue(suggester.Name, out var old))
            {
                var existingFields = suggester.SourceFields.Where(before.ContainsKey).ToList();
                if (existingFields.Count > 0)
                {
                    throw Refused($"Fields that were already present in an index ({string.Join(", ", existingFields)}) cannot be referenced by a new suggester. Only new fields added in the same index update operation are allowed.");
                }

                continue;
            }

            var removed = old.SourceFields.Except(suggester.SourceFields, StringComparer.Ordinal).ToList();
            if (removed.Count > 0)
            {
                throw Refused($"Cannot remove field(s) {string.Join(", ", removed.Select(f => $"'{f}'"))} from suggester '{suggester.Name}'.");
            }

            var addedExisting = suggester.SourceFields.Except(old.SourceFields, StringComparer.Ordinal).Where(before.ContainsKey).ToList();
            if (addedExisting.Count > 0)
            {
                throw Refused($"Only new fields added in the same index update operation can be added to an existing suggester.The following fields cannot be added to suggester '{suggester.Name}': {string.Join(", ", addedExisting)}.");
            }
        }

        if (current.Keys.Any(name => !next.ContainsKey(name)))
        {
            throw Refused("This index has suggesters defined in it that cannot be removed. This error is caused by attempting to update an index without including all of its existing suggesters or by attempting to update an index that was created/updated with a newer version of the API.");
        }
    }

    /// <summary>
    /// Analysis components can't change, and new ones need <c>allowIndexDowntime</c>.
    /// </summary>
    private static void ValidateAnalysis(SearchIndex existing, SearchIndex updated, bool allowIndexDowntime)
    {
        var added = false;
        added |= ValidateComponents("analyzers", existing.Analyzers, updated.Analyzers, a => a.Name);
        added |= ValidateComponents("tokenizers", existing.Tokenizers, updated.Tokenizers, t => t.Name);
        added |= ValidateComponents("token filters", existing.TokenFilters, updated.TokenFilters, t => t.Name);
        added |= ValidateComponents("character filters", existing.CharFilters, updated.CharFilters, c => c.Name);
        added |= ValidateComponents("normalizers", existing.Normalizers, updated.Normalizers, n => n.Name);
        if (added && !allowIndexDowntime)
        {
            throw Refused("Index update not allowed because it would cause downtime. In order to add new analyzers, normalizers, tokenizers, token filters, or character filters to an existing index, or modify its similarity settings, set the 'allowIndexDowntime' query parameter to 'true' in the index update request.");
        }
    }

    /// <summary>
    /// Refuses changing or removing existing components of one kind, and returns whether any were added.
    /// </summary>
    private static bool ValidateComponents<T>(string kind, List<T>? existing, List<T>? updated, Func<T, string> name)
    {
        var next = (updated ?? []).ToDictionary(name, StringComparer.Ordinal);
        foreach (var component in existing ?? [])
        {
            if (!next.TryGetValue(name(component), out var match) || !SameJson(component, match))
            {
                throw Refused($"Existing {kind} configuration cannot be modified. Consider adding a new one and use it instead.");
            }
        }

        return next.Count > (existing?.Count ?? 0);
    }

    /// <summary>
    /// Algorithms used by existing vector fields can't change, and those fields can't move to another algorithm.
    /// </summary>
    private static void ValidateVectorSearch(SearchIndex existing, SearchIndex updated, Dictionary<string, SearchField> before, Dictionary<string, SearchField> after)
    {
        var used = before.Values
            .Select(f => AlgorithmOf(existing, f.VectorSearchProfile))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var next = (updated.VectorSearch?.Algorithms ?? []).ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var algorithm in (existing.VectorSearch?.Algorithms ?? []).Where(a => used.Contains(a.Name)))
        {
            if (!next.TryGetValue(algorithm.Name, out var match) || !SameJson(algorithm, match))
            {
                throw Refused("Existing vector search algorithm configuration cannot be modified or deleted because it is already used by an existing vector field.");
            }
        }

        foreach (var (path, field) in before.Where(f => f.Value.VectorSearchProfile is not null))
        {
            if (AlgorithmOf(existing, field.VectorSearchProfile) != AlgorithmOf(updated, after[path].VectorSearchProfile))
            {
                throw Refused("The request is invalid. Details: definition : Algorithm name cannot be updated.");
            }
        }
    }

    /// <summary>
    /// The algorithm a vector search profile uses, if any.
    /// </summary>
    private static string? AlgorithmOf(SearchIndex index, string? profile) =>
        profile is null ? null : index.VectorSearch?.Profiles?.FirstOrDefault(p => p.Name == profile)?.Algorithm;

    /// <summary>
    /// Every field and sub-field by its path, e.g. <c>Rooms/Type</c>.
    /// </summary>
    private static Dictionary<string, SearchField> Flatten(IEnumerable<SearchField>? fields, string prefix)
    {
        var result = new Dictionary<string, SearchField>(StringComparer.Ordinal);
        foreach (var field in fields ?? [])
        {
            result[prefix + field.Name] = field;
            foreach (var (path, sub) in Flatten(field.Fields, prefix + field.Name + "/"))
            {
                result[path] = sub;
            }
        }

        return result;
    }

    /// <summary>
    /// Whether two definitions of a field agree on everything Azure won't let an update change.
    /// </summary>
    private static bool SameImmutableAttributes(SearchField a, SearchField b) =>
        a.Type == b.Type
        && a.Key == b.Key
        && a.Searchable == b.Searchable
        && a.Filterable == b.Filterable
        && a.Sortable == b.Sortable
        && a.Facetable == b.Facetable
        && a.Stored == b.Stored
        && a.Analyzer == b.Analyzer
        && a.IndexAnalyzer == b.IndexAnalyzer
        && a.Normalizer == b.Normalizer
        && a.Dimensions == b.Dimensions
        && a.VectorEncoding == b.VectorEncoding;

    /// <summary>
    /// Compares two definitions by content, so an unchanged component can list its parameters in another order.
    /// </summary>
    private static bool SameJson<T>(T a, T b) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(a), JsonSerializer.SerializeToElement(b));

    /// <summary>
    /// The exception the API turns into Azure's 400 for a refused update.
    /// </summary>
    private static InvalidOperationException Refused(string message) => new(message);
}
