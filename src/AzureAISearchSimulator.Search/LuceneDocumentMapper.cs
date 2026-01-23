using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Util;
using AzureAISearchSimulator.Core.Models;
using System.Text.Json;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Maps Azure AI Search field types to Lucene field types.
/// </summary>
public static class LuceneDocumentMapper
{
    /// <summary>
    /// Lucene version used throughout the application.
    /// </summary>
    public static readonly LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    /// <summary>
    /// Converts an Azure AI Search document to a Lucene document.
    /// </summary>
    /// <param name="searchDocument">The document as a dictionary.</param>
    /// <param name="schema">The index schema containing field definitions.</param>
    /// <returns>A Lucene Document.</returns>
    public static Document ToLuceneDocument(Dictionary<string, object?> searchDocument, SearchIndex schema)
    {
        var doc = new Document();

        foreach (var field in schema.Fields)
        {
            if (!searchDocument.TryGetValue(field.Name, out var value) || value == null)
            {
                continue;
            }

            var luceneFields = CreateLuceneFields(field, value);
            foreach (var luceneField in luceneFields)
            {
                doc.Add(luceneField);
            }
        }

        // Store the original JSON for retrieval
        doc.Add(new StoredField("_raw_json", JsonSerializer.Serialize(searchDocument)));

        return doc;
    }

    /// <summary>
    /// Converts a Lucene document back to a search document.
    /// </summary>
    /// <param name="luceneDoc">The Lucene document.</param>
    /// <param name="selectedFields">Fields to include (null for all).</param>
    /// <returns>The document as a dictionary.</returns>
    public static Dictionary<string, object?> FromLuceneDocument(Document luceneDoc, IEnumerable<string>? selectedFields = null)
    {
        var rawJson = luceneDoc.Get("_raw_json");
        if (!string.IsNullOrEmpty(rawJson))
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawJson) ?? new();
            
