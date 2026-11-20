using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Azure AI Search document semantics the simulator adds: batch validation, recursive merges and the shape of
/// returned documents.
/// </summary>
internal static class DocumentCompat
{
    private static readonly Regex ValidKey = new(@"\A[A-Za-z0-9_=-]+\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// OData's DateTimeOffset: a date, a time and a Z or an offset.
    /// </summary>
    private static readonly Regex OffsetDateTime = new(
        @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant);

    private static readonly Regex JsonNumber = new(
        @"\A-?(?<int>\d+)(\.(?<frac>\d+))?([eE](?<exp>[+-]?\d+))?\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates a batch as Azure does, before any of its actions is applied: the action, the key, and every
    /// property's name and type.
    /// </summary>
    /// <param name="schema">The index definition.</param>
    /// <param name="keyField">The name of the index's key field.</param>
    /// <param name="actions">The batch's actions.</param>
    /// <exception cref="ArgumentException">An action is invalid; Azure rejects the whole batch with HTTP 400.</exception>
    public static void ValidateBatch(SearchIndex schema, string keyField, IReadOnlyList<IndexAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            // IndexAction.ActionType reads an unknown action as upload.
            if (actions[i].TryGetValue("@search.action", out var action)
                && AsString(action)?.ToLowerInvariant() is not ("upload" or "merge" or "mergeorupload" or "delete"))
            {
                throw new ArgumentException($"The request is invalid. Details: actions : {i}: '{AsString(action)}' isn't a valid @search.action. Use 'upload', 'merge', 'mergeOrUpload' or 'delete'.");
            }

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

    /// <summary>
    /// Checks that every property is a field of the (complex) type and has a value of the field's type.
    /// </summary>
    private static void ValidateObject(List<SearchField>? fields, IEnumerable<(string Name, object? Value)> properties, string typeName)
    {
        foreach (var (name, value) in properties)
        {
            var field = fields?.FirstOrDefault(f => f.Name == name)
                ?? throw new ArgumentException($"The request is invalid. Details: The property '{name}' does not exist on type '{typeName}'. Make sure to only use property names that are defined by the type.");
            ValidateValue(field, ToElement(value), $"search.complex.{field.Name}");
        }
    }

    /// <summary>
    /// Checks that a value has the field's type, recursing into collections and complex objects.
    /// </summary>
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
                if (item.ValueKind == JsonValueKind.Null)
                {
                    throw new ArgumentException($"The request is invalid. Details: A null value was found for the property named '{field.Name}', which expects a non-null value: collections can't contain null.");
                }

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
            case SearchFieldDataType.Double when value.ValueKind == JsonValueKind.String && value.GetString() is "NaN" or "INF" or "-INF":
            case SearchFieldDataType.GeographyPoint when IsGeoJsonPoint(value):
                return;
            case SearchFieldDataType.DateTimeOffset when value.ValueKind == JsonValueKind.String
                && OffsetDateTime.IsMatch(value.GetString()!)
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _):
                return;
            default:
                throw Invalid(value, field.Type);
        }
    }

    /// <summary>
    /// Whether a value is a GeoJSON point with a valid longitude and latitude.
    /// </summary>
    private static bool IsGeoJsonPoint(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "Point"
        && value.TryGetProperty("coordinates", out var coordinates) && coordinates.ValueKind == JsonValueKind.Array
        && coordinates.GetArrayLength() == 2
        && coordinates[0].ValueKind == JsonValueKind.Number && coordinates[0].GetDouble() is >= -180 and <= 180
        && coordinates[1].ValueKind == JsonValueKind.Number && coordinates[1].GetDouble() is >= -90 and <= 90;

    /// <summary>
    /// Whether a value is a whole number within a range, read exactly from its JSON text.
    /// </summary>
    private static bool IsWhole(JsonElement value, long min, long max)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (value.TryGetInt64(out var whole))
        {
            return whole >= min && whole <= max;
        }

        // Read the JSON text exactly: decimal and double round fractions such as 1e-29 to whole numbers.
        var number = JsonNumber.Match(value.GetRawText());
        var fraction = number.Groups["frac"].Value;
        var digits = (number.Groups["int"].Value + fraction).TrimStart('0');
        var significant = digits.TrimEnd('0');
        if (significant.Length == 0)
        {
            return min <= 0 && max >= 0;
        }

        var exponentText = number.Groups["exp"].Success ? number.Groups["exp"].Value : "0";
        if (!int.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
        {
            return false;
        }

        var power = (long)exponent - fraction.Length + (digits.Length - significant.Length);
        if (power < 0 || significant.Length + power > 19)
        {
            return false;
        }

        var magnitude = BigInteger.Parse(significant, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)power);
        var signed = number.Value.StartsWith('-') ? -magnitude : magnitude;
        return signed >= min && signed <= max;
    }

    /// <summary>
    /// Azure's error for a value of the wrong type.
    /// </summary>
    private static ArgumentException Invalid(JsonElement value, string type) =>
        new($"The request is invalid. Details: Cannot convert the literal '{(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())}' to the expected type '{type}'.");

    /// <summary>
    /// Merges a <c>merge</c> (or <c>mergeOrUpload</c> on an existing document) into the existing document: only the
    /// given values are set, complex objects merge recursively and collections are replaced.
    /// </summary>
    /// <param name="schema">The index definition.</param>
    /// <param name="existing">The stored document.</param>
    /// <param name="update">The values to merge.</param>
    /// <returns>The merged document.</returns>
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

    /// <summary>
    /// Merges one value: complex objects recursively, anything else replaced.
    /// </summary>
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

    /// <summary>
    /// Shapes a stored document as Azure returns it: every retrievable (or selected) field, with <see langword="null"/>
    /// for missing values, empty collections for missing collections, and dates in UTC to the millisecond.
    /// </summary>
    /// <param name="schema">The index definition.</param>
    /// <param name="document">The stored document.</param>
    /// <param name="select">The <c>$select</c> paths (which can name sub-fields), or <see langword="null"/> for all fields.</param>
    /// <returns>The document as Azure returns it.</returns>
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

    /// <summary>
    /// A tree of the selected paths (a null leaf selects the whole field), or null when <c>*</c> selects everything.
    /// </summary>
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

    /// <summary>
    /// Shapes one field's value, recursing into collections and complex objects.
    /// </summary>
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

    /// <summary>
    /// Formats a date as Azure returns it: UTC, to the millisecond, without trailing zeros.
    /// </summary>
    /// <param name="value">The date.</param>
    /// <returns>The date, e.g. <c>2024-03-05T08:20:30.123Z</c>.</returns>
    internal static string FormatDate(DateTimeOffset value)
    {
        var utc = Filtering.ODataFilter.ToAzurePrecision(value).UtcDateTime;
        var text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return utc.Millisecond == 0 ? text + "Z" : text + "." + utc.Millisecond.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0') + "Z";
    }

    /// <summary>
    /// A field describing one element of a collection field.
    /// </summary>
    private static SearchField ElementOf(SearchField collection) => new()
    {
        Name = collection.Name,
        Type = collection.Type["Collection(".Length..^1],
        Fields = collection.Fields,
        Retrievable = collection.Retrievable,
    };

    /// <summary>
    /// A document value as JSON; values set from code (not parsed from JSON) are serialized first.
    /// </summary>
    private static JsonElement ToElement(object? value) => value switch
    {
        null => default,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>
    /// A value as text, for keys, actions and error messages.
    /// </summary>
    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement e => e.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
