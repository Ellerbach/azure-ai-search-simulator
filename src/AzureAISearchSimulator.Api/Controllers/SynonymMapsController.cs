using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Api.Services.Authorization;
using AzureAISearchSimulator.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for synonym map management operations.
/// Implements the Azure AI Search synonym map REST API.
/// </summary>
[ApiController]
[Route("synonymmaps")]
[Produces("application/json")]
public class SynonymMapsController : ControllerBase
{
    private readonly ISynonymMapService _synonymMapService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<SynonymMapsController> _logger;

    public SynonymMapsController(
        ISynonymMapService synonymMapService,
        IAuthorizationService authorizationService,
        ILogger<SynonymMapsController> logger)
    {
        _synonymMapService = synonymMapService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new synonym map.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SynonymMap), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSynonymMap(
        [FromBody] SynonymMap synonymMap,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.CreateSynonymMap);
        if (authResult != null) return authResult;

        try
        {
            var result = await _synonymMapService.CreateAsync(synonymMap, cancellationToken);
            return CreatedAtAction(nameof(GetSynonymMap), new { synonymMapName = result.Name }, result);
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
    /// Gets a synonym map by name.
    /// </summary>
    [HttpGet("{synonymMapName}")]
    [ProducesResponseType(typeof(SynonymMap), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSynonymMap(
        string synonymMapName,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.GetSynonymMap);
        if (authResult != null) return authResult;

        var synonymMap = await _synonymMapService.GetAsync(synonymMapName, cancellationToken);

        if (synonymMap == null)
        {
            return NotFound(new { error = new { code = "ResourceNotFound", message = $"Synonym map '{synonymMapName}' not found" } });
        }

        // Add ETag header
        if (!string.IsNullOrEmpty(synonymMap.ETag))
        {
            Response.Headers.ETag = synonymMap.ETag;
        }

        return Ok(synonymMap);
    }

    /// <summary>
    /// Lists all synonym maps.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SynonymMapListResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSynonymMaps(
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.ListSynonymMaps);
        if (authResult != null) return authResult;

        var synonymMaps = await _synonymMapService.ListAsync(cancellationToken);

        return Ok(new SynonymMapListResult
        {
            Value = synonymMaps.ToList()
        });
    }

    /// <summary>
    /// Creates or updates a synonym map.
    /// </summary>
    [HttpPut("{synonymMapName}")]
    [ProducesResponseType(typeof(SynonymMap), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SynonymMap), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateOrUpdateSynonymMap(
        string synonymMapName,
        [FromBody] SynonymMap synonymMap,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.UpdateSynonymMap);
        if (authResult != null) return authResult;

        try
        {
            var exists = await _synonymMapService.ExistsAsync(synonymMapName, cancellationToken);
            var result = await _synonymMapService.CreateOrUpdateAsync(synonymMapName, synonymMap, cancellationToken);

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
                return CreatedAtAction(nameof(GetSynonymMap), new { synonymMapName = result.Name }, result);
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { code = "InvalidArgument", message = ex.Message } });
        }
    }

    /// <summary>
    /// Deletes a synonym map.
    /// </summary>
    [HttpDelete("{synonymMapName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSynonymMap(
        string synonymMapName,
        [FromQuery(Name = "api-version")] string? apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.DeleteSynonymMap);
        if (authResult != null) return authResult;

        var deleted = await _synonymMapService.DeleteAsync(synonymMapName, cancellationToken);

        if (!deleted)
        {
            return NotFound(new { error = new { code = "ResourceNotFound", message = $"Synonym map '{synonymMapName}' not found" } });
        }

        return NoContent();
    }
}

/// <summary>
/// Response model for listing synonym maps.
/// </summary>
public class SynonymMapListResult
{
    public List<SynonymMap> Value { get; set; } = new();
}
