using System.Globalization;
using System.Text.Json;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search;

// Azure's document semantics that upstream doesn't model: recursive merges and the shape of returned documents.
internal static class DocumentCompat
{
    // Merge (and mergeOrUpload on an existing document) sets only the given values; complex objects merge
    // recursively, collections are replaced.
    public static Dictionary<string, object?> Merge(SearchIndex schema, Dictionary<string, object?> existing, Dictionary<string, object?> update)
    {
        var merged = new Dictionary<string, object?>(existing);
        foreach (var (name, value) in update)
        {
            var field = schema.Fields.FirstOrDefault(f => f.Name == name);
            merged[name] = field is null ? value : MergeValue(field, merged.TryGetValue(name, out var old) ? old : null, value);
        }

        return merged;
    }

    private static object? MergeValue(SearchField field, object? existing, object? update)
    {
        var updated = ToElement(update);
        var current = ToElement(existing);
        if (field.Type != SearchFieldDataType.ComplexType || updated.ValueKind != JsonValueKind.Object || current.ValueKind != JsonValueKind.Object)
        {
            return update;
        }

        var result = current.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        foreach (var property in updated.EnumerateObject())
        {
            var sub = field.Fields?.FirstOrDefault(f => f.Name == property.Name);
            result[property.Name] = sub is null ? property.Value : MergeValue(sub, result.TryGetValue(property.Name, out var old) ? old : null, property.Value);
        }

        return JsonSerializer.SerializeToElement(result);
    }

    // Azure returns every retrievable (or selected) field: missing values are null, missing collections are empty,
    // and dates are UTC to the millisecond.
    public static Dictionary<string, object?> Shape(SearchIndex schema, Dictionary<string, object?> document, IEnumerable<string>? select)
    {
        var selection = select is null ? null : BuildSelection(select);
        var shaped = new Dictionary<string, object?>();
        foreach (var field in schema.Fields)
        {
            if (field.Retrievable == false || (selection is not null && !selection.ContainsKey(field.Name)))
            {
                continue;
            }

            shaped[field.Name] = ShapeValue(field, document.TryGetValue(field.Name, out var value) ? ToElement(value) : default, selection?[field.Name]);
        }

        return shaped;
    }

    private static Dictionary<string, object?> BuildSelection(IEnumerable<string> paths)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var path in paths.Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (path == "*")
            {
                return null!;
            }

            var node = root;
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (i == segments.Length - 1)
                {
                    node[segments[i]] = null;
                }
                else
                {
                    if (!node.TryGetValue(segments[i], out var child) || child is not Dictionary<string, object?> childNode)
                    {
                        if (node.ContainsKey(segments[i]) && child is null)
                        {
                            break;
                        }

                        childNode = new Dictionary<string, object?>(StringComparer.Ordinal);
                        node[segments[i]] = childNode;
                    }

                    node = childNode;
                }
            }
        }

        return root;
    }

    private static object? ShapeValue(SearchField field, JsonElement value, object? selection)
    {
        var missing = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        if (field.IsCollection)
        {
            if (missing || value.ValueKind != JsonValueKind.Array)
            {
                return field.Type == SearchFieldDataType.CollectionSingle && missing ? null : Array.Empty<object?>();
            }

            var element = ElementOf(field);
            return value.EnumerateArray().Select(item => ShapeValue(element, item, selection)).ToList();
        }

        if (field.Type == SearchFieldDataType.ComplexType)
        {
            if (missing || value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var subSelection = selection as Dictionary<string, object?>;
            var shaped = new Dictionary<string, object?>();
            foreach (var sub in field.Fields ?? [])
            {
                if (sub.Retrievable == false || (subSelection is not null && !subSelection.ContainsKey(sub.Name)))
                {
                    continue;
                }

                shaped[sub.Name] = ShapeValue(sub, value.TryGetProperty(sub.Name, out var subValue) ? subValue : default, subSelection?[sub.Name]);
            }

            return shaped;
        }

        if (missing)
        {
            return null;
        }

        return field.Type == SearchFieldDataType.DateTimeOffset && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? FormatDate(date)
                : value;
    }

    internal static string FormatDate(DateTimeOffset value)
    {
        var utc = Filtering.ODataFilter.ToAzurePrecision(value).UtcDateTime;
        var text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return utc.Millisecond == 0 ? text + "Z" : text + "." + utc.Millisecond.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0') + "Z";
    }

    private static SearchField ElementOf(SearchField collection) => new()
    {
        Name = collection.Name,
        Type = collection.Type["Collection(".Length..^1],
        Fields = collection.Fields,
        Retrievable = collection.Retrievable,
    };

    private static JsonElement ToElement(object? value) => value switch
    {
        null => default,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(value),
    };
}
