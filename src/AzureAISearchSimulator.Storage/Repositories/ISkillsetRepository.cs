using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Storage.Repositories;

/// <summary>
/// Interface for skillset persistence.
/// </summary>
public interface ISkillsetRepository
{
    Task<Skillset> CreateAsync(Skillset skillset);
    Task<Skillset?> GetByNameAsync(string name);
    Task<IEnumerable<Skillset>> GetAllAsync();
    Task<Skillset> UpdateAsync(Skillset skillset);
    Task<bool> DeleteAsync(string name);
    Task<bool> ExistsAsync(string name);
}
