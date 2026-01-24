using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for managing indexers.
/// </summary>
[ApiController]
[Route("indexers")]
[Produces("application/json")]
public class IndexersController : ControllerBase
{
    private readonly IIndexerService _indexerService;
    private readonly ILogger<IndexersController> _logger;

    public IndexersController(
        IIndexerService indexerService,
        ILogger<IndexersController> logger)
    {
        _indexerService = indexerService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new indexer.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Indexer), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] Indexer indexer)
    {
        try
        {
            var created = await _indexerService.CreateAsync(indexer);
            return CreatedAtAction(nameof(Get), new { indexerName = created.Name }, created);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Creates or updates an indexer.
    /// </summary>
    [HttpPut("{indexerName}")]
    [ProducesResponseType(typeof(Indexer), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Indexer), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrUpdate(
        string indexerName, 
        [FromBody] Indexer indexer)
    {
        try
        {
            var exists = await _indexerService.ExistsAsync(indexerName);
            var result = await _indexerService.CreateOrUpdateAsync(indexerName, indexer);
            
            if (exists)
            {
                return Ok(result);
            }
            return CreatedAtAction(nameof(Get), new { indexerName = result.Name }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets an indexer by name.
    /// </summary>
    [HttpGet("{indexerName}")]
    [ProducesResponseType(typeof(Indexer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string indexerName)
    {
        _logger.LogInformation("IndexersController.Get called with: {IndexerName}", indexerName);
        var indexer = await _indexerService.GetAsync(indexerName);
        _logger.LogInformation("IndexersController.Get result for {IndexerName}: {Found}", indexerName, indexer != null);
        if (indexer == null)
        {
            return NotFound(new { error = $"Indexer '{indexerName}' not found." });
        }
        return Ok(indexer);
    }

    /// <summary>
    /// Lists all indexers.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ODataResponse<Indexer>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var indexers = await _indexerService.ListAsync();
        return Ok(new ODataResponse<Indexer>
        {
            Value = indexers.ToList()
        });
    }

    /// <summary>
    /// Deletes an indexer.
    /// </summary>
    [HttpDelete("{indexerName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string indexerName)
    {
        var deleted = await _indexerService.DeleteAsync(indexerName);
        if (!deleted)
        {
            return NotFound(new { error = $"Indexer '{indexerName}' not found." });
        }
        return NoContent();
    }

    /// <summary>
    /// Gets the status of an indexer.
    /// </summary>
    [HttpGet("{indexerName}/status")]
    [HttpGet("{indexerName}/search.status")] // Azure SDK uses this format
    [ProducesResponseType(typeof(IndexerStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(string indexerName)
    {
        if (!await _indexerService.ExistsAsync(indexerName))
        {
            return NotFound(new { error = $"Indexer '{indexerName}' not found." });
        }

        var status = await _indexerService.GetStatusAsync(indexerName);
        return Ok(status);
    }

    /// <summary>
    /// Runs an indexer immediately.
    /// </summary>
    [HttpPost("{indexerName}/run")]
    [HttpPost("{indexerName}/search.run")] // Azure SDK uses this format
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Run(string indexerName)
    {
        try
        {
            if (!await _indexerService.ExistsAsync(indexerName))
            {
                return NotFound(new { error = $"Indexer '{indexerName}' not found." });
            }

            // Run synchronously for simplicity (real Azure runs async)
            await _indexerService.RunAsync(indexerName);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Resets an indexer (clears tracking state).
    /// </summary>
    [HttpPost("{indexerName}/reset")]
    [HttpPost("{indexerName}/search.reset")] // Azure SDK uses this format
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(string indexerName)
    {
        try
        {
            if (!await _indexerService.ExistsAsync(indexerName))
            {
                return NotFound(new { error = $"Indexer '{indexerName}' not found." });
            }

            await _indexerService.ResetAsync(indexerName);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
