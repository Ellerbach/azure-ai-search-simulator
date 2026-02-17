using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using AzureAISearchSimulator.Search.Hnsw;
using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Api.Services.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for service-level statistics.
/// </summary>
[ApiController]
[Route("servicestats")]
[Produces("application/json")]
public class ServiceStatsController : ControllerBase
{
    // Default quotas matching Azure AI Search Standard (S1) tier
    private const long DefaultIndexesQuota = 15;
    private const long DefaultIndexersQuota = 15;
    private const long DefaultDataSourcesQuota = 15;
    private const long DefaultSynonymMapsQuota = 3;
    private const long DefaultSkillsetQuota = 15;
    private const long DefaultStorageSizeQuota = 16_106_127_360;       // ~15 GB (S1)
    private const long DefaultVectorIndexSizeQuota = 5_368_709_120;    // 5 GB (S1)
    private const long DefaultMaxStoragePerIndex = 16_106_127_360;     // ~15 GB (S1)
    private const int DefaultMaxFieldsPerIndex = 1000;
    private const int DefaultMaxFieldNestingDepthPerIndex = 10;
    private const int DefaultMaxComplexCollectionFieldsPerIndex = 40;
    private const int DefaultMaxComplexObjectsInCollectionsPerDocument = 3000;

    private readonly IIndexService _indexService;
    private readonly IDocumentService _documentService;
    private readonly IIndexerService _indexerService;
    private readonly IDataSourceService _dataSourceService;
    private readonly ISkillsetService _skillsetService;
    private readonly LuceneIndexManager _luceneManager;
    private readonly IHnswIndexManager _hnswManager;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<ServiceStatsController> _logger;

    public ServiceStatsController(
        IIndexService indexService,
        IDocumentService documentService,
        IIndexerService indexerService,
        IDataSourceService dataSourceService,
        ISkillsetService skillsetService,
        LuceneIndexManager luceneManager,
        IHnswIndexManager hnswManager,
        IAuthorizationService authorizationService,
        ILogger<ServiceStatsController> logger)
    {
        _indexService = indexService;
        _documentService = documentService;
        _indexerService = indexerService;
        _dataSourceService = dataSourceService;
        _skillsetService = skillsetService;
        _luceneManager = luceneManager;
        _hnswManager = hnswManager;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets service-level statistics including resource counters and limits.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ServiceStatistics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetServiceStatistics(
        [FromQuery(Name = "api-version")] string apiVersion,
        CancellationToken cancellationToken)
    {
        var authResult = this.CheckAuthorization(_authorizationService, SearchOperation.GetServiceStatistics);
        if (authResult != null) return authResult;

        // Gather usage counts
        var indexes = await _indexService.ListIndexesAsync(cancellationToken);
        var indexList = indexes.ToList();
        var indexers = await _indexerService.ListAsync();
        var dataSources = await _dataSourceService.ListAsync();
        var skillsets = await _skillsetService.ListAsync(cancellationToken);

        // Sum document counts and storage sizes across all indexes
        long totalDocumentCount = 0;
        long totalStorageSize = 0;
        long totalVectorIndexSize = 0;

        foreach (var index in indexList)
        {
            totalDocumentCount += await _documentService.GetDocumentCountAsync(index.Name!);
            totalStorageSize += _luceneManager.GetStorageSize(index.Name!);
            totalVectorIndexSize += _hnswManager.GetVectorIndexSize(index.Name!);
        }

        var stats = new ServiceStatistics
        {
            ODataContext = $"{Request.Scheme}://{Request.Host}/$metadata#Microsoft.Azure.Search.V2024_07_01.ServiceStatistics",
            Counters = new ServiceCounters
            {
                DocumentCount = new ResourceCounter
                {
                    Usage = totalDocumentCount,
                    Quota = null // No quota for document count (same as Azure Standard tier)
                },
                IndexesCount = new ResourceCounter
                {
                    Usage = indexList.Count,
                    Quota = DefaultIndexesQuota
                },
                IndexersCount = new ResourceCounter
                {
                    Usage = indexers.Count(),
                    Quota = DefaultIndexersQuota
                },
                DataSourcesCount = new ResourceCounter
                {
                    Usage = dataSources.Count(),
                    Quota = DefaultDataSourcesQuota
                },
                StorageSize = new ResourceCounter
                {
                    Usage = totalStorageSize,
                    Quota = DefaultStorageSizeQuota
                },
                SynonymMaps = new ResourceCounter
                {
                    Usage = 0, // Synonym maps not yet implemented
                    Quota = DefaultSynonymMapsQuota
                },
                SkillsetCount = new ResourceCounter
                {
                    Usage = skillsets.Count(),
                    Quota = DefaultSkillsetQuota
                },
                VectorIndexSize = new ResourceCounter
                {
                    Usage = totalVectorIndexSize,
                    Quota = DefaultVectorIndexSizeQuota
                }
            },
            Limits = new ServiceLimits
            {
                MaxStoragePerIndex = DefaultMaxStoragePerIndex,
                MaxFieldsPerIndex = DefaultMaxFieldsPerIndex,
                MaxFieldNestingDepthPerIndex = DefaultMaxFieldNestingDepthPerIndex,
                MaxComplexCollectionFieldsPerIndex = DefaultMaxComplexCollectionFieldsPerIndex,
                MaxComplexObjectsInCollectionsPerDocument = DefaultMaxComplexObjectsInCollectionsPerDocument
            }
        };

        return Ok(stats);
    }
}
