using System.Text.Json;
using System.Text.RegularExpressions;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.DataSources;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Skills;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Exception thrown when a document key contains invalid characters.
/// </summary>
public partial class InvalidDocumentKeyException : Exception
{
    public string Key { get; }
    public string? DocumentName { get; }
    public string? DataSourceName { get; }

    public InvalidDocumentKeyException(string key, string? documentName = null, string? dataSourceName = null)
        : base($"Invalid document key: '{key}'. Keys can only contain letters, digits, underscore (_), dash (-), or equal sign (=). Please see https://docs.microsoft.com/azure/search/search-howto-indexing-azure-blob-storage#DocumentKeys")
    {
        Key = key;
        DocumentName = documentName;
        DataSourceName = dataSourceName;
    }
}

/// <summary>
/// Service for managing indexers.
/// </summary>
public partial class IndexerService : IIndexerService
{
    // Regex pattern for valid document keys: letters, digits, underscore, dash, equal sign
    [GeneratedRegex(@"^[a-zA-Z0-9_\-=]+$")]
    private static partial Regex ValidKeyPattern();

    private readonly IIndexerRepository _repository;
    private readonly IDataSourceService _dataSourceService;
    private readonly ISkillsetService _skillsetService;
    private readonly IIndexService _indexService;
    private readonly IDataSourceConnectorFactory _connectorFactory;
    private readonly IDocumentCrackerFactory _documentCrackerFactory;
    private readonly ISkillPipeline _skillPipeline;
    private readonly IDocumentService _documentService;
    private readonly ILogger<IndexerService> _logger;
    private readonly DiagnosticLoggingSettings _diagnosticSettings;
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public IndexerService(
        IIndexerRepository repository,
        IDataSourceService dataSourceService,
        ISkillsetService skillsetService,
        IIndexService indexService,
        IDataSourceConnectorFactory connectorFactory,
        IDocumentCrackerFactory documentCrackerFactory,
        ISkillPipeline skillPipeline,
        IDocumentService documentService,
        ILogger<IndexerService> logger,
        IOptions<DiagnosticLoggingSettings> diagnosticSettings)
    {
        _repository = repository;
        _dataSourceService = dataSourceService;
        _skillsetService = skillsetService;
        _indexService = indexService;
        _connectorFactory = connectorFactory;
        _documentCrackerFactory = documentCrackerFactory;
        _skillPipeline = skillPipeline;
        _documentService = documentService;
        _logger = logger;
        _diagnosticSettings = diagnosticSettings.Value;
    }

    public async Task<Indexer> CreateAsync(Indexer indexer)
    {
        await ValidateIndexerAsync(indexer);

        if (await _repository.ExistsAsync(indexer.Name))
        {
            throw new InvalidOperationException($"Indexer '{indexer.Name}' already exists.");
        }

        _logger.LogInformation("Creating indexer: {Name} (data source: {DataSource}, target: {Target})", 
            indexer.Name, indexer.DataSourceName, indexer.TargetIndexName);

        return await _repository.CreateAsync(indexer);
    }

    public async Task<Indexer> CreateOrUpdateAsync(string name, Indexer indexer)
    {
        indexer.Name = name;
        await ValidateIndexerAsync(indexer);

        _logger.LogInformation("Creating or updating indexer: {Name}", indexer.Name);

        if (await _repository.ExistsAsync(name))
        {
            return await _repository.UpdateAsync(indexer);
        }

        return await _repository.CreateAsync(indexer);
    }

    public async Task<Indexer?> GetAsync(string name)
    {
        _logger.LogInformation("IndexerService.GetAsync called for: {Name}", name);
        var result = await _repository.GetAsync(name);
        _logger.LogInformation("IndexerService.GetAsync result for {Name}: {Found}", name, result != null);
        return result;
    }

    public async Task<IEnumerable<Indexer>> ListAsync()
    {
        return await _repository.ListAsync();
    }

    public async Task<bool> DeleteAsync(string name)
    {
        _logger.LogInformation("Deleting indexer: {Name}", name);
        return await _repository.DeleteAsync(name);
    }

    public async Task<bool> ExistsAsync(string name)
    {
        return await _repository.ExistsAsync(name);
    }

    public async Task<IndexerStatus> GetStatusAsync(string name)
    {
        var status = await _repository.GetStatusAsync(name);
        return status ?? new IndexerStatus();
    }

