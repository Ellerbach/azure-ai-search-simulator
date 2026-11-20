using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Azure AI Search document semantics the simulator adds: batch validation and value conversion, recursive merges
/// and the shape of returned documents.
/// </summary>
internal static class DocumentCompat
{
    private static readonly Regex ValidKey = new(@"\A[A-Za-z0-9_=-]+\z", RegexOptions.CultureInvariant);

    private static readonly Regex ZoneSuffix = new(@"(Z|z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The number format Azure's OData reader parses doubles with: invariant, with OData's INF, -INF and NaN.
    /// </summary>
    private static readonly NumberFormatInfo ODataNumbers = CreateODataNumbers();

    /// <summary>
    /// Validates a batch as Azure does, before any of its actions is applied: the action, the key, and every
    /// property's name and type. Each value is converted to its field's type as Azure reads it (e.g. <c>"5"</c> for an
    /// <c>Edm.Int32</c> becomes <c>5</c>) and replaced, in its action, by the value Azure stores.
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
                foreach (var name in actions[i].Keys.Where(k => !k.StartsWith('@')).ToList())
                {
                    var field = FieldOf(schema.Fields, name, "search.documentFields");
                    if (actions[i][name] is { } value)
                    {
                        actions[i][name] = ConvertValue(field, ToElement(value), i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The field a property names, or Azure's error when the (complex) type has no such field.
    /// </summary>
    private static SearchField FieldOf(List<SearchField>? fields, string name, string typeName) =>
        fields?.FirstOrDefault(f => f.Name == name)
            ?? throw new ArgumentException($"The request is invalid. Details: The property '{name}' does not exist on type '{typeName}'. Make sure to only use property names that are defined by the type.");

    /// <summary>
    /// Converts a value to its field's type as Azure reads it, recursing into collections and complex objects.
    /// </summary>
    private static JsonElement ConvertValue(SearchField field, JsonElement value, int action)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return value;
        }

        if (field.IsCollection)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(value, field.Type);
            }

            if (field.IsVector && field.Dimensions is > 0 && value.GetArrayLength() != field.Dimensions)
            {
                throw new ArgumentException($"The request is invalid. Details: The vector field '{field.Name}', with dimension of '{field.Dimensions}', expects a length of '{field.Dimensions}'. However, the provided vector has a length of '{value.GetArrayLength()}'.");
            }

            var element = ElementOf(field);
            var items = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Null
                ? throw new ArgumentException($"The request is invalid. Details: A null value was found for the property named '{field.Name}', which expects a non-null value: collections can't contain null.")
                : ConvertValue(element, item, action)).ToList();

            // Vector elements are kept as given.
            return IsVectorElement(element.Type) ? value : JsonSerializer.SerializeToElement(items);
        }

