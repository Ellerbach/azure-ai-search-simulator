using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Interface for skillset management operations.
/// </summary>
public interface ISkillsetService
{
    /// <summary>
    /// Creates a new skillset.
    /// </summary>
    Task<Skillset> CreateAsync(Skillset skillset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a skillset by name.
    /// </summary>
    Task<Skillset?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all skillsets.
    /// </summary>
    Task<IEnumerable<Skillset>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing skillset.
    /// </summary>
    Task<Skillset> UpdateAsync(string name, Skillset skillset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a skillset.
    /// </summary>
    Task<Skillset> CreateOrUpdateAsync(string name, Skillset skillset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a skillset.
    /// </summary>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a skillset exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);
}
