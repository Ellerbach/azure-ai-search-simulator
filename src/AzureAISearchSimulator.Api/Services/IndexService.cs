using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Implementation of the index management service.
/// </summary>
public partial class IndexService : IIndexService
{
    private readonly IIndexRepository _repository;
    private readonly SimulatorSettings _settings;
    private readonly ILogger<IndexService> _logger;

    [GeneratedRegex(@"^[a-z][a-z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex IndexNameRegex();

    public IndexService(
        IIndexRepository repository,
        IOptions<SimulatorSettings> settings,
        ILogger<IndexService> logger)
    {
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SearchIndex> CreateIndexAsync(SearchIndex index, CancellationToken cancellationToken = default)
    {
        var validation = ValidateIndex(index);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Invalid index definition: {string.Join("; ", validation.Errors)}");
        }

        if (await _repository.ExistsAsync(index.Name, cancellationToken))
        {
            throw new InvalidOperationException($"Index '{index.Name}' already exists");
        }

        var indexCount = (await _repository.GetAllAsync(cancellationToken)).Count;
        if (indexCount >= _settings.MaxIndexes)
        {
            throw new InvalidOperationException(
                $"Maximum number of indexes ({_settings.MaxIndexes}) reached");
        }

        // Apply Azure-compatible defaults before storing
        ApplyIndexDefaults(index);

        _logger.LogInformation("Creating index {IndexName} with {FieldCount} fields", 
            index.Name, index.Fields.Count);

        return await _repository.CreateAsync(index, cancellationToken);
    }

    public async Task<SearchIndex> CreateOrUpdateIndexAsync(string indexName, SearchIndex index, CancellationToken cancellationToken = default)
    {
        index.Name = indexName; // Ensure name matches URL parameter
        
        var validation = ValidateIndex(index);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Invalid index definition: {string.Join("; ", validation.Errors)}");
        }

        // Apply Azure-compatible defaults before storing
        ApplyIndexDefaults(index);

        var existing = await _repository.GetByNameAsync(indexName, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Updating existing index {IndexName}", indexName);
            return await _repository.UpdateAsync(index, cancellationToken);
        }

        var indexCount = (await _repository.GetAllAsync(cancellationToken)).Count;
        if (indexCount >= _settings.MaxIndexes)
        {
            throw new InvalidOperationException(
                $"Maximum number of indexes ({_settings.MaxIndexes}) reached");
        }

        _logger.LogInformation("Creating new index {IndexName} with {FieldCount} fields", 
            indexName, index.Fields.Count);

        return await _repository.CreateAsync(index, cancellationToken);
    }

    public Task<SearchIndex?> GetIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        return _repository.GetByNameAsync(indexName, cancellationToken);
    }

