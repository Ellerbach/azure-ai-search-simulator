using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.DataSources;
using AzureAISearchSimulator.Search.DocumentCracking;
using AzureAISearchSimulator.Search.Skills;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Service for managing indexers.
/// </summary>
public class IndexerService : IIndexerService
{
    private readonly IIndexerRepository _repository;
    private readonly IDataSourceService _dataSourceService;
    private readonly ISkillsetService _skillsetService;
    private readonly IIndexService _indexService;
    private readonly IDataSourceConnectorFactory _connectorFactory;
    private readonly IDocumentCrackerFactory _documentCrackerFactory;
    private readonly ISkillPipeline _skillPipeline;
    private readonly IDocumentService _documentService;
    private readonly ILogger<IndexerService> _logger;

    public IndexerService(
        IIndexerRepository repository,
        IDataSourceService dataSourceService,
        ISkillsetService skillsetService,
        IIndexService indexService,
        IDataSourceConnectorFactory connectorFactory,
        IDocumentCrackerFactory documentCrackerFactory,
        ISkillPipeline skillPipeline,
        IDocumentService documentService,
        ILogger<IndexerService> logger)
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

            // Fetch documents from data source
            var documents = await connector.ListDocumentsAsync(dataSource, trackingState);
            var documentList = documents.ToList();

            _logger.LogInformation("Indexer {Name} found {Count} documents to process", 
                name, documentList.Count);

            // Process documents
            var processedCount = 0;
            var failedCount = 0;
            var batchSize = indexer.Parameters?.BatchSize ?? 1000;
            var maxFailedItems = indexer.Parameters?.MaxFailedItems ?? -1;

            foreach (var batch in documentList.Chunk(batchSize))
            {
                foreach (var doc in batch)
                {
                    try
                    {
                        await ProcessDocumentAsync(indexer, doc);
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogWarning(ex, "Failed to process document: {Key}", doc.Key);
                        
                        executionResult.Errors.Add(new IndexerExecutionError
                        {
                            Key = doc.Key,
                            ErrorMessage = ex.Message,
                            StatusCode = 500,
                            Name = doc.Name
                        });

                        if (maxFailedItems >= 0 && failedCount > maxFailedItems)
                        {
                            throw new InvalidOperationException(
                                $"Maximum failed items ({maxFailedItems}) exceeded.");
                        }
                    }
                }
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
                "Indexer {Name} completed: {Processed} processed, {Failed} failed", 
                name, processedCount, failedCount);
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

