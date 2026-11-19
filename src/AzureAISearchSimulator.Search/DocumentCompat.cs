using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search;

// Azure's document semantics that upstream doesn't model: batch validation, recursive merges and the shape of returned documents.
internal static class DocumentCompat
{
    private static readonly Regex ValidKey = new(@"\A[A-Za-z0-9_=-]+\z", RegexOptions.CultureInvariant);

    // Azure rejects the whole batch (HTTP 400) when any action is invalid, before applying any of them.
    public static void ValidateBatch(SearchIndex schema, string keyField, IReadOnlyList<IndexAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var document = actions[i].Document;
            var key = document.TryGetValue(keyField, out var keyValue) ? AsString(keyValue) : null;
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException($"The request is invalid. Details: actions : {i}: Document key cannot be missing or empty.");
            }

            if (!ValidKey.IsMatch(key))
            {
                throw new ArgumentException($"The request is invalid. Details: actions : {i}: Invalid document key: '{key}'. Keys can only contain letters, digits, underscore (_), dash (-), or equal sign (=).");
            }

            if (key.StartsWith('_'))
            {
                throw new ArgumentException($"The request is invalid. Details: actions : {i}: Invalid document key: '{key}'. Keys cannot start with a leading underscore.");
            }

            if (actions[i].ActionType != IndexActionType.Delete)
            {
                ValidateObject(schema.Fields, document.Where(p => !p.Key.StartsWith('@')).Select(p => (p.Key, p.Value)), "search.documentFields");
            }
        }
    }

    private static void ValidateObject(List<SearchField>? fields, IEnumerable<(string Name, object? Value)> properties, string typeName)
    {
        foreach (var (name, value) in properties)
        {
            var field = fields?.FirstOrDefault(f => f.Name == name)
                ?? throw new ArgumentException($"The request is invalid. Details: The property '{name}' does not exist on type '{typeName}'. Make sure to only use property names that are defined by the type.");
            ValidateValue(field, ToElement(value), $"search.complex.{field.Name}");
        }
    }

    private static void ValidateValue(SearchField field, JsonElement value, string complexTypeName)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        if (field.IsCollection)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(value, field.Type);
            }

            var element = ElementOf(field);
            foreach (var item in value.EnumerateArray())
            {
                ValidateValue(element, item, complexTypeName);
            }

            return;
        }

        switch (field.Type)
        {
            case SearchFieldDataType.ComplexType when value.ValueKind == JsonValueKind.Object:
                ValidateObject(field.Fields, value.EnumerateObject().Select(p => (p.Name, (object?)p.Value)), complexTypeName);
                return;
            case SearchFieldDataType.String when value.ValueKind == JsonValueKind.String:
            case SearchFieldDataType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
            case SearchFieldDataType.Int32 when IsWhole(value, int.MinValue, int.MaxValue):
            case SearchFieldDataType.Int64 when IsWhole(value, long.MinValue, long.MaxValue):
            case "Edm.Int16" when IsWhole(value, short.MinValue, short.MaxValue):
            case "Edm.SByte" when IsWhole(value, sbyte.MinValue, sbyte.MaxValue):
            case "Edm.Byte" when IsWhole(value, byte.MinValue, byte.MaxValue):
            case SearchFieldDataType.Double or "Edm.Single" or "Edm.Half" when value.ValueKind == JsonValueKind.Number:
            case SearchFieldDataType.GeographyPoint when value.ValueKind == JsonValueKind.Object:
                return;
            case SearchFieldDataType.DateTimeOffset when value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _):
                return;
            default:
                throw Invalid(value, field.Type);
        }
    }

    private static bool IsWhole(JsonElement value, decimal min, decimal max) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
        && decimal.Truncate(number) == number && number >= min && number <= max;

    private static ArgumentException Invalid(JsonElement value, string type) =>
        new($"The request is invalid. Details: Cannot convert the literal '{(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())}' to the expected type '{type}'.");

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

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
