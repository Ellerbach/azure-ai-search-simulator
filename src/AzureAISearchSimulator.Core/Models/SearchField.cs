using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// Represents a field definition in a search index.
/// </summary>
public class SearchField
{
    /// <summary>
    /// Name of the field.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// EDM data type of the field.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether this field is the document key.
    /// </summary>
    [JsonPropertyName("key")]
    public bool Key { get; set; }

    /// <summary>
    /// Whether the field is searchable (full-text search).
    /// </summary>
    [JsonPropertyName("searchable")]
    public bool? Searchable { get; set; }

    /// <summary>
    /// Whether the field can be used in filter expressions.
    /// </summary>
    [JsonPropertyName("filterable")]
    public bool? Filterable { get; set; }

    /// <summary>
    /// Whether the field can be used in orderby expressions.
    /// </summary>
    [JsonPropertyName("sortable")]
    public bool? Sortable { get; set; }

    /// <summary>
    /// Whether the field can be used in facet queries.
    /// </summary>
    [JsonPropertyName("facetable")]
    public bool? Facetable { get; set; }

    /// <summary>
    /// Whether the field is returned in search results.
    /// </summary>
    [JsonPropertyName("retrievable")]
    public bool? Retrievable { get; set; }

    /// <summary>
    /// Analyzer name for indexing and queries.
    /// </summary>
    [JsonPropertyName("analyzer")]
    public string? Analyzer { get; set; }

    /// <summary>
    /// Analyzer name for indexing.
    /// </summary>
    [JsonPropertyName("indexAnalyzer")]
    public string? IndexAnalyzer { get; set; }

    /// <summary>
    /// Analyzer name for search queries.
    /// </summary>
    [JsonPropertyName("searchAnalyzer")]
    public string? SearchAnalyzer { get; set; }

    /// <summary>
    /// Normalizer name for filtering, sorting, and faceting.
    /// Applies case-insensitive or accent-insensitive operations.
    /// Added in API version 2025-09-01.
    /// </summary>
    [JsonPropertyName("normalizer")]
    public string? Normalizer { get; set; }

    /// <summary>
    /// Synonym map names associated with this field.
    /// </summary>
    [JsonPropertyName("synonymMaps")]
    public List<string>? SynonymMaps { get; set; }

    /// <summary>
    /// Sub-fields for complex types.
    /// </summary>
    [JsonPropertyName("fields")]
    public List<SearchField>? Fields { get; set; }

    /// <summary>
    /// Number of dimensions for vector fields.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }

    /// <summary>
    /// Vector search profile name for vector fields.
    /// </summary>
    [JsonPropertyName("vectorSearchProfile")]
    public string? VectorSearchProfile { get; set; }

    /// <summary>
    /// Default value for the field.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Checks if this field is a vector field.
    /// </summary>
    [JsonIgnore]
    public bool IsVector => Type == SearchFieldDataType.CollectionSingle;

    /// <summary>
    /// Checks if this field is a complex type.
    /// </summary>
    [JsonIgnore]
    public bool IsComplex => Type == SearchFieldDataType.ComplexType || 
                             Type == SearchFieldDataType.CollectionComplex;

    /// <summary>
    /// Checks if this field is a collection type.
    /// </summary>
    [JsonIgnore]
    public bool IsCollection => Type.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// EDM data type constants for search fields.
/// </summary>
public static class SearchFieldDataType
{
    public const string String = "Edm.String";
    public const string Int32 = "Edm.Int32";
    public const string Int64 = "Edm.Int64";
    public const string Double = "Edm.Double";
    public const string Boolean = "Edm.Boolean";
    public const string DateTimeOffset = "Edm.DateTimeOffset";
    public const string GeographyPoint = "Edm.GeographyPoint";
    public const string ComplexType = "Edm.ComplexType";
    public const string CollectionString = "Collection(Edm.String)";
    public const string CollectionInt32 = "Collection(Edm.Int32)";
    public const string CollectionInt64 = "Collection(Edm.Int64)";
    public const string CollectionDouble = "Collection(Edm.Double)";
    public const string CollectionBoolean = "Collection(Edm.Boolean)";
    public const string CollectionDateTimeOffset = "Collection(Edm.DateTimeOffset)";
    public const string CollectionGeographyPoint = "Collection(Edm.GeographyPoint)";
    public const string CollectionComplex = "Collection(Edm.ComplexType)";
    public const string CollectionSingle = "Collection(Edm.Single)";

    /// <summary>
    /// Validates if the given type is a valid EDM type.
    /// </summary>
    public static bool IsValid(string type)
    {
        return type switch
        {
            String or Int32 or Int64 or Double or Boolean or 
            DateTimeOffset or GeographyPoint or ComplexType or
            CollectionString or CollectionInt32 or CollectionInt64 or 
            CollectionDouble or CollectionBoolean or CollectionDateTimeOffset or 
            CollectionGeographyPoint or CollectionComplex or CollectionSingle => true,
            _ => type.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Checks if the type supports searchable attribute.
    /// </summary>
    public static bool SupportsSearchable(string type)
    {
        return type == String || type == CollectionString || type == CollectionSingle;
    }

    /// <summary>
    /// Checks if the type supports filterable attribute.
    /// </summary>
    public static bool SupportsFilterable(string type)
    {
        return type != ComplexType && type != CollectionComplex && type != CollectionSingle;
    }

    /// <summary>
    /// Checks if the type supports sortable attribute.
    /// </summary>
    public static bool SupportsSortable(string type)
    {
        return !type.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase) && 
               type != ComplexType;
    }

    /// <summary>
    /// Checks if the type supports facetable attribute.
    /// </summary>
    public static bool SupportsFacetable(string type)
    {
        return type != ComplexType && type != CollectionComplex && 
               type != GeographyPoint && type != CollectionSingle;
    }
}