    private async Task ProcessDocumentAsync(Indexer indexer, DataSourceDocument sourceDoc)
    {
        // Check for JSON parsing mode
        var parsingMode = indexer.Parameters?.Configuration?.ParsingMode?.ToLowerInvariant() ?? "default";
        
        if (parsingMode == "json" || parsingMode == "jsonarray")
        {
            await ProcessJsonDocumentAsync(indexer, sourceDoc, parsingMode);
            return;
        }
        
        // Extract content using document cracker (default mode)
        var crackedDoc = await ExtractContentAsync(sourceDoc, indexer.Parameters?.Configuration);

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
        if (!string.IsNullOrEmpty(indexer.SkillsetName))
        {
            var skillset = await _skillsetService.GetAsync(indexer.SkillsetName);
            if (skillset != null)
            {
                _logger.LogDebug("Executing skillset '{SkillsetName}' for document {Key}", 
                    skillset.Name, sourceDoc.Key);
                
                var pipelineResult = await _skillPipeline.ExecuteAsync(skillset, enrichedDoc);
                
                if (!pipelineResult.Success)
                {
                    var errorMessage = $"Skillset '{skillset.Name}' failed for document {sourceDoc.Key}: {string.Join("; ", pipelineResult.Errors)}";
                    _logger.LogWarning(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                if (pipelineResult.Warnings.Count > 0)
                {
                    _logger.LogDebug("Skillset warnings: {Warnings}", 
                        string.Join("; ", pipelineResult.Warnings));
                }
            }
            else
            {
                _logger.LogWarning("Skillset '{SkillsetName}' not found, skipping skill execution", 
                    indexer.SkillsetName);
            }
        }

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
        if (indexer.FieldMappings != null)
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

        // Apply output field mappings (from skillset outputs)
        if (indexer.OutputFieldMappings != null)
        {
            foreach (var mapping in indexer.OutputFieldMappings)
            {
                // Source is a path like "/document/embedding"
                var sourcePath = mapping.SourceFieldName;
                var value = enrichedDoc.GetValue(sourcePath);
                if (value != null)
                {
                    var targetField = mapping.TargetFieldName ?? Path.GetFileName(sourcePath);
                    document[targetField] = ApplyMappingFunction(value, mapping.MappingFunction);
                }
            }
        }

        // Upload to index
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction> { document }
        };
        await _documentService.IndexDocumentsAsync(indexer.TargetIndexName, request);
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
    /// Process a JSON document using JSON parsing mode.
    /// The JSON content is parsed and its properties are used directly as index fields.
    /// </summary>
    private async Task ProcessJsonDocumentAsync(Indexer indexer, DataSourceDocument sourceDoc, string parsingMode)
    {
        var jsonContent = System.Text.Encoding.UTF8.GetString(sourceDoc.Content);
        
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
                        await IndexJsonElementAsync(indexer, sourceDoc, element, $"{sourceDoc.Key}_{index}");
                        index++;
                    }
                }
            }
            else
            {
                // Parse as single JSON object
                var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonContent);
                await IndexJsonElementAsync(indexer, sourceDoc, jsonElement, null);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON document {Name}: {Error}", sourceDoc.Name, ex.Message);
            throw new InvalidOperationException($"Failed to parse JSON document '{sourceDoc.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Index a single JSON element as a document.
    /// </summary>
    private async Task IndexJsonElementAsync(Indexer indexer, DataSourceDocument sourceDoc, System.Text.Json.JsonElement jsonElement, string? keySuffix)
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

        // Create enriched document for skill processing
        var enrichedDoc = new EnrichedDocument(jsonData);

        // Execute skillset if configured
        if (!string.IsNullOrEmpty(indexer.SkillsetName))
        {
            var skillset = await _skillsetService.GetAsync(indexer.SkillsetName);
            if (skillset != null)
            {
                _logger.LogInformation("Executing skillset '{SkillsetName}' for JSON document {Key}", 
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
                    _logger.LogWarning("Skillset warnings for {Key}: {Warnings}", 
                        docKey, string.Join("; ", pipelineResult.Warnings));
                }
            }
            else
            {
                _logger.LogWarning("Skillset '{SkillsetName}' not found, skipping skill execution", 
                    indexer.SkillsetName);
            }
        }

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
        if (indexer.OutputFieldMappings != null)
        {
            foreach (var mapping in indexer.OutputFieldMappings)
            {
                // Source is a path like "/document/contentEmbedding"
                var sourcePath = mapping.SourceFieldName;
                var value = enrichedDoc.GetValue(sourcePath);
                if (value != null)
                {
                    var targetField = mapping.TargetFieldName ?? System.IO.Path.GetFileName(sourcePath);
                    document[targetField] = ApplyMappingFunction(value, mapping.MappingFunction);
                    _logger.LogDebug("Applied output field mapping: {Source} -> {Target}", sourcePath, targetField);
                }
                else
                {
                    _logger.LogDebug("Output field mapping source {Source} has no value", sourcePath);
                }
            }
        }

        // Ensure we have a key field
        if (!document.ContainsKey("id") && !indexer.FieldMappings?.Any(m => m.TargetFieldName == "id") == true)
        {
            document["id"] = docKey;
        }

        _logger.LogDebug("Indexing JSON document with fields: {Fields}", 
            string.Join(", ", document.Keys.Where(k => k != "@search.action")));

        // Upload to index
        var request = new IndexDocumentsRequest
        {
            Value = new List<IndexAction> { document }
        };
        await _documentService.IndexDocumentsAsync(indexer.TargetIndexName, request);
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