    public async Task RunAsync(string name)
    {
        var indexer = await _repository.GetAsync(name);
        if (indexer == null)
        {
            throw new InvalidOperationException($"Indexer '{name}' not found.");
        }

        if (indexer.Disabled == true)
        {
            throw new InvalidOperationException($"Indexer '{name}' is disabled.");
        }

        _logger.LogInformation("Running indexer: {Name}", name);

        var status = await _repository.GetStatusAsync(name) ?? new IndexerStatus();
        var executionResult = new IndexerExecutionResult
        {
            Status = IndexerExecutionStatus.InProgress,
            StartTime = DateTimeOffset.UtcNow
        };

        status.Status = IndexerStatusValue.Running;
        status.LastResult = executionResult;
        await _repository.SaveStatusAsync(name, status);

        try
        {
            // Get data source and connector
            var dataSource = await _dataSourceService.GetAsync(indexer.DataSourceName);
            if (dataSource == null)
            {
                throw new InvalidOperationException($"Data source '{indexer.DataSourceName}' not found.");
            }

            var connector = _connectorFactory.GetConnector(dataSource.Type);

            // Get tracking state for incremental indexing
            var trackingState = status.LastResult?.FinalTrackingState;
            executionResult.InitialTrackingState = trackingState;

            // Fetch document metadata from data source (content is NOT downloaded yet)
            var documents = await connector.ListDocumentsAsync(dataSource, trackingState);
            var documentList = documents.ToList();

            _logger.LogInformation("Indexer {Name} found {Count} documents to process", 
                name, documentList.Count);

            // Process documents in batches
            var processedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var batchSize = indexer.Parameters?.BatchSize ?? 1000;
            var maxFailedItems = indexer.Parameters?.MaxFailedItems ?? -1;
            var maxFailedItemsPerBatch = indexer.Parameters?.MaxFailedItemsPerBatch ?? -1;
            // Concurrency limit for parallel document preparation within a batch
            var maxParallelism = Math.Min(Environment.ProcessorCount, batchSize);

            foreach (var batch in documentList.Chunk(batchSize))
            {
                var batchFailedCount = 0; // Reset per-batch failure counter
                var batchStart = DateTime.UtcNow;

                _logger.LogDebug("Processing batch of {Count} documents (batch size: {BatchSize})", 
                    batch.Length, batchSize);

                // Phase 1: Prepare all documents in parallel (download, crack, enrich, map fields)
                var preparedResults = await PrepareDocumentBatchAsync(
                    indexer, batch, maxParallelism, connector, dataSource);

                // Phase 2: Collect successes and failures, then bulk upload
                var batchActions = new List<IndexAction>();

                foreach (var result in preparedResults)
                {
                    if (result.Skipped)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (result.Success)
                    {
                        batchActions.AddRange(result.Actions);
                    }
                    else
                    {
                        failedCount++;
                        batchFailedCount++;

                        executionResult.Errors.Add(result.Error!);

                        // Check per-batch failure limit
                        if (maxFailedItemsPerBatch >= 0 && batchFailedCount > maxFailedItemsPerBatch)
                        {
                            _logger.LogWarning(
                                "Batch failure limit ({Limit}) exceeded, skipping rest of batch",
                                maxFailedItemsPerBatch);
                            break;
                        }

                        // Check global failure limit
                        if (maxFailedItems >= 0 && failedCount > maxFailedItems)
                        {
                            throw new InvalidOperationException(
                                $"Maximum failed items ({maxFailedItems}) exceeded.");
                        }
                    }
                }

                // Phase 3: Bulk upload all successful documents in a single call
                if (batchActions.Count > 0)
                {
                    try
                    {
                        var request = new IndexDocumentsRequest { Value = batchActions };
                        await _documentService.IndexDocumentsAsync(indexer.TargetIndexName, request);
                        processedCount += batchActions.Count;

                        _logger.LogDebug(
                            "Bulk indexed {Count} documents in {Duration}ms",
                            batchActions.Count, (DateTime.UtcNow - batchStart).TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        // If the bulk upload fails, count all documents in the batch as failed
                        _logger.LogError(ex, "Bulk upload failed for batch of {Count} documents", batchActions.Count);
                        failedCount += batchActions.Count;
                        
                        executionResult.Errors.Add(new IndexerExecutionError
                        {
                            Key = "(batch)",
                            ErrorMessage = $"Bulk upload failed: {ex.Message}",
                            StatusCode = 500,
                            Name = $"BulkUpload.{indexer.TargetIndexName}"
                        });

                        if (maxFailedItems >= 0 && failedCount > maxFailedItems)
                        {
                            throw new InvalidOperationException(
                                $"Maximum failed items ({maxFailedItems}) exceeded.");
                        }
                    }
                }

                // Live progress update after each batch
                executionResult.ItemsProcessed = processedCount;
                executionResult.ItemsFailed = failedCount;
                await _repository.SaveStatusAsync(name, status);
            }

            // Update execution result
            executionResult.Status = failedCount > 0 
                ? IndexerExecutionStatus.TransientFailure 
                : IndexerExecutionStatus.Success;
            executionResult.EndTime = DateTimeOffset.UtcNow;
            executionResult.ItemsProcessed = processedCount;
            executionResult.ItemsFailed = failedCount;
            executionResult.FinalTrackingState = DateTimeOffset.UtcNow.ToString("O");

            _logger.LogInformation(
                "Indexer {Name} completed: {Processed} processed, {Failed} failed, {Skipped} skipped (unchanged)", 
                name, processedCount, failedCount, skippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexer {Name} failed", name);
            
            executionResult.Status = IndexerExecutionStatus.TransientFailure;
            executionResult.EndTime = DateTimeOffset.UtcNow;
            executionResult.ErrorMessage = ex.Message;
        }

        // Update status
        status.Status = executionResult.Status == IndexerExecutionStatus.Success 
            ? IndexerStatusValue.Unknown 
            : IndexerStatusValue.Error;
        status.LastResult = executionResult;
        
        // Keep last 10 executions in history
        status.ExecutionHistory.Insert(0, executionResult);
        if (status.ExecutionHistory.Count > 10)
        {
            status.ExecutionHistory = status.ExecutionHistory.Take(10).ToList();
        }

        await _repository.SaveStatusAsync(name, status);
    }

    /// <summary>
    /// Result of preparing a single source document for indexing.
    /// Contains one or more IndexActions (e.g., JSON array parsing produces multiple actions).
    /// </summary>
    private class DocumentPrepareResult
    {
        public bool Success { get; init; }
        public bool Skipped { get; init; }
        public List<IndexAction> Actions { get; init; } = new();
        public IndexerExecutionError? Error { get; init; }

        public static DocumentPrepareResult Ok(IndexAction action) =>
            new() { Success = true, Actions = [action] };

        public static DocumentPrepareResult Ok(List<IndexAction> actions) =>
            new() { Success = true, Actions = actions };

        public static DocumentPrepareResult Fail(IndexerExecutionError error) =>
            new() { Success = false, Error = error };

        public static DocumentPrepareResult Skip() =>
            new() { Success = true, Skipped = true };
    }

    /// <summary>
    /// Prepares a batch of documents in parallel: downloads content, cracks, enriches, and maps fields
    /// without uploading to the index. Returns preparation results for bulk upload.
    /// </summary>
    private async Task<List<DocumentPrepareResult>> PrepareDocumentBatchAsync(
        Indexer indexer,
        DataSourceDocument[] batch,
        int maxParallelism,
        IDataSourceConnector connector,
        DataSource dataSource)
    {
        var semaphore = new SemaphoreSlim(maxParallelism);
        var tasks = batch.Select(async doc =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await PrepareDocumentSafeAsync(indexer, doc, connector, dataSource);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Safely prepares a document, catching any exceptions and wrapping them
    /// in a DocumentPrepareResult with error details.
    /// Performs change detection to skip documents that haven't changed since last indexing.
    /// </summary>
    private async Task<DocumentPrepareResult> PrepareDocumentSafeAsync(
        Indexer indexer, DataSourceDocument doc,
        IDataSourceConnector connector, DataSource dataSource)
    {
        try
        {
            // Check if the document has changed since it was last indexed
            if (dataSource.DataChangeDetectionPolicy != null)
            {
                var hasChanged = await HasDocumentChangedAsync(indexer, doc, dataSource);
                if (!hasChanged)
                {
                    _logger.LogDebug(
                        "Skipping unchanged document: {Key} (Name: {Name})",
                        doc.Key, doc.Name);
                    return DocumentPrepareResult.Skip();
                }
            }

            var actions = await PrepareDocumentAsync(indexer, doc, connector, dataSource);
            return DocumentPrepareResult.Ok(actions);
        }
        catch (InvalidDocumentKeyException keyEx)
        {
            _logger.LogWarning(keyEx, "Invalid document key: {Key}", keyEx.Key);
            return DocumentPrepareResult.Fail(new IndexerExecutionError
            {
                Key = $"localId={doc.Key}&documentKey={keyEx.Key}",
                ErrorMessage = keyEx.Message,
                StatusCode = 400,
                Name = $"DocumentExtraction.azureblob.{indexer.DataSourceName}",
                Details = $"Target field 'id' is either not present, doesn't have a value set, or no data could be extracted from the document for it.Failed document: '{doc.Key}'",
                DocumentationLink = "https://go.microsoft.com/fwlink/?linkid=2049388"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process document: {Key}", doc.Key);
            return DocumentPrepareResult.Fail(new IndexerExecutionError
            {
                Key = doc.Key,
                ErrorMessage = ex.Message,
                StatusCode = 500,
                Name = doc.Name
            });
        }
    }

    public async Task ResetAsync(string name)
    {
        var indexer = await _repository.GetAsync(name);
        if (indexer == null)
        {
            throw new InvalidOperationException($"Indexer '{name}' not found.");
        }

        _logger.LogInformation("Resetting indexer: {Name}", name);

        var status = await _repository.GetStatusAsync(name) ?? new IndexerStatus();
        
        // Clear tracking state
        var resetResult = new IndexerExecutionResult
        {
            Status = IndexerExecutionStatus.Reset,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            FinalTrackingState = null
        };

        status.LastResult = resetResult;
        status.ExecutionHistory.Insert(0, resetResult);
        if (status.ExecutionHistory.Count > 10)
        {
            status.ExecutionHistory = status.ExecutionHistory.Take(10).ToList();
        }

        await _repository.SaveStatusAsync(name, status);
    }

    private async Task ValidateIndexerAsync(Indexer indexer)
    {
        if (string.IsNullOrWhiteSpace(indexer.Name))
        {
            throw new ArgumentException("Indexer name is required.");
        }

        if (string.IsNullOrWhiteSpace(indexer.DataSourceName))
        {
            throw new ArgumentException("Data source name is required.");
        }

        if (string.IsNullOrWhiteSpace(indexer.TargetIndexName))
        {
            throw new ArgumentException("Target index name is required.");
        }

        // Verify data source exists
        if (!await _dataSourceService.ExistsAsync(indexer.DataSourceName))
        {
            throw new ArgumentException($"Data source '{indexer.DataSourceName}' does not exist.");
        }

        // Verify target index exists
        if (!await _indexService.IndexExistsAsync(indexer.TargetIndexName))
        {
            throw new ArgumentException($"Target index '{indexer.TargetIndexName}' does not exist.");
        }
    }

    /// <summary>
    /// Checks whether a source document has changed since it was last indexed.
    /// Compares the high-water-mark column value (e.g., metadata_storage_last_modified)
    /// from the source document metadata against the value stored in the index.
    /// Returns true if the document is new or has changed, false if unchanged.
    /// </summary>
    private async Task<bool> HasDocumentChangedAsync(
        Indexer indexer, DataSourceDocument sourceDoc, DataSource dataSource)
    {
        var policy = dataSource.DataChangeDetectionPolicy;
        if (policy == null)
            return true; // No policy = always process

        // Determine the document key as it would appear in the index
        var keyMapping = GetKeyFieldMapping(indexer);
        var docKey = sourceDoc.Key;

        // Apply the key mapping function if one exists
        if (keyMapping.MappingFunction != null)
        {
            docKey = ApplyMappingFunction(docKey, keyMapping.MappingFunction)?.ToString() ?? docKey;
        }

        var keyFieldName = keyMapping.TargetFieldName ?? keyMapping.SourceFieldName;

        // Look up the document in the index
        Dictionary<string, object?>? existingDoc;
        try
        {
            existingDoc = await _documentService.GetDocumentAsync(
                indexer.TargetIndexName, docKey);
        }
        catch
        {
            // If lookup fails (e.g., index not created yet), treat as changed
            return true;
        }

        if (existingDoc == null)
        {
            // Document doesn't exist in the index yet — needs indexing
            return true;
        }

        // Compare using the high-water-mark column
        var hwmColumn = policy.HighWaterMarkColumnName;
        if (string.IsNullOrEmpty(hwmColumn))
            return true; // No column specified = always process

        // Get the source value from the document metadata
        object? sourceValue = null;

        // Common high-water-mark columns map to DataSourceDocument properties
        if (hwmColumn.Equals("metadata_storage_last_modified", StringComparison.OrdinalIgnoreCase))
        {
            sourceValue = sourceDoc.LastModified?.ToString("O");
        }
        else if (hwmColumn.Equals("metadata_storage_size", StringComparison.OrdinalIgnoreCase))
        {
            sourceValue = sourceDoc.Size.ToString();
        }
        else if (sourceDoc.Metadata.TryGetValue(hwmColumn, out var metaValue))
        {
            sourceValue = metaValue?.ToString();
        }

        if (sourceValue == null)
            return true; // Can't determine = always process

        // Find the matching field in the index
        // The hwm column might be stored under a different field name via field mappings
        var targetFieldName = hwmColumn;

        // Check if there's a field mapping for this column
        if (indexer.FieldMappings != null)
        {
            var mapping = indexer.FieldMappings.FirstOrDefault(m =>
                m.SourceFieldName.Equals(hwmColumn, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                targetFieldName = mapping.TargetFieldName ?? mapping.SourceFieldName;
            }
        }

        // Get the indexed value
        if (!existingDoc.TryGetValue(targetFieldName, out var indexedValue) || indexedValue == null)
        {
            // Field not found in index = treat as changed (needs re-indexing)
            return true;
        }

        // Compare values as strings
        var sourceStr = sourceValue.ToString() ?? "";
        var indexedStr = indexedValue.ToString() ?? "";

        // For date comparisons, try to parse and compare as DateTimeOffset
        if (DateTimeOffset.TryParse(sourceStr, out var sourceDate) &&
            DateTimeOffset.TryParse(indexedStr, out var indexedDate))
        {
            var changed = sourceDate > indexedDate;
            if (!changed && _diagnosticSettings.Enabled && _diagnosticSettings.LogDocumentDetails)
            {
                _logger.LogInformation(
                    "[DIAGNOSTIC] Document '{Key}' unchanged: source={SourceDate}, indexed={IndexedDate}",
                    sourceDoc.Key, sourceStr, indexedStr);
            }
            return changed;
        }

        // Fallback: string comparison
        return !string.Equals(sourceStr, indexedStr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that a document key contains only valid characters.
    /// Valid characters are: letters, digits, underscore (_), dash (-), or equal sign (=).
    /// </summary>
    private static void ValidateDocumentKey(string key, string? documentName, string? dataSourceName)
    {
        if (string.IsNullOrEmpty(key) || !ValidKeyPattern().IsMatch(key))
        {
            throw new InvalidDocumentKeyException(key, documentName, dataSourceName);
        }
    }

    /// <summary>
    /// Prepares a document for indexing: downloads content, cracks, runs skillset, maps fields.
    /// Returns one or more IndexActions ready for bulk upload (JSON array produces multiple).
    /// Does NOT upload to the index.
    /// </summary>
    private async Task<List<IndexAction>> PrepareDocumentAsync(
        Indexer indexer, DataSourceDocument sourceDoc,
        IDataSourceConnector connector, DataSource dataSource)
    {
        var processStart = DateTime.UtcNow;
        
        // Validate document key before processing
        ValidateDocumentKey(sourceDoc.Key, sourceDoc.Name, indexer.DataSourceName);

        // Download content on-demand (parallel across batch via PrepareDocumentBatchAsync)
        if (sourceDoc.Content == null || sourceDoc.Content.Length == 0)
        {
            await connector.DownloadContentAsync(dataSource, sourceDoc);
        }

        // Diagnostic: Log document processing start
        if (_diagnosticSettings.Enabled && _diagnosticSettings.LogDocumentDetails)
        {
            _logger.LogInformation(
                "[DIAGNOSTIC] Processing document: Key='{DocumentKey}', Name='{DocumentName}', ContentType='{ContentType}', Size={Size} bytes",
                sourceDoc.Key, sourceDoc.Name, sourceDoc.ContentType, sourceDoc.Size);
            
            if (sourceDoc.Metadata.Count > 0)
            {
                var metadataJson = JsonSerializer.Serialize(sourceDoc.Metadata, _jsonOptions);
                _logger.LogInformation("[DIAGNOSTIC] Document metadata:\n{Metadata}", metadataJson);
            }
        }

        // Check for JSON parsing mode
        var parsingMode = indexer.Parameters?.Configuration?.ParsingMode?.ToLowerInvariant() ?? "default";
        
        if (parsingMode == "json" || parsingMode == "jsonarray")
        {
            return await PrepareJsonDocumentAsync(indexer, sourceDoc, parsingMode);
        }
        
        // Extract content using document cracker (default mode)
        var crackedDoc = await ExtractContentAsync(sourceDoc, indexer.Parameters?.Configuration);

        // Diagnostic: Log cracked document details
        if (_diagnosticSettings.Enabled && _diagnosticSettings.LogDocumentDetails)
        {
            _logger.LogInformation(
                "[DIAGNOSTIC] Document cracked: Title='{Title}', Author='{Author}', PageCount={PageCount}, WordCount={WordCount}, ContentLength={ContentLength}",
                crackedDoc.Title ?? "(none)", crackedDoc.Author ?? "(none)", 
                crackedDoc.PageCount ?? 0, crackedDoc.WordCount ?? 0, crackedDoc.Content?.Length ?? 0);
        }

        // Create enriched document for skill processing
        var initialContent = new Dictionary<string, object?>
        {
            ["key"] = sourceDoc.Key,
            ["content"] = crackedDoc.Content,
            ["metadata_storage_path"] = sourceDoc.Key,
            ["metadata_storage_name"] = sourceDoc.Name,
            ["metadata_content_type"] = sourceDoc.ContentType
        };

        // Add cracked document metadata
        if (crackedDoc.Title != null) initialContent["metadata_title"] = crackedDoc.Title;
        if (crackedDoc.Author != null) initialContent["metadata_author"] = crackedDoc.Author;
        if (crackedDoc.PageCount.HasValue) initialContent["metadata_page_count"] = crackedDoc.PageCount.Value;
        if (crackedDoc.WordCount.HasValue) initialContent["metadata_word_count"] = crackedDoc.WordCount.Value;
        if (crackedDoc.CharacterCount.HasValue) initialContent["metadata_character_count"] = crackedDoc.CharacterCount.Value;
        if (crackedDoc.Language != null) initialContent["metadata_language"] = crackedDoc.Language;

        // Add source metadata
        foreach (var (key, value) in sourceDoc.Metadata)
        {
            initialContent[key] = value;
        }
        foreach (var (key, value) in crackedDoc.Metadata)
        {
            initialContent[$"metadata_{key}"] = value;
        }

        var enrichedDoc = new EnrichedDocument(initialContent);

        // Execute skillset if configured
        await ExecuteSkillsetAsync(indexer, enrichedDoc, sourceDoc.Key);

        // Build the final document from enriched data
        var enrichedData = enrichedDoc.ToDictionary();
        var document = new IndexAction
        {
            ["@search.action"] = "mergeOrUpload"
        };

        // Add key field (using metadata_storage_path as default key)
        var keyField = GetKeyFieldMapping(indexer);
        document[keyField.TargetFieldName ?? keyField.SourceFieldName] = sourceDoc.Key;

        // Add all enriched fields
        foreach (var (key, value) in enrichedData)
        {
            if (key != "key" && !key.StartsWith("@"))
            {
                document[key] = value;
            }
        }

        // Apply custom field mappings (can override enriched values)
        ApplyFieldMappings(indexer, enrichedData, document, sourceDoc.Key);

        // Apply output field mappings (from skillset outputs)
        ApplyOutputFieldMappings(indexer, enrichedDoc, document);

        // Diagnostic: Log document processing completion with timing
        if (_diagnosticSettings.Enabled && _diagnosticSettings.LogDocumentDetails && _diagnosticSettings.IncludeTimings)
        {
            var duration = DateTime.UtcNow - processStart;
            _logger.LogInformation(
                "[DIAGNOSTIC] Document '{DocumentKey}' prepared in {Duration}ms - Target index: '{IndexName}', Fields mapped: {FieldCount}",
                sourceDoc.Key, duration.TotalMilliseconds, indexer.TargetIndexName, document.Count - 1);
        }

        return [document];
    }

    /// <summary>
    /// Executes the skillset on an enriched document if the indexer has one configured.
    /// </summary>
    private async Task ExecuteSkillsetAsync(Indexer indexer, EnrichedDocument enrichedDoc, string docKey)
    {
        if (string.IsNullOrEmpty(indexer.SkillsetName))
            return;

        var skillset = await _skillsetService.GetAsync(indexer.SkillsetName);
        if (skillset == null)
        {
            _logger.LogWarning("Skillset '{SkillsetName}' not found, skipping skill execution", 
                indexer.SkillsetName);
            return;
        }

        _logger.LogDebug("Executing skillset '{SkillsetName}' for document {Key}", 
            skillset.Name, docKey);

        var pipelineResult = await _skillPipeline.ExecuteAsync(skillset, enrichedDoc);

        if (!pipelineResult.Success)
        {
            var errorMessage = $"Skillset '{skillset.Name}' failed for document {docKey}: {string.Join("; ", pipelineResult.Errors)}";
            _logger.LogWarning(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        if (pipelineResult.Warnings.Count > 0)
        {
            _logger.LogDebug("Skillset warnings: {Warnings}", 
                string.Join("; ", pipelineResult.Warnings));
        }
    }

    /// <summary>
    /// Applies field mappings from enriched data to the document action.
    /// </summary>
    private void ApplyFieldMappings(
        Indexer indexer,
        Dictionary<string, object?> enrichedData,
        IndexAction document,
        string docKey)
    {
        if (indexer.FieldMappings == null)
            return;

        if (_diagnosticSettings.Enabled && _diagnosticSettings.LogFieldMappings && indexer.FieldMappings.Count > 0)
        {
            _logger.LogInformation("[DIAGNOSTIC] Applying {Count} field mappings for document '{DocumentKey}'",
                indexer.FieldMappings.Count, docKey);
        }

        foreach (var mapping in indexer.FieldMappings)
        {
            if (enrichedData.TryGetValue(mapping.SourceFieldName, out var value) && value != null)
            {
                var targetField = mapping.TargetFieldName ?? mapping.SourceFieldName;
                var mappedValue = ApplyMappingFunction(value, mapping.MappingFunction);
                document[targetField] = mappedValue;

                if (_diagnosticSettings.Enabled && _diagnosticSettings.LogFieldMappings)
                {
                    _logger.LogInformation(
                        "[DIAGNOSTIC] Field mapping: '{Source}' -> '{Target}', Function: {Function}",
                        mapping.SourceFieldName, targetField, mapping.MappingFunction?.Name ?? "(none)");
                }
            }
        }
    }

    /// <summary>
    /// Applies output field mappings (from skillset outputs) to the document action.
    /// </summary>
    private void ApplyOutputFieldMappings(
        Indexer indexer,
        EnrichedDocument enrichedDoc,
        IndexAction document)
    {
        if (indexer.OutputFieldMappings == null)
            return;

        if (_diagnosticSettings.Enabled && _diagnosticSettings.LogFieldMappings && indexer.OutputFieldMappings.Count > 0)
        {
            _logger.LogInformation("[DIAGNOSTIC] Applying {Count} output field mappings",
                indexer.OutputFieldMappings.Count);
        }

        foreach (var mapping in indexer.OutputFieldMappings)
        {
            // Source is a path like "/document/embedding"
            var sourcePath = mapping.SourceFieldName;
            var value = enrichedDoc.GetValue(sourcePath);
            if (value != null)
            {
                var targetField = mapping.TargetFieldName ?? Path.GetFileName(sourcePath);
                var mappedValue = ApplyMappingFunction(value, mapping.MappingFunction);
                document[targetField] = mappedValue;

                if (_diagnosticSettings.Enabled && _diagnosticSettings.LogFieldMappings)
                {
                    _logger.LogInformation(
                        "[DIAGNOSTIC] Output field mapping: '{Source}' -> '{Target}', Function: {Function}",
                        sourcePath, targetField, mapping.MappingFunction?.Name ?? "(none)");
                }
            }
        }
    }

    private async Task<CrackedDocument> ExtractContentAsync(DataSourceDocument doc, IndexerConfiguration? config)
    {
        var dataToExtract = config?.DataToExtract ?? "contentAndMetadata";
        
        if (dataToExtract == "storageMetadata")
        {
            return new CrackedDocument { Success = true };
        }

        var extension = Path.GetExtension(doc.Name).ToLowerInvariant();

        // Try to use document cracker
        if (_documentCrackerFactory.CanCrack(doc.ContentType, extension))
        {
            var crackedDoc = await _documentCrackerFactory.CrackDocumentAsync(
                doc.Content, doc.Name, doc.ContentType);
            
            if (crackedDoc.Success)
            {
                _logger.LogDebug("Successfully cracked document {Name} ({ContentType}): {CharCount} characters",
                    doc.Name, doc.ContentType, crackedDoc.CharacterCount);
                return crackedDoc;
            }
            else
            {
                _logger.LogWarning("Failed to crack document {Name}: {Error}", 
                    doc.Name, crackedDoc.ErrorMessage);
            }
        }

        // Fallback: simple text extraction for text-based content types
        var contentType = doc.ContentType.ToLowerInvariant();
        if (contentType.StartsWith("text/") || 
            contentType == "application/json" ||
            contentType == "application/xml")
        {
            return new CrackedDocument
            {
                Content = System.Text.Encoding.UTF8.GetString(doc.Content),
                Success = true
            };
        }

        // For unsupported binary files, return empty
        _logger.LogDebug("No cracker available for {Name} ({ContentType})", doc.Name, doc.ContentType);
        return new CrackedDocument 
        { 
            Success = true,
            Warnings = new List<string> { $"No content extraction available for {doc.ContentType}" }
        };
    }

    private static FieldMapping GetKeyFieldMapping(Indexer indexer)
    {
        // Find key field mapping or use default
        var keyMapping = indexer.FieldMappings?.FirstOrDefault(m => 
            m.TargetFieldName?.Equals("id", StringComparison.OrdinalIgnoreCase) == true ||
            m.SourceFieldName.Equals("metadata_storage_path", StringComparison.OrdinalIgnoreCase));

        if (keyMapping != null)
        {
            return keyMapping;
        }

        return new FieldMapping
        {
            SourceFieldName = "metadata_storage_path",
            TargetFieldName = "id"
        };
    }

    private static object ApplyMappingFunction(object? value, FieldMappingFunction? function)
    {
        if (function == null)
        {
            return value ?? string.Empty;
        }

        var stringValue = value?.ToString() ?? string.Empty;

        return function.Name.ToLowerInvariant() switch
        {
            "base64encode" => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(stringValue)),
            "base64decode" => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(stringValue)),
            "urlencode" => Uri.EscapeDataString(stringValue),
            "urldecode" => Uri.UnescapeDataString(stringValue),
            "extracttokenatposition" => ExtractTokenAtPosition(stringValue, function.Parameters),
            _ => value ?? string.Empty
        };
    }

    private static string ExtractTokenAtPosition(string value, Dictionary<string, object>? parameters)
    {
        if (parameters == null)
        {
            return value;
        }

        var delimiter = parameters.TryGetValue("delimiter", out var d) ? d?.ToString() ?? " " : " ";
        var position = parameters.TryGetValue("position", out var p) && int.TryParse(p?.ToString(), out var pos) ? pos : 0;

        var tokens = value.Split([delimiter], StringSplitOptions.RemoveEmptyEntries);
        return position >= 0 && position < tokens.Length ? tokens[position] : value;
    }

    /// <summary>
    /// Prepares a JSON document using JSON parsing mode.
    /// Returns one or more IndexActions (JSON array produces multiple actions).
    /// Does NOT upload to the index.
    /// </summary>
    private async Task<List<IndexAction>> PrepareJsonDocumentAsync(Indexer indexer, DataSourceDocument sourceDoc, string parsingMode)
    {
        var jsonContent = System.Text.Encoding.UTF8.GetString(sourceDoc.Content);
        var actions = new List<IndexAction>();

        try
        {
            if (parsingMode == "jsonarray")
            {
                // Parse as JSON array - each element becomes a separate document
                var jsonArray = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(jsonContent);
                if (jsonArray != null)
                {
                    var index = 0;
                    foreach (var element in jsonArray)
                    {
                        var action = await PrepareJsonElementAsync(indexer, sourceDoc, element, $"{sourceDoc.Key}_{index}");
                        actions.Add(action);
                        index++;
                    }
                }
            }
            else
            {
                // Parse as single JSON object
                var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonContent);
                var action = await PrepareJsonElementAsync(indexer, sourceDoc, jsonElement, null);
                actions.Add(action);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON document {Name}: {Error}", sourceDoc.Name, ex.Message);
            throw new InvalidOperationException($"Failed to parse JSON document '{sourceDoc.Name}': {ex.Message}");
        }

        return actions;
    }

    /// <summary>
    /// Prepares a single JSON element as an IndexAction.
    /// Does NOT upload to the index.
    /// </summary>
    private async Task<IndexAction> PrepareJsonElementAsync(Indexer indexer, DataSourceDocument sourceDoc, System.Text.Json.JsonElement jsonElement, string? keySuffix)
    {
        // Extract all properties from the JSON
        var jsonData = new Dictionary<string, object?>();
        if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in jsonElement.EnumerateObject())
            {
                jsonData[property.Name] = ConvertJsonValue(property.Value);
            }
        }

        // Add metadata fields
        jsonData["metadata_storage_path"] = sourceDoc.Key;
        jsonData["metadata_storage_name"] = sourceDoc.Name;
        jsonData["metadata_content_type"] = sourceDoc.ContentType;

        // Determine the document key
        var docKey = sourceDoc.Key;
        if (!string.IsNullOrEmpty(keySuffix))
        {
            docKey = keySuffix;
        }
        else if (jsonData.TryGetValue("id", out var idValue) && idValue != null)
        {
            docKey = idValue.ToString() ?? sourceDoc.Key;
        }

        // Validate document key
        ValidateDocumentKey(docKey, sourceDoc.Name, indexer.DataSourceName);

        // Create enriched document for skill processing
        var enrichedDoc = new EnrichedDocument(jsonData);

        // Execute skillset if configured
        await ExecuteSkillsetAsync(indexer, enrichedDoc, docKey);

        // Build the final document from enriched data
        var enrichedData = enrichedDoc.ToDictionary();
        var document = new IndexAction
        {
            ["@search.action"] = "mergeOrUpload"
        };

        // Apply field mappings
        if (indexer.FieldMappings != null && indexer.FieldMappings.Count > 0)
        {
            foreach (var mapping in indexer.FieldMappings)
            {
                if (enrichedData.TryGetValue(mapping.SourceFieldName, out var value) && value != null)
                {
                    var targetField = mapping.TargetFieldName ?? mapping.SourceFieldName;
                    document[targetField] = ApplyMappingFunction(value, mapping.MappingFunction);
                }
            }
        }
        else
        {
            // No field mappings - use enriched data properties directly
            foreach (var (key, value) in enrichedData)
            {
                if (!key.StartsWith("metadata_") || key == "metadata_storage_path")
                {
                    document[key] = value;
                }
            }
        }

        // Apply output field mappings (from skillset outputs)
        ApplyOutputFieldMappings(indexer, enrichedDoc, document);

        // Ensure we have a key field
        if (!document.ContainsKey("id") && !indexer.FieldMappings?.Any(m => m.TargetFieldName == "id") == true)
        {
            document["id"] = docKey;
        }

        _logger.LogDebug("Prepared JSON document with fields: {Fields}", 
            string.Join(", ", document.Keys.Where(k => k != "@search.action")));

        return document;
    }

    /// <summary>
    /// Convert a JSON element to a .NET object.
    /// </summary>
    private static object? ConvertJsonValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value)),
            _ => element.ToString()
        };
    }
}
