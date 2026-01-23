using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Services;

/// <summary>
/// Service interface for managing search indexes.
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// Creates a new search index.
    /// </summary>
    Task<SearchIndex> CreateIndexAsync(SearchIndex index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a search index.
    /// </summary>
    Task<SearchIndex> CreateOrUpdateIndexAsync(string indexName, SearchIndex index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a search index by name.
    /// </summary>
    Task<SearchIndex?> GetIndexAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all search indexes.
    /// </summary>
    Task<IReadOnlyList<SearchIndex>> ListIndexesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a search index.
    /// </summary>
    Task<bool> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an index exists.
    /// </summary>
    Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an index definition.
    /// </summary>
    IndexValidationResult ValidateIndex(SearchIndex index);
}

/// <summary>
/// Result of index validation.
/// </summary>
public class IndexValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();

    public static IndexValidationResult Success() => new() { IsValid = true };
    
    public static IndexValidationResult Failure(params string[] errors) => new() 
    { 
        IsValid = false, 
        Errors = errors.ToList() 
    };

    public static IndexValidationResult Failure(IEnumerable<string> errors) => new() 
    { 
        IsValid = false, 
        Errors = errors.ToList() 
    };
}
