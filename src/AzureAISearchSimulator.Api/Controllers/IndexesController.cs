using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for managing search indexes.
/// </summary>
[ApiController]
[Route("indexes")]
[Produces("application/json")]
public class IndexesController : ControllerBase
{
    private readonly IIndexService _indexService;
    private readonly IDocumentService _documentService;
    private readonly LuceneIndexManager _luceneManager;
    private readonly IHnswIndexManager _hnswManager;
    private readonly ILogger<IndexesController> _logger;

    public IndexesController(
        IIndexService indexService,
        IDocumentService documentService,
        LuceneIndexManager luceneManager,
        IHnswIndexManager hnswManager,
        ILogger<IndexesController> logger)
    {
        _indexService = indexService;
        _documentService = documentService;
        _luceneManager = luceneManager;
        _hnswManager = hnswManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new search index.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SearchIndex), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateIndex(
        [FromBody] SearchIndex index,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _indexService.CreateIndexAsync(index, cancellationToken);
            return CreatedAtAction(
                nameof(GetIndex), 
                new { indexName = created.Name, apiVersion }, 
                created);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(CreateError("IndexAlreadyExists", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateError("InvalidRequest", ex.Message));
        }
    }

    /// <summary>
    /// Gets a search index by name.
    /// </summary>
    [HttpGet("{indexName}")]
    [HttpGet("('{indexName}')")] // OData entity syntax for Azure SDK compatibility
    [ProducesResponseType(typeof(SearchIndex), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIndex(
        string indexName,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var index = await _indexService.GetIndexAsync(indexName, cancellationToken);
        if (index == null)
        {
            return NotFound(CreateError("IndexNotFound", $"Index '{indexName}' not found"));
        }

        // Add OData context
        Response.Headers["ETag"] = index.ETag;
        return Ok(index);
    }

    /// <summary>
    /// Lists all search indexes.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ODataResponse<SearchIndex>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIndexes(
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var indexes = await _indexService.ListIndexesAsync(cancellationToken);
        
        var response = new ODataResponse<SearchIndex>
        {
            ODataContext = $"{Request.Scheme}://{Request.Host}/$metadata#indexes",
            Value = indexes.ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates or updates a search index.
    /// </summary>
    [HttpPut("{indexName}")]
    [HttpPut("('{indexName}')")] // OData entity syntax for Azure SDK compatibility
    [ProducesResponseType(typeof(SearchIndex), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SearchIndex), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateOrUpdateIndex(
        string indexName,
        [FromBody] SearchIndex index,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var existed = await _indexService.IndexExistsAsync(indexName, cancellationToken);
            var result = await _indexService.CreateOrUpdateIndexAsync(indexName, index, cancellationToken);

            Response.Headers["ETag"] = result.ETag;

            if (existed)
            {
                return Ok(result);
            }
            return CreatedAtAction(
                nameof(GetIndex), 
                new { indexName = result.Name, apiVersion }, 
                result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(CreateError("InvalidRequest", ex.Message));
        }
    }

    /// <summary>
    /// Deletes a search index.
    /// </summary>
    [HttpDelete("{indexName}")]
    [HttpDelete("('{indexName}')")] // OData entity syntax for Azure SDK compatibility
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteIndex(
        string indexName,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var deleted = await _indexService.DeleteIndexAsync(indexName, cancellationToken);
        if (!deleted)
        {
            return NotFound(CreateError("IndexNotFound", $"Index '{indexName}' not found"));
        }

        return NoContent();
    }

    /// <summary>
    /// Analyzes text with the specified analyzer.
    /// </summary>
    [HttpPost("{indexName}/analyze")]
    [ProducesResponseType(typeof(AnalyzeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AnalyzeText(
        string indexName,
        [FromBody] AnalyzeRequest request,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var index = await _indexService.GetIndexAsync(indexName, cancellationToken);
        if (index == null)
        {
            return NotFound(CreateError("IndexNotFound", $"Index '{indexName}' not found"));
        }

        // TODO: Implement actual text analysis with Lucene
        // For now, return simple tokenization
        var tokens = request.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        
        var result = new AnalyzeResult
        {
            Tokens = tokens.Select((t, i) => new AnalyzeToken
            {
                Token = t.ToLowerInvariant(),
                StartOffset = 0,
                EndOffset = t.Length,
                Position = i
            }).ToList()
        };

        return Ok(result);
    }

    /// <summary>
    /// Gets statistics for a search index.
    /// </summary>
    [HttpGet("{indexName}/stats")]
    [ProducesResponseType(typeof(IndexStatistics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIndexStatistics(
        string indexName,
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var index = await _indexService.GetIndexAsync(indexName, cancellationToken);
        if (index == null)
        {
            return NotFound(CreateError("IndexNotFound", $"Index '{indexName}' not found"));
        }

        var documentCount = await _documentService.GetDocumentCountAsync(indexName);
        var storageSize = _luceneManager.GetStorageSize(indexName);
        var vectorIndexSize = _hnswManager.GetVectorIndexSize(indexName);

        var stats = new IndexStatistics
        {
            ODataContext = $"{Request.Scheme}://{Request.Host}/$metadata#Microsoft.Azure.Search.V2024_07_01.IndexStatistics",
            DocumentCount = documentCount,
            StorageSize = storageSize,
            VectorIndexSize = vectorIndexSize
        };

        return Ok(stats);
    }

    private static ODataError CreateError(string code, string message)
    {
        return new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = code,
                Message = message
            }
        };
    }
}

/// <summary>
/// Request for text analysis.
/// </summary>
public class AnalyzeRequest
{
    public string? Text { get; set; }
    public string? Analyzer { get; set; }
}

/// <summary>
/// Result of text analysis.
/// </summary>
public class AnalyzeResult
{
    public List<AnalyzeToken> Tokens { get; set; } = new();
}

/// <summary>
/// A single token from analysis.
/// </summary>
public class AnalyzeToken
{
    public string Token { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int Position { get; set; }
}

/// <summary>
/// Statistics for a search index.
/// </summary>
public class IndexStatistics
{
    /// <summary>
    /// OData context URL.
    /// </summary>
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    /// <summary>
    /// Number of documents in the index.
    /// </summary>
    [JsonPropertyName("documentCount")]
    public long DocumentCount { get; set; }

    /// <summary>
    /// Size of the index storage in bytes.
    /// </summary>
    [JsonPropertyName("storageSize")]
    public long StorageSize { get; set; }

    /// <summary>
    /// Size of the vector index storage in bytes.
    /// </summary>
    [JsonPropertyName("vectorIndexSize")]
    public long VectorIndexSize { get; set; }
}
