using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for managing data sources.
/// </summary>
[ApiController]
[Route("datasources")]
[Produces("application/json")]
public class DataSourcesController : ControllerBase
{
    private readonly IDataSourceService _dataSourceService;
    private readonly ILogger<DataSourcesController> _logger;

    public DataSourcesController(
        IDataSourceService dataSourceService,
        ILogger<DataSourcesController> logger)
    {
        _dataSourceService = dataSourceService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new data source.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DataSource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] DataSource dataSource)
    {
        try
        {
            var created = await _dataSourceService.CreateAsync(dataSource);
            return CreatedAtAction(nameof(Get), new { dataSourceName = created.Name }, created);
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
    /// Creates or updates a data source.
    /// </summary>
    [HttpPut("{dataSourceName}")]
    [ProducesResponseType(typeof(DataSource), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataSource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrUpdate(
        string dataSourceName, 
        [FromBody] DataSource dataSource)
    {
        try
        {
            var exists = await _dataSourceService.ExistsAsync(dataSourceName);
            var result = await _dataSourceService.CreateOrUpdateAsync(dataSourceName, dataSource);
            
            if (exists)
            {
                return Ok(result);
            }
            return CreatedAtAction(nameof(Get), new { dataSourceName = result.Name }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a data source by name.
    /// </summary>
    [HttpGet("{dataSourceName}")]
    [ProducesResponseType(typeof(DataSource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string dataSourceName)
    {
        var dataSource = await _dataSourceService.GetAsync(dataSourceName);
        if (dataSource == null)
        {
            return NotFound(new { error = $"Data source '{dataSourceName}' not found." });
        }
        return Ok(dataSource);
    }

    /// <summary>
    /// Lists all data sources.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ODataResponse<DataSource>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var dataSources = await _dataSourceService.ListAsync();
        return Ok(new ODataResponse<DataSource>
        {
            Value = dataSources.ToList()
        });
    }

    /// <summary>
    /// Deletes a data source.
    /// </summary>
    [HttpDelete("{dataSourceName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string dataSourceName)
    {
        var deleted = await _dataSourceService.DeleteAsync(dataSourceName);
        if (!deleted)
        {
            return NotFound(new { error = $"Data source '{dataSourceName}' not found." });
        }
        return NoContent();
    }
}
