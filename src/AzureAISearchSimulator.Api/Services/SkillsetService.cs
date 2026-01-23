using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Service for managing skillsets.
/// </summary>
public class SkillsetService : ISkillsetService
{
    private readonly ISkillsetRepository _repository;
    private readonly ILogger<SkillsetService> _logger;

    // Known skill types supported by the simulator
    private static readonly HashSet<string> SupportedSkillTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "#Microsoft.Skills.Text.SplitSkill",
        "#Microsoft.Skills.Text.MergeSkill",
        "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
        "#Microsoft.Skills.Util.ShaperSkill",
        "#Microsoft.Skills.Util.ConditionalSkill",
        "#Microsoft.Skills.Custom.WebApiSkill"
    };

    public SkillsetService(
        ISkillsetRepository repository,
        ILogger<SkillsetService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Skillset> CreateAsync(Skillset skillset, CancellationToken cancellationToken = default)
    {
        ValidateSkillset(skillset);

        if (await _repository.ExistsAsync(skillset.Name))
        {
            throw new InvalidOperationException($"Skillset '{skillset.Name}' already exists");
        }

        _logger.LogInformation("Creating skillset '{Name}' with {SkillCount} skills",
            skillset.Name, skillset.Skills.Count);

        return await _repository.CreateAsync(skillset);
    }

    public async Task<Skillset?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByNameAsync(name);
    }

    public async Task<IEnumerable<Skillset>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Skillset> UpdateAsync(string name, Skillset skillset, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(name))
        {
            throw new InvalidOperationException($"Skillset '{name}' not found");
        }

        skillset.Name = name; // Ensure name matches
        ValidateSkillset(skillset);

        _logger.LogInformation("Updating skillset '{Name}'", name);

        return await _repository.UpdateAsync(skillset);
    }

    public async Task<Skillset> CreateOrUpdateAsync(string name, Skillset skillset, CancellationToken cancellationToken = default)
    {
        skillset.Name = name;
        ValidateSkillset(skillset);

        if (await _repository.ExistsAsync(name))
        {
            _logger.LogInformation("Updating existing skillset '{Name}'", name);
            return await _repository.UpdateAsync(skillset);
        }
        else
        {
            _logger.LogInformation("Creating new skillset '{Name}'", name);
            return await _repository.CreateAsync(skillset);
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting skillset '{Name}'", name);
        return await _repository.DeleteAsync(name);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _repository.ExistsAsync(name);
    }

    private void ValidateSkillset(Skillset skillset)
    {
        if (string.IsNullOrWhiteSpace(skillset.Name))
        {
            throw new ArgumentException("Skillset name is required");
        }

        if (skillset.Name.Length > 128)
        {
            throw new ArgumentException("Skillset name cannot exceed 128 characters");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(skillset.Name, @"^[a-zA-Z][a-zA-Z0-9\-]*$"))
        {
            throw new ArgumentException("Skillset name must start with a letter and contain only letters, numbers, and hyphens");
        }

        var warnings = new List<string>();

        foreach (var skill in skillset.Skills)
        {
            ValidateSkill(skill, warnings);
        }

        if (warnings.Count > 0)
        {
            _logger.LogWarning("Skillset '{Name}' validation warnings: {Warnings}",
                skillset.Name, string.Join("; ", warnings));
        }
    }

    private void ValidateSkill(Skill skill, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(skill.ODataType))
        {
            throw new ArgumentException("Skill @odata.type is required");
        }

        if (!SupportedSkillTypes.Contains(skill.ODataType))
        {
            warnings.Add($"Skill type '{skill.ODataType}' may not be fully supported by the simulator");
        }

        if (skill.Inputs.Count == 0)
        {
            warnings.Add($"Skill '{skill.Name ?? skill.ODataType}' has no inputs defined");
        }

        if (skill.Outputs.Count == 0)
        {
            warnings.Add($"Skill '{skill.Name ?? skill.ODataType}' has no outputs defined");
        }

        // Validate skill-specific requirements
        switch (skill.ODataType)
        {
            case "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill":
                if (string.IsNullOrEmpty(skill.ResourceUri))
                {
                    throw new ArgumentException("AzureOpenAIEmbeddingSkill requires 'resourceUri' property");
                }
                if (string.IsNullOrEmpty(skill.DeploymentId))
                {
                    throw new ArgumentException("AzureOpenAIEmbeddingSkill requires 'deploymentId' property");
                }
                break;

            case "#Microsoft.Skills.Custom.WebApiSkill":
                if (string.IsNullOrEmpty(skill.Uri))
                {
                    throw new ArgumentException("WebApiSkill requires 'uri' property");
                }
                break;
        }
    }
}
