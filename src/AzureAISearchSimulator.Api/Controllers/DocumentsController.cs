using Microsoft.AspNetCore.Mvc;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Api.Services.Authorization;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for document operations within a search index.
/// </summary>
[ApiController]
[Route("indexes/{indexName}/docs")]
[Route("indexes('{indexName}')/docs")] // OData entity syntax for Azure SDK compatibility
public class DocumentsController : ControllerBase
{
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDocumentService _documentService;
    private readonly ISearchService _searchService;
    private readonly IAuthorizationService _authorizationService;

    public DocumentsController(
        ILogger<DocumentsController> logger,
        IDocumentService documentService,
        ISearchService searchService,
        IAuthorizationService authorizationService)
    {
        _logger = logger;
        _documentService = documentService;
        _searchService = searchService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Upload, merge, or delete documents in an index.
    /// POST indexes/{indexName}/docs/index
    /// </summary>
    [HttpPost("index")]
    [HttpPost("search.index")] // Azure SDK uses this format
    [ProducesResponseType(typeof(IndexDocumentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IndexDocumentsResponse), StatusCodes.Status207MultiStatus)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IndexDocuments(
        string indexName,
        [FromBody] IndexDocumentsRequest request,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - document operations require IndexDataContributor
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.UploadDocuments);
        if (authResult != null) return authResult;

        if (request.Value == null || !request.Value.Any())
        {
            return BadRequest(new { error = new { message = "Request must contain at least one document action" } });
        }

        try
        {
            var response = await _documentService.IndexDocumentsAsync(indexName, request);

            // Return 207 if some operations failed
            var hasFailures = response.Value.Any(r => !r.Status);
            if (hasFailures)
            {
                return StatusCode(StatusCodes.Status207MultiStatus, response);
            }

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing documents to {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Search documents in an index.
    /// POST indexes/{indexName}/docs/search
    /// </summary>
    [HttpPost("search")]
    [HttpPost("search.post")] // Azure SDK may use this format
    [HttpPost("search.post.search")] // Azure SDK uses this format for search
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Search(
        string indexName,
        [FromBody] SearchRequest request,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - search requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Search);
        if (authResult != null) return authResult;

        try
        {
            var response = await _searchService.SearchAsync(indexName, request);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching index {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Search documents using GET (query string parameters).
    /// GET indexes/{indexName}/docs
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchGet(
        string indexName,
        [FromQuery] string? search = null,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$orderby")] string? orderby = null,
        [FromQuery(Name = "$top")] int? top = null,
        [FromQuery(Name = "$skip")] int? skip = null,
        [FromQuery(Name = "$count")] bool? count = null,
        [FromQuery] string? highlight = null,
        [FromQuery] string? searchMode = null,
        [FromQuery] string? queryType = null,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - search requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Search);
        if (authResult != null) return authResult;

        var request = new SearchRequest
        {
            Search = search ?? "*",
            Filter = filter,
            Select = select,
            OrderBy = orderby,
            Top = top,
            Skip = skip,
            Count = count,
            Highlight = highlight,
            SearchMode = searchMode ?? "any",
            QueryType = queryType ?? "simple"
        };

        try
        {
            var response = await _searchService.SearchAsync(indexName, request);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching index {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Get a document by key.
    /// GET indexes/{indexName}/docs/{key}
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(
        string indexName,
        string key,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - lookup requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Lookup);
        if (authResult != null) return authResult;

        try
        {
            var selectedFields = string.IsNullOrWhiteSpace(select)
                ? null
                : select.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

            var document = await _documentService.GetDocumentAsync(indexName, key, selectedFields);
            if (document == null)
            {
                return NotFound(new { error = new { message = $"Document with key '{key}' not found" } });
            }

            return Ok(document);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document {Key} from {IndexName}", key, indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Get document count in an index.
    /// GET indexes/{indexName}/docs/$count
    /// </summary>
    [HttpGet("$count")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentCount(
        string indexName,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - count requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Count);
        if (authResult != null) return authResult;

        try
        {
            var count = await _documentService.GetDocumentCountAsync(indexName);
            return Ok(count);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document count for {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Get suggestions for search queries.
    /// POST indexes/{indexName}/docs/suggest
    /// </summary>
    [HttpPost("suggest")]
    [ProducesResponseType(typeof(SuggestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Suggest(
        string indexName,
        [FromBody] SuggestRequest request,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - suggest requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Suggest);
        if (authResult != null) return authResult;

        try
        {
            var response = await _searchService.SuggestAsync(indexName, request);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suggestions for {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }

    /// <summary>
    /// Get autocomplete suggestions.
    /// POST indexes/{indexName}/docs/autocomplete
    /// </summary>
    [HttpPost("autocomplete")]
    [ProducesResponseType(typeof(AutocompleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Autocomplete(
        string indexName,
        [FromBody] AutocompleteRequest request,
        [FromQuery(Name = "api-version")] string? apiVersion = null)
    {
        // Check authorization - autocomplete requires IndexDataReader
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.Autocomplete);
        if (authResult != null) return authResult;

        try
        {
            var response = await _searchService.AutocompleteAsync(indexName, request);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = new { message = $"Index '{indexName}' not found" } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting autocomplete for {IndexName}", indexName);
            return StatusCode(500, new { error = new { message = ex.Message } });
        }
    }
}