        if (field.Type == SearchFieldDataType.ComplexType)
        {
            return value.ValueKind == JsonValueKind.Object
                ? JsonSerializer.SerializeToElement(value.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => ConvertValue(FieldOf(field.Fields, p.Name, $"search.complex.{field.Name}"), p.Value, action)))
                : throw Invalid(value, field.Type);
        }

        var payload = Payload(value);
        return field.Type switch
        {
            SearchFieldDataType.String when payload is string => value,
            SearchFieldDataType.Int32 when ToInt32(payload) is { } number => Json(number.ToString(CultureInfo.InvariantCulture)),
            SearchFieldDataType.Int64 when payload is string => throw new ArgumentException($"The request is invalid. Details: Cannot convert a value to target type '{field.Type}' because of conflict between input format string/number and parameter 'IEEE754Compatible' false/true."),
            SearchFieldDataType.Int64 when ToInt64(payload) is { } number => Json(number.ToString(CultureInfo.InvariantCulture)),
            SearchFieldDataType.Double when ToDouble(payload) is { } number => DoubleJson(number),
            SearchFieldDataType.Boolean when ToBoolean(payload) is { } flag => Json(flag ? "true" : "false"),
            SearchFieldDataType.DateTimeOffset when payload is string text && TryParseDate(text, out var date) => JsonSerializer.SerializeToElement(FormatDate(date)),
            SearchFieldDataType.GeographyPoint => ConvertPoint(field, value, action),
            "Edm.Single" or "Edm.Half" when value.ValueKind == JsonValueKind.Number => value,
            "Edm.Int16" when payload is int number && number is >= short.MinValue and <= short.MaxValue => value,
            "Edm.SByte" when payload is int number && number is >= sbyte.MinValue and <= sbyte.MaxValue => value,
            "Edm.Byte" when payload is int number && number is >= byte.MinValue and <= byte.MaxValue => value,
            _ => throw Invalid(value, field.Type),
        };
    }

    /// <summary>
    /// Whether a type is one of a vector field's element types.
    /// </summary>
    private static bool IsVectorElement(string type) => type is "Edm.Single" or "Edm.Half" or "Edm.Int16" or "Edm.SByte" or "Edm.Byte";

    /// <summary>
    /// A JSON primitive as Azure's OData reader reads it: a string, a bool, or a number as an int, else a decimal (no
    /// exponent), else a double; <see langword="null"/> for objects and arrays.
    /// </summary>
    private static object? Payload(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.True or JsonValueKind.False:
                return value.GetBoolean();
            case JsonValueKind.Number:
                var text = value.GetRawText();
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    return whole;
                }

                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var exact))
                {
                    return exact;
                }

                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            default:
                return null;
        }
    }

    /// <summary>
    /// An <c>Edm.Int32</c>: a JSON integer, or a string such as <c>" +5 "</c>.
    /// </summary>
    private static int? ToInt32(object? payload) => payload switch
    {
        int number => number,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
        _ => null,
    };

    /// <summary>
    /// An <c>Edm.Int64</c>: a JSON integer, or a decimal number rounded half to even (<c>2.5</c> is <c>2</c>).
    /// </summary>
    private static long? ToInt64(object? payload)
    {
        try
        {
            return payload switch
            {
                int number => number,
                decimal number => Convert.ToInt64(number),
                _ => null,
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// An <c>Edm.Double</c>: any JSON number, or a string such as <c>"1,000.5"</c>, <c>"nan"</c>, <c>"-Inf"</c> or
    /// <c>"Infinity"</c>.
    /// </summary>
    private static double? ToDouble(object? payload) => payload switch
    {
        int number => number,
        decimal number => (double)number,
        double number => number,
        string text when double.TryParse(text switch { "Infinity" => "INF", "-Infinity" => "-INF", _ => text },
            NumberStyles.Float | NumberStyles.AllowThousands, ODataNumbers, out var number) => number,
        _ => null,
    };

    /// <summary>
    /// An <c>Edm.Boolean</c>: <c>true</c>, <c>false</c>, or a string such as <c>" True "</c>.
    /// </summary>
    private static bool? ToBoolean(object? payload) => payload switch
    {
        bool flag => flag,
        string text when bool.TryParse(text, out var flag) => flag,
        _ => null,
    };

    /// <summary>
    /// Reads a date as Azure's OData reader does: an XML Schema date or time, seconds optional, which must end with a
    /// time zone. A date alone passes that check, as its last hyphen sits where an offset's sign would, and is read as UTC.
    /// </summary>
    private static bool TryParseDate(string text, out DateTimeOffset date)
    {
        date = default;
        var t = text.IndexOf('T', StringComparison.Ordinal);
        if (t > 0 && (text.Length == t + 6 || (text.Length > t + 6 && text[t + 6] != ':')))
        {
            text = text.Insert(t + 6, ":00");
        }

        try
        {
            date = XmlConvert.ToDateTimeOffset(text);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }

        if (!ZoneSuffix.IsMatch(text))
        {
            date = new DateTimeOffset(date.DateTime, TimeSpan.Zero);
        }

        return (text.Length > 1 && text[^1] is 'Z' or 'z') || (text.Length > 6 && text[^6] is '+' or '-');
    }

    /// <summary>
    /// Converts a GeoJSON point as Azure does: two to four coordinates (it keeps the longitude and latitude), other
    /// members allowed, returned with only its coordinates and the WGS 84 <c>crs</c>.
    /// </summary>
    private static JsonElement ConvertPoint(SearchField field, JsonElement value, int action)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The request is invalid. Details: The value specified for the spatial property was not valid. You must specify a valid spatial value.");
        }

        if (QuotedMember(value, "type") is var type && type != "Point")
        {
            throw InvalidGeoJson($"The value '{type}' is not a valid value for the 'type' member.");
        }

        var coordinates = Member(value, "coordinates");
        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            throw InvalidGeoJson("A primitive value was found in an array element where an array was expected.");
        }

        if (coordinates.GetArrayLength() == 0)
        {
            throw new ArgumentException($"The request is invalid. Details: actions : {action}: The 'coordinates' property of the data field '{field.Name}' cannot be empty.");
        }

        var position = coordinates.EnumerateArray().Select(c => c.ValueKind switch
        {
            JsonValueKind.Number => double.Parse(c.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonValueKind.Null => throw InvalidGeoJson("A null value was found in an array element where nulls are not allowed."),
            _ => throw InvalidGeoJson("A non-numeric value was found in an array element where a numeric value was expected."),
        }).ToList();
        if (position.Count > 4 || position.Count < 2)
        {
            throw InvalidGeoJson("A position must contain at least two and no more than four elements.");
        }

        if (value.TryGetProperty("crs", out var crs))
        {
            if (crs.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"The request is invalid. Details: Cannot read the value '{crs.GetRawText()}' as a JSON object.");
            }

            if (QuotedMember(crs, "type") is var crsType && crsType != "name")
            {
                throw InvalidGeoJson($"The value '{crsType}' is not a recognized CRS type.");
            }

            var properties = Member(crs, "properties");
            if (properties.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"The request is invalid. Details: Cannot read the value '{properties.GetRawText()}' as a JSON object.");
            }

            QuotedMember(properties, "name");
        }

        var (longitude, latitude) = (position[0], position[1]);
        if (longitude is < -180 or > 180)
        {
            throw new ArgumentException($"The request is invalid. Details: actions : {action}: The data field '{field.Name}' has an invalid longitude coordinate '{longitude.ToString(CultureInfo.InvariantCulture)}'. A longitude coordinate must be a value between -180.0 and +180.0 degrees. The coordinates should be specified as [longitude, latitude].");
        }

        if (latitude is < -90 or > 90)
        {
            throw new ArgumentException($"The request is invalid. Details: actions : {action}: The data field '{field.Name}' has an invalid latitude coordinate '{latitude.ToString(CultureInfo.InvariantCulture)}'. A latitude coordinate must be a value between -90.0 and +90.0 degrees. The coordinates should be specified as [longitude, latitude].");
        }

        var point = $"[{DoubleJson(longitude).GetRawText()},{DoubleJson(latitude).GetRawText()}]";
        return Json("""{"type":"Point","coordinates":""" + point + ""","crs":{"type":"name","properties":{"name":"EPSG:4326"}}}""");
    }

    /// <summary>
    /// A required GeoJSON member.
    /// </summary>
    private static JsonElement Member(JsonElement value, string name) =>
        value.TryGetProperty(name, out var member) ? member : throw InvalidGeoJson($"The '{name}' member is required, but was not found.");

    /// <summary>
    /// A required GeoJSON member that must be a string.
    /// </summary>
    private static string QuotedMember(JsonElement value, string name) => Member(value, name) is var member && member.ValueKind == JsonValueKind.String
        ? member.GetString()!
        : throw new ArgumentException($"The request is invalid. Details: Cannot read the value '{member.GetRawText()}' for the property '{name}' as a quoted JSON string value.");

    private static ArgumentException InvalidGeoJson(string message) => new($"The request is invalid. Details: Invalid GeoJSON. {message}");

    /// <summary>
    /// A double as Azure writes it: its shortest round-trip text with a decimal point (<c>2.0</c>, <c>1E+21</c>), or the
    /// string <c>"NaN"</c>, <c>"INF"</c> or <c>"-INF"</c>.
    /// </summary>
    internal static JsonElement DoubleJson(double number)
    {
        if (!double.IsFinite(number))
        {
            return JsonSerializer.SerializeToElement(number.ToString(ODataNumbers));
        }

        var text = number.ToString("R", CultureInfo.InvariantCulture);
        return Json(text.IndexOfAny(['.', 'E']) < 0 ? text + ".0" : text);
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static NumberFormatInfo CreateODataNumbers()
    {
        var numbers = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        numbers.PositiveInfinitySymbol = "INF";
        numbers.NegativeInfinitySymbol = "-INF";
        numbers.NaNSymbol = "NaN";
        return NumberFormatInfo.ReadOnly(numbers);
    }

    /// <summary>
    /// Azure's error for a value of the wrong type, quoting it as .NET prints what the OData reader read
    /// (<c>True</c>, <c>5.0</c>, <c>1E+19</c>).
    /// </summary>
    private static ArgumentException Invalid(JsonElement value, string type)
    {
        var literal = Payload(value) switch
        {
            null => value.GetRawText(),
            bool flag => flag ? "True" : "False",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            var text => text.ToString(),
        };
        return new($"The request is invalid. Details: Cannot convert the literal '{literal}' to the expected type '{type}'.");
    }

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
