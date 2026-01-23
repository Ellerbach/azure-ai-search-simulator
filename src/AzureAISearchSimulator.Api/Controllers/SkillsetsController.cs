using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for skillset management operations.
/// </summary>
[ApiController]
[Route("skillsets")]
[Produces("application/json")]
public class SkillsetsController : ControllerBase
{
    private readonly ISkillsetService _skillsetService;
    private readonly ILogger<SkillsetsController> _logger;

    public SkillsetsController(
        ISkillsetService skillsetService,
        ILogger<SkillsetsController> logger)
    {
        _skillsetService = skillsetService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new skillset.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Skillset), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSkillset(
        [FromBody] Skillset skillset,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _skillsetService.CreateAsync(skillset, cancellationToken);
            return CreatedAtAction(nameof(GetSkillset), new { skillsetName = result.Name }, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { error = new { code = "ResourceAlreadyExists", message = ex.Message } });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { code = "InvalidArgument", message = ex.Message } });
        }
    }

    /// <summary>
    /// Gets a skillset by name.
    /// </summary>
    [HttpGet("{skillsetName}")]
    [ProducesResponseType(typeof(Skillset), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSkillset(
        string skillsetName,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var skillset = await _skillsetService.GetAsync(skillsetName, cancellationToken);
        
        if (skillset == null)
        {
            return NotFound(new { error = new { code = "ResourceNotFound", message = $"Skillset '{skillsetName}' not found" } });
        }

        // Add ETag header
        if (!string.IsNullOrEmpty(skillset.ETag))
        {
            Response.Headers.ETag = skillset.ETag;
        }

        return Ok(skillset);
    }

    /// <summary>
    /// Lists all skillsets.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SkillsetListResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSkillsets(
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var skillsets = await _skillsetService.ListAsync(cancellationToken);
        
        return Ok(new SkillsetListResult
        {
            Value = skillsets.ToList()
        });
    }

    /// <summary>
    /// Creates or updates a skillset.
    /// </summary>
    [HttpPut("{skillsetName}")]
    [ProducesResponseType(typeof(Skillset), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Skillset), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrUpdateSkillset(
        string skillsetName,
        [FromBody] Skillset skillset,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var exists = await _skillsetService.ExistsAsync(skillsetName, cancellationToken);
            var result = await _skillsetService.CreateOrUpdateAsync(skillsetName, skillset, cancellationToken);

            // Add ETag header
            if (!string.IsNullOrEmpty(result.ETag))
            {
                Response.Headers.ETag = result.ETag;
            }

            if (exists)
            {
                return Ok(result);
            }
            else
            {
                return CreatedAtAction(nameof(GetSkillset), new { skillsetName = result.Name }, result);
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { code = "InvalidArgument", message = ex.Message } });
        }
    }

    /// <summary>
    /// Deletes a skillset.
    /// </summary>
    [HttpDelete("{skillsetName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSkillset(
        string skillsetName,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var deleted = await _skillsetService.DeleteAsync(skillsetName, cancellationToken);
        
        if (!deleted)
        {
            return NotFound(new { error = new { code = "ResourceNotFound", message = $"Skillset '{skillsetName}' not found" } });
        }

        return NoContent();
    }
}

/// <summary>
/// Response model for listing skillsets.
/// </summary>
public class SkillsetListResult
{
    public List<Skillset> Value { get; set; } = new();
}