    public Task<IReadOnlyList<SearchIndex>> ListIndexesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public async Task<bool> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting index {IndexName}", indexName);
        return await _repository.DeleteAsync(indexName, cancellationToken);
    }

    public Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        return _repository.ExistsAsync(indexName, cancellationToken);
    }

    /// <summary>
    /// Applies Azure AI Search-compatible defaults to an index definition.
    /// Azure always returns all field properties with their default values,
    /// so we normalize the index to match that behavior.
    /// </summary>
    private static void ApplyIndexDefaults(SearchIndex index)
    {
        // Apply field-level defaults
        if (index.Fields != null)
        {
            foreach (var field in index.Fields)
            {
                ApplyFieldDefaults(field);
            }
        }

        // Normalize scoring function "parameters" alias into type-specific properties
        if (index.ScoringProfiles != null)
        {
            foreach (var profile in index.ScoringProfiles)
            {
                if (profile.Functions != null)
                {
                    foreach (var function in profile.Functions)
                    {
                        function.NormalizeParameters();
                    }
                }
            }
        }

        // Apply index-level defaults for empty collections
        // Azure always returns these as empty arrays rather than omitting them
        index.ScoringProfiles ??= new List<ScoringProfile>();
        index.Suggesters ??= new List<Suggester>();
        index.Analyzers ??= new List<CustomAnalyzer>();
        index.Tokenizers ??= new List<CustomTokenizer>();
        index.TokenFilters ??= new List<CustomTokenFilter>();
        index.CharFilters ??= new List<CustomCharFilter>();

        // Azure defaults to BM25 similarity
        index.Similarity ??= new SimilarityAlgorithm();
    }

    /// <summary>
    /// Applies Azure AI Search-compatible defaults to a field definition.
    /// Azure always returns all field attributes explicitly, applying defaults
    /// based on the field's data type when not specified in the request.
    /// </summary>
    private static void ApplyFieldDefaults(SearchField field)
    {
        var type = field.Type;

        // searchable: defaults to true for Edm.String and Collection(Edm.String), false for others
        field.Searchable ??= SearchFieldDataType.SupportsSearchable(type) && !field.IsVector;

        // filterable: defaults to true for non-complex, non-vector types
        field.Filterable ??= SearchFieldDataType.SupportsFilterable(type);

        // retrievable: defaults to true
        field.Retrievable ??= true;

        // stored: defaults to true
        field.Stored ??= true;

        // sortable: defaults to true for non-collection, non-complex types
        field.Sortable ??= SearchFieldDataType.SupportsSortable(type);

        // facetable: defaults to true for supported types
        field.Facetable ??= SearchFieldDataType.SupportsFacetable(type);

        // synonymMaps: defaults to empty array
        field.SynonymMaps ??= new List<string>();

        // Apply defaults recursively for complex type sub-fields
        if (field.Fields != null)
        {
            foreach (var subField in field.Fields)
            {
                ApplyFieldDefaults(subField);
            }
        }
    }

    public IndexValidationResult ValidateIndex(SearchIndex index)
    {
        var errors = new List<string>();

        // Validate index name
        if (string.IsNullOrWhiteSpace(index.Name))
        {
            errors.Add("Index name is required");
        }
        else if (index.Name.Length < 2 || index.Name.Length > 128)
        {
            errors.Add("Index name must be between 2 and 128 characters");
        }
        else if (!IndexNameRegex().IsMatch(index.Name))
        {
            errors.Add("Index name must start with a letter and contain only lowercase letters, digits, and hyphens");
        }

        // Validate fields
        if (index.Fields == null || index.Fields.Count == 0)
        {
            errors.Add("At least one field is required");
        }
        else
        {
            if (index.Fields.Count > _settings.MaxFieldsPerIndex)
            {
                errors.Add($"Maximum number of fields ({_settings.MaxFieldsPerIndex}) exceeded");
            }

            var keyFields = index.Fields.Where(f => f.Key).ToList();
            if (keyFields.Count == 0)
            {
                errors.Add("At least one field must be marked as key");
            }
            else if (keyFields.Count > 1)
            {
                errors.Add("Only one field can be marked as key");
            }
            else if (keyFields[0].Type != SearchFieldDataType.String)
            {
                errors.Add("Key field must be of type Edm.String");
            }

            var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in index.Fields)
            {
                var fieldErrors = ValidateField(field, fieldNames);
                errors.AddRange(fieldErrors);
            }
        }

        // Validate vector search configuration
        if (index.VectorSearch != null)
        {
            var vectorFields = index.Fields?.Where(f => f.IsVector).ToList() ?? new List<SearchField>();
            foreach (var vectorField in vectorFields)
            {
                if (string.IsNullOrEmpty(vectorField.VectorSearchProfile))
                {
                    errors.Add($"Vector field '{vectorField.Name}' must specify a vectorSearchProfile");
                }
                else if (index.VectorSearch.Profiles?.Any(p => p.Name == vectorField.VectorSearchProfile) != true)
                {
                    errors.Add($"Vector field '{vectorField.Name}' references unknown profile '{vectorField.VectorSearchProfile}'");
                }
            }
        }

        // Validate scoring profiles
        ValidateScoringProfiles(index, errors);

        // Validate suggesters
        if (index.Suggesters != null)
        {
            foreach (var suggester in index.Suggesters)
            {
                if (string.IsNullOrWhiteSpace(suggester.Name))
                {
                    errors.Add("Suggester name is required");
                }

                foreach (var sourceField in suggester.SourceFields)
                {
                    var field = index.Fields?.FirstOrDefault(f => f.Name == sourceField);
                    if (field == null)
                    {
                        errors.Add($"Suggester '{suggester.Name}' references unknown field '{sourceField}'");
                    }
                    else if (field.Type != SearchFieldDataType.String)
                    {
                        errors.Add($"Suggester '{suggester.Name}' field '{sourceField}' must be of type Edm.String");
                    }
                }
            }
        }

        return errors.Count == 0 
            ? IndexValidationResult.Success() 
            : IndexValidationResult.Failure(errors);
    }

    /// <summary>
    /// Validates scoring profiles, text weights, and scoring function field references.
    /// </summary>
    private static void ValidateScoringProfiles(SearchIndex index, List<string> errors)
    {
        if (index.ScoringProfiles == null || index.ScoringProfiles.Count == 0)
        {
            // Validate defaultScoringProfile references even when no profiles defined
            if (!string.IsNullOrWhiteSpace(index.DefaultScoringProfile))
            {
                errors.Add($"defaultScoringProfile '{index.DefaultScoringProfile}' references a scoring profile that does not exist");
            }
            return;
        }

        // Collect all field names (flattened) for validation lookups
        var allFields = new Dictionary<string, SearchField>(StringComparer.OrdinalIgnoreCase);
        if (index.Fields != null)
        {
            CollectFields(index.Fields, allFields, prefix: "");
        }

        // Validate max profile count (Azure allows up to 100)
        if (index.ScoringProfiles.Count > 100)
        {
            errors.Add($"Index has {index.ScoringProfiles.Count} scoring profiles, but maximum is 100");
        }

        // Validate profile names are unique
        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in index.ScoringProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                errors.Add("Scoring profile name is required");
                continue;
            }

            if (!profileNames.Add(profile.Name))
            {
                errors.Add($"Duplicate scoring profile name: '{profile.Name}'");
            }

            // Validate functionAggregation
            if (!string.IsNullOrWhiteSpace(profile.FunctionAggregation))
            {
                var validAggregations = new[] { "sum", "average", "minimum", "maximum", "firstMatching" };
                if (!validAggregations.Contains(profile.FunctionAggregation, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Scoring profile '{profile.Name}' has invalid functionAggregation '{profile.FunctionAggregation}'. Valid values: {string.Join(", ", validAggregations)}");
                }
            }

            // Validate text weights
            if (profile.Text?.Weights != null)
            {
                foreach (var (fieldName, weight) in profile.Text.Weights)
                {
                    if (!allFields.TryGetValue(fieldName, out var field))
                    {
                        errors.Add($"Scoring profile '{profile.Name}' text weight references unknown field '{fieldName}'");
                    }
                    else if (field.Searchable != true)
                    {
                        errors.Add($"Scoring profile '{profile.Name}' text weight field '{fieldName}' must be searchable");
                    }

                    if (weight <= 0)
                    {
                        errors.Add($"Scoring profile '{profile.Name}' text weight for field '{fieldName}' must be positive");
                    }
                }
            }

            // Validate scoring functions
            if (profile.Functions != null)
            {
                foreach (var function in profile.Functions)
                {
                    ValidateScoringFunction(profile.Name, function, allFields, errors);
                }
            }
        }

        // Validate defaultScoringProfile references an existing profile
        if (!string.IsNullOrWhiteSpace(index.DefaultScoringProfile))
        {
            if (!profileNames.Contains(index.DefaultScoringProfile))
            {
                errors.Add($"defaultScoringProfile '{index.DefaultScoringProfile}' references a scoring profile that does not exist");
            }
        }
    }

    /// <summary>
    /// Recursively collects all fields from the index into a flat dictionary.
    /// </summary>
    private static void CollectFields(List<SearchField> fields, Dictionary<string, SearchField> result, string prefix)
    {
        foreach (var field in fields)
        {
            var fullName = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}/{field.Name}";
            result[fullName] = field;
            // Also add without prefix for top-level lookups
            if (!string.IsNullOrEmpty(prefix))
            {
                result.TryAdd(field.Name, field);
            }
            if (field.Fields != null)
            {
                CollectFields(field.Fields, result, fullName);
            }
        }
    }

    /// <summary>
    /// Validates a single scoring function's field reference and type-specific parameters.
    /// </summary>
    private static void ValidateScoringFunction(string profileName, ScoringFunction function, Dictionary<string, SearchField> allFields, List<string> errors)
    {
        var validTypes = new[] { "freshness", "magnitude", "distance", "tag" };
        if (string.IsNullOrWhiteSpace(function.Type))
        {
            errors.Add($"Scoring profile '{profileName}' has a function with no type specified");
            return;
        }

        if (!validTypes.Contains(function.Type, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Scoring profile '{profileName}' has invalid function type '{function.Type}'. Valid types: {string.Join(", ", validTypes)}");
            return;
        }

        // Validate interpolation
        if (!string.IsNullOrWhiteSpace(function.Interpolation))
        {
            var validInterpolations = new[] { "linear", "constant", "quadratic", "logarithmic" };
            if (!validInterpolations.Contains(function.Interpolation, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Scoring profile '{profileName}' function on field '{function.FieldName}' has invalid interpolation '{function.Interpolation}'");
            }

            // Tag functions only support linear and constant interpolation
            if (string.Equals(function.Type, "tag", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(function.Interpolation, "linear", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(function.Interpolation, "constant", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Scoring profile '{profileName}' tag function on field '{function.FieldName}' only supports 'linear' or 'constant' interpolation, not '{function.Interpolation}'");
            }
        }

        // Validate boost: must not be zero or 1.0 (Azure requires boost != 0 and != 1.0)
        if (function.Boost == 0)
        {
            errors.Add($"Scoring profile '{profileName}' function on field '{function.FieldName}' must have a non-zero boost value");
        }
        else if (Math.Abs(function.Boost - 1.0) < double.Epsilon)
        {
            errors.Add($"Scoring profile '{profileName}' function on field '{function.FieldName}' boost must not be 1.0");
        }

        // Validate field reference and compatible types
        if (string.IsNullOrWhiteSpace(function.FieldName))
        {
            errors.Add($"Scoring profile '{profileName}' has a '{function.Type}' function with no fieldName");
            return;
        }

        if (!allFields.TryGetValue(function.FieldName, out var field))
        {
            errors.Add($"Scoring profile '{profileName}' function references unknown field '{function.FieldName}'");
            return;
        }

        // Scoring function fields must be filterable (Azure requirement)
        if (field.Filterable != true)
        {
            errors.Add($"Scoring profile '{profileName}' function field '{function.FieldName}' must be filterable");
        }

        switch (function.Type.ToLowerInvariant())
        {
            case "freshness":
                if (field.Type != SearchFieldDataType.DateTimeOffset && field.Type != SearchFieldDataType.CollectionDateTimeOffset)
                {
                    errors.Add($"Scoring profile '{profileName}' freshness function field '{function.FieldName}' must be of type Edm.DateTimeOffset, but is '{field.Type}'");
                }
                if (function.Freshness == null)
                {
                    errors.Add($"Scoring profile '{profileName}' freshness function on field '{function.FieldName}' is missing the 'freshness' parameters");
                }
                else if (string.IsNullOrWhiteSpace(function.Freshness.BoostingDuration))
                {
                    errors.Add($"Scoring profile '{profileName}' freshness function on field '{function.FieldName}' is missing 'boostingDuration'");
                }
                break;

            case "magnitude":
                var numericTypes = new[] 
                { 
                    SearchFieldDataType.Double, SearchFieldDataType.Int32, SearchFieldDataType.Int64,
                    SearchFieldDataType.CollectionDouble, SearchFieldDataType.CollectionInt32, SearchFieldDataType.CollectionInt64
                };
                if (!numericTypes.Contains(field.Type))
                {
                    errors.Add($"Scoring profile '{profileName}' magnitude function field '{function.FieldName}' must be a numeric type (Edm.Double, Edm.Int32, or Edm.Int64), but is '{field.Type}'");
                }
                if (function.Magnitude == null)
                {
                    errors.Add($"Scoring profile '{profileName}' magnitude function on field '{function.FieldName}' is missing the 'magnitude' parameters");
                }
                break;

            case "distance":
                if (field.Type != SearchFieldDataType.GeographyPoint && field.Type != SearchFieldDataType.CollectionGeographyPoint)
                {
                    errors.Add($"Scoring profile '{profileName}' distance function field '{function.FieldName}' must be of type Edm.GeographyPoint, but is '{field.Type}'");
                }
                if (function.Distance == null)
                {
                    errors.Add($"Scoring profile '{profileName}' distance function on field '{function.FieldName}' is missing the 'distance' parameters");
                }
                else if (string.IsNullOrWhiteSpace(function.Distance.ReferencePointParameter))
                {
                    errors.Add($"Scoring profile '{profileName}' distance function on field '{function.FieldName}' is missing 'referencePointParameter'");
                }
                break;

            case "tag":
                var tagTypes = new[] { SearchFieldDataType.String, SearchFieldDataType.CollectionString };
                if (!tagTypes.Contains(field.Type))
                {
                    errors.Add($"Scoring profile '{profileName}' tag function field '{function.FieldName}' must be of type Edm.String or Collection(Edm.String), but is '{field.Type}'");
                }
                if (function.Tag == null)
                {
                    errors.Add($"Scoring profile '{profileName}' tag function on field '{function.FieldName}' is missing the 'tag' parameters");
                }
                else if (string.IsNullOrWhiteSpace(function.Tag.TagsParameter))
                {
                    errors.Add($"Scoring profile '{profileName}' tag function on field '{function.FieldName}' is missing 'tagsParameter'");
                }
                break;
        }
    }

    private static List<string> ValidateField(SearchField field, HashSet<string> fieldNames, string prefix = "")
    {
        var errors = new List<string>();
        var fullName = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";

        // Validate field name
        if (string.IsNullOrWhiteSpace(field.Name))
        {
            errors.Add($"Field name is required (at '{prefix}')");
            return errors;
        }

        if (!fieldNames.Add(fullName))
        {
            errors.Add($"Duplicate field name: '{fullName}'");
        }

        // Validate field type
        if (string.IsNullOrWhiteSpace(field.Type))
        {
            errors.Add($"Field '{fullName}' must have a type");
        }
        else if (!SearchFieldDataType.IsValid(field.Type))
        {
            errors.Add($"Field '{fullName}' has invalid type: '{field.Type}'");
        }

        // Validate vector field properties
        if (field.IsVector)
        {
            if (!field.Dimensions.HasValue || field.Dimensions <= 0)
            {
                errors.Add($"Vector field '{fullName}' must specify positive dimensions");
            }
            else if (field.Dimensions > 3072)
            {
                errors.Add($"Vector field '{fullName}' dimensions cannot exceed 3072");
            }
        }

        // Validate complex type fields
        if (field.IsComplex && field.Fields != null)
        {
            foreach (var subField in field.Fields)
            {
                errors.AddRange(ValidateField(subField, fieldNames, fullName));
            }
        }

        // Validate attribute compatibility with type
        if (field.Searchable == true && !SearchFieldDataType.SupportsSearchable(field.Type))
        {
            errors.Add($"Field '{fullName}' of type '{field.Type}' cannot be searchable");
        }

        if (field.Sortable == true && !SearchFieldDataType.SupportsSortable(field.Type))
        {
            errors.Add($"Field '{fullName}' of type '{field.Type}' cannot be sortable");
        }

        if (field.Facetable == true && !SearchFieldDataType.SupportsFacetable(field.Type))
        {
            errors.Add($"Field '{fullName}' of type '{field.Type}' cannot be facetable");
        }

        return errors;
    }
}
