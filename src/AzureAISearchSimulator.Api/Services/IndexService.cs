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