            if (selectedFields != null)
            {
                var selected = selectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
                return doc.Where(kvp => selected.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
            
            return doc;
        }

        // Fallback: reconstruct from Lucene fields
        var result = new Dictionary<string, object?>();
        foreach (var field in luceneDoc.Fields)
        {
            if (field.Name.StartsWith("_"))
                continue;

            if (selectedFields != null && !selectedFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            result[field.Name] = field.GetStringValue();
        }
        return result;
    }

    private static IEnumerable<IIndexableField> CreateLuceneFields(SearchField field, object value)
    {
        var fields = new List<IIndexableField>();

        switch (field.Type.ToLowerInvariant())
        {
            case "edm.string":
                fields.AddRange(CreateStringFields(field, value));
                break;

            case "edm.int32":
                fields.AddRange(CreateInt32Fields(field, value));
                break;

            case "edm.int64":
                fields.AddRange(CreateInt64Fields(field, value));
                break;

            case "edm.double":
                fields.AddRange(CreateDoubleFields(field, value));
                break;

            case "edm.boolean":
                fields.AddRange(CreateBooleanFields(field, value));
                break;

            case "edm.datetimeoffset":
                fields.AddRange(CreateDateTimeFields(field, value));
                break;

            case "edm.geographypoint":
                fields.AddRange(CreateGeoPointFields(field, value));
                break;

            case "collection(edm.string)":
                fields.AddRange(CreateCollectionStringFields(field, value));
                break;

            case "collection(edm.single)":
                // Vector field - stored for retrieval, not indexed in Lucene
                fields.Add(new StoredField(field.Name, JsonSerializer.Serialize(value)));
                break;

            default:
                // Unknown type - store as JSON string
                fields.Add(new StoredField(field.Name, JsonSerializer.Serialize(value)));
                break;
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateStringFields(SearchField field, object value)
    {
        var strValue = ConvertToString(value);
        var fields = new List<IIndexableField>();

        if (field.Key == true)
        {
            // Key field - store and index exactly
            fields.Add(new StringField(field.Name, strValue, Field.Store.YES));
        }
        else if (field.Searchable == true)
        {
            // Searchable - use TextField for full-text search
            fields.Add(new TextField(field.Name, strValue, field.Retrievable != false ? Field.Store.YES : Field.Store.NO));
        }
        else if (field.Filterable == true || field.Sortable == true)
        {
            // Filterable/Sortable - use StringField for exact matching
            fields.Add(new StringField(field.Name, strValue, field.Retrievable != false ? Field.Store.YES : Field.Store.NO));
            
            if (field.Sortable == true)
            {
                fields.Add(new SortedDocValuesField(field.Name, new BytesRef(strValue)));
            }
        }
        else if (field.Retrievable != false)
        {
            // Just store for retrieval
            fields.Add(new StoredField(field.Name, strValue));
        }

        if (field.Facetable == true)
        {
            fields.Add(new SortedSetDocValuesField(field.Name + "_facet", new BytesRef(strValue)));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateInt32Fields(SearchField field, object value)
    {
        var intValue = ConvertToInt32(value);
        var fields = new List<IIndexableField>();

        if (field.Filterable == true)
        {
            fields.Add(new Int32Field(field.Name, intValue, Field.Store.NO));
        }

        if (field.Sortable == true)
        {
            fields.Add(new NumericDocValuesField(field.Name + "_sort", intValue));
        }

        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, intValue));
        }

        if (field.Facetable == true)
        {
            fields.Add(new NumericDocValuesField(field.Name + "_facet", intValue));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateInt64Fields(SearchField field, object value)
    {
        var longValue = ConvertToInt64(value);
        var fields = new List<IIndexableField>();

        if (field.Filterable == true)
        {
            fields.Add(new Int64Field(field.Name, longValue, Field.Store.NO));
        }

        if (field.Sortable == true)
        {
            fields.Add(new NumericDocValuesField(field.Name + "_sort", longValue));
        }

        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, longValue));
        }

        if (field.Facetable == true)
        {
            fields.Add(new NumericDocValuesField(field.Name + "_facet", longValue));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateDoubleFields(SearchField field, object value)
    {
        var doubleValue = ConvertToDouble(value);
        var fields = new List<IIndexableField>();

        if (field.Filterable == true)
        {
            fields.Add(new DoubleField(field.Name, doubleValue, Field.Store.NO));
        }

        if (field.Sortable == true)
        {
            fields.Add(new DoubleDocValuesField(field.Name + "_sort", doubleValue));
        }

        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, doubleValue));
        }

        if (field.Facetable == true)
        {
            fields.Add(new DoubleDocValuesField(field.Name + "_facet", doubleValue));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateBooleanFields(SearchField field, object value)
    {
        var boolValue = ConvertToBoolean(value);
        var strValue = boolValue ? "true" : "false";
        var fields = new List<IIndexableField>();

        if (field.Filterable == true)
        {
            fields.Add(new StringField(field.Name, strValue, Field.Store.NO));
        }

        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, strValue));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateDateTimeFields(SearchField field, object value)
    {
        var fields = new List<IIndexableField>();
        
        DateTimeOffset dateValue;
        if (value is DateTimeOffset dto)
        {
            dateValue = dto;
        }
        else if (value is DateTime dt)
        {
            dateValue = new DateTimeOffset(dt);
        }
        else
        {
            dateValue = DateTimeOffset.Parse(ConvertToString(value));
        }

        var ticks = dateValue.UtcTicks;

        if (field.Filterable == true)
        {
            fields.Add(new Int64Field(field.Name, ticks, Field.Store.NO));
        }

        if (field.Sortable == true)
        {
            fields.Add(new NumericDocValuesField(field.Name + "_sort", ticks));
        }

        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, dateValue.ToString("O")));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateGeoPointFields(SearchField field, object value)
    {
        var fields = new List<IIndexableField>();
        
        // Store as JSON for now - geospatial queries would need specialized implementation
        var json = value is string s ? s : JsonSerializer.Serialize(value);
        
        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, json));
        }

        return fields;
    }

    private static IEnumerable<IIndexableField> CreateCollectionStringFields(SearchField field, object value)
    {
        var fields = new List<IIndexableField>();
        
        IEnumerable<string> values;
        if (value is IEnumerable<object> objList)
        {
            values = objList.Select(o => ConvertToString(o));
        }
        else if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            values = je.EnumerateArray().Select(e => e.GetString() ?? "");
        }
        else
        {
            values = new[] { ConvertToString(value) };
        }

        foreach (var strValue in values)
        {
            if (field.Searchable == true)
            {
                fields.Add(new TextField(field.Name, strValue, Field.Store.NO));
            }
            
            if (field.Filterable == true)
            {
                fields.Add(new StringField(field.Name, strValue, Field.Store.NO));
            }

            if (field.Facetable == true)
            {
                fields.Add(new SortedSetDocValuesField(field.Name + "_facet", new BytesRef(strValue)));
            }
        }

        // Store the entire collection as JSON
        if (field.Retrievable != false)
        {
            fields.Add(new StoredField(field.Name, JsonSerializer.Serialize(values)));
        }

        return fields;
    }

    private static string ConvertToString(object? value)
    {
        if (value == null)
            return string.Empty;
            
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? "",
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.GetRawText()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    private static int ConvertToInt32(object? value)
    {
        if (value == null)
            return 0;
            
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt32(out var i) ? i : (int)je.GetDouble(),
                JsonValueKind.String => int.TryParse(je.GetString(), out var s) ? s : 0,
                _ => 0
            };
        }

        return Convert.ToInt32(value);
    }

    private static long ConvertToInt64(object? value)
    {
        if (value == null)
            return 0;
            
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : (long)je.GetDouble(),
                JsonValueKind.String => long.TryParse(je.GetString(), out var s) ? s : 0,
                _ => 0
            };
        }

        return Convert.ToInt64(value);
    }

    private static double ConvertToDouble(object? value)
    {
        if (value == null)
            return 0;
            
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.GetDouble(),
                JsonValueKind.String => double.TryParse(je.GetString(), out var s) ? s : 0,
                _ => 0
            };
        }

        return Convert.ToDouble(value);
    }

    private static bool ConvertToBoolean(object? value)
    {
        if (value == null)
            return false;
            
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(je.GetString(), out var b) && b,
                JsonValueKind.Number => je.GetDouble() != 0,
                _ => false
            };
        }

        return Convert.ToBoolean(value);
    }

    /// <summary>
    /// Gets the key field name from an index schema.
    /// </summary>
    public static string GetKeyFieldName(SearchIndex schema)
    {
        return schema.Fields.FirstOrDefault(f => f.Key == true)?.Name
            ?? throw new InvalidOperationException("Index has no key field");
    }

    /// <summary>
    /// Gets the key value from a document.
    /// </summary>
    public static string GetKeyValue(Dictionary<string, object?> document, SearchIndex schema)
    {
        var keyField = GetKeyFieldName(schema);
        if (!document.TryGetValue(keyField, out var value) || value == null)
        {
            throw new ArgumentException($"Document is missing key field '{keyField}'");
        }
        return ConvertToString(value);
    }
}
