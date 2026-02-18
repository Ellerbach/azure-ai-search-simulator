using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Logging;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.Hnsw;
using System.Text.Json;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Service for managing documents using Lucene.NET.
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly LuceneIndexManager _indexManager;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly IIndexService _indexService;

    public DocumentService(
        ILogger<DocumentService> logger,
        LuceneIndexManager indexManager,
        IVectorSearchService vectorSearchService,
        IIndexService indexService)
    {
        _logger = logger;
        _indexManager = indexManager;
        _vectorSearchService = vectorSearchService;
        _indexService = indexService;
    }

    public async Task<IndexDocumentsResponse> IndexDocumentsAsync(string indexName, IndexDocumentsRequest request)
    {
        var index = await _indexService.GetIndexAsync(indexName);
        if (index == null)
        {
            throw new KeyNotFoundException($"Index '{indexName}' not found");
        }

        var keyField = LuceneDocumentMapper.GetKeyFieldName(index);
        IndexWriter writer;
        try
        {
            writer = _indexManager.GetWriter(indexName);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning(
                "Index manager is disposed — skipping batch of {Count} documents for '{IndexName}' (application may be shutting down)",
                request.Value.Count, indexName);
            return new IndexDocumentsResponse
            {
                Value = request.Value.Select(a => new IndexingResult
                {
                    Key = "(unavailable)",
                    Status = false,
                    StatusCode = 503,
                    ErrorMessage = $"Index '{indexName}' is no longer available"
                }).ToList()
            };
        }

        var results = new List<IndexingResult>();

        foreach (var action in request.Value)
        {
            var result = await ProcessActionAsync(indexName, index, keyField, writer, action);
            results.Add(result);

            // If the index was disposed mid-batch, stop processing remaining actions
            if (result.StatusCode == 503)
            {
                var remaining = request.Value.Count - results.Count;
                if (remaining > 0)
                {
                    _logger.LogWarning("Stopping batch — {Remaining} remaining documents skipped", remaining);
                }
                break;
            }
        }

        // Only commit if we have successful results and the manager is still alive
        try
        {
            if (results.Any(r => r.Status))
            {
                _indexManager.Commit(indexName);
                _vectorSearchService.SaveAll();
            }
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Could not commit changes — index manager disposed");
        }

        var response = new IndexDocumentsResponse
        {
            Value = results
        };

        _logger.LogInformation(
            "Indexed {Count} documents to {IndexName}: {Succeeded} succeeded, {Failed} failed",
            results.Count, indexName,
            results.Count(r => r.Status),
            results.Count(r => !r.Status));

        return response;
    }

    private async Task<IndexingResult> ProcessActionAsync(
        string indexName,
        SearchIndex schema,
        string keyField,
        IndexWriter writer,
        IndexAction action)
    {
        string? key = null;
        try
        {
            // Normalize action type
            var actionType = action.ActionType;

            // Get document key
            if (!action.Document.TryGetValue(keyField, out var keyObj) || keyObj == null)
            {
                return new IndexingResult
                {
                    Key = "(unknown)",
                    Status = false,
                    StatusCode = 400,
                    ErrorMessage = $"Document is missing key field '{keyField}'"
                };
            }

            key = ConvertToString(keyObj);
            var term = new Term(keyField, key);

            switch (actionType)
            {
                case IndexActionType.Upload:
                    var existingDoc = await GetDocumentAsync(indexName, key);
                    return await UploadDocumentAsync(indexName, schema, writer, term, key, action.Document, isMerge: existingDoc != null);

                case IndexActionType.Merge:
                    return await MergeDocumentAsync(indexName, schema, writer, term, key, action.Document);

                case IndexActionType.MergeOrUpload:
                    return await MergeOrUploadDocumentAsync(indexName, schema, writer, term, key, action.Document);

                case IndexActionType.Delete:
                    return DeleteDocument(indexName, writer, term, key);

                default:
                    return new IndexingResult
                    {
                        Key = key,
                        Status = false,
                        StatusCode = 400,
                        ErrorMessage = $"Unknown action type: {actionType}"
                    };
            }
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Index '{IndexName}' is disposed — document action skipped (application may be shutting down)", indexName);
            return new IndexingResult
            {
                Key = key ?? "(unknown)",
                Status = false,
                StatusCode = 503,
                ErrorMessage = $"Index '{indexName}' is no longer available (may be shutting down)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document action");
            return new IndexingResult
            {
                Key = "(error)",
                Status = false,
                StatusCode = 500,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Validates document fields against the index schema.
    /// </summary>
    private List<string> ValidateDocument(SearchIndex schema, Dictionary<string, object?> document, string key)
    {
        var errors = new List<string>();
        var schemaFields = schema.Fields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in document)
        {
            // Skip action type field
            if (kvp.Key.StartsWith("@search."))
                continue;

            // Check if field exists in schema
            if (!schemaFields.TryGetValue(kvp.Key, out var field))
            {
                errors.Add($"Field '{kvp.Key}' is not defined in the index schema");
                continue;
            }

            // Validate field type if value is not null
            if (kvp.Value != null)
            {
                var typeError = ValidateFieldType(field, kvp.Key, kvp.Value);
                if (typeError != null)
                {
                    errors.Add(typeError);
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that a value matches the expected field type.
    /// </summary>
    private string? ValidateFieldType(SearchField field, string fieldName, object value)
    {
        var actualType = value.GetType();
        
        // Handle JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return ValidateJsonElementType(field, fieldName, jsonElement);
        }

        return field.Type switch
        {
            "Edm.String" when actualType != typeof(string) => 
                $"Field '{fieldName}' expects Edm.String but received {actualType.Name}",
            
            "Edm.Int32" when !IsNumericType(actualType) => 
                $"Field '{fieldName}' expects Edm.Int32 but received {actualType.Name}",
            
            "Edm.Int64" when !IsNumericType(actualType) => 
                $"Field '{fieldName}' expects Edm.Int64 but received {actualType.Name}",
            
            "Edm.Double" when !IsNumericType(actualType) => 
                $"Field '{fieldName}' expects Edm.Double but received {actualType.Name}",
            
            "Edm.Boolean" when actualType != typeof(bool) => 
                $"Field '{fieldName}' expects Edm.Boolean but received {actualType.Name}",
            
            "Edm.DateTimeOffset" when !IsDateTimeType(actualType) => 
                $"Field '{fieldName}' expects Edm.DateTimeOffset but received {actualType.Name}",
            
            "Collection(Edm.String)" when !IsCollectionType(actualType) => 
                $"Field '{fieldName}' expects Collection(Edm.String) but received {actualType.Name}",
            
            "Collection(Edm.Single)" when !IsCollectionType(actualType) => 
                $"Field '{fieldName}' expects Collection(Edm.Single) but received {actualType.Name}",
            
            _ => null
        };
    }

    private string? ValidateJsonElementType(SearchField field, string fieldName, System.Text.Json.JsonElement element)
    {
        return field.Type switch
        {
            "Edm.String" when element.ValueKind != System.Text.Json.JsonValueKind.String => 
                $"Field '{fieldName}' expects Edm.String but received {element.ValueKind}",
            
            "Edm.Int32" or "Edm.Int64" or "Edm.Double" when element.ValueKind != System.Text.Json.JsonValueKind.Number => 
                $"Field '{fieldName}' expects numeric type but received {element.ValueKind}",
            
            "Edm.Boolean" when element.ValueKind != System.Text.Json.JsonValueKind.True && element.ValueKind != System.Text.Json.JsonValueKind.False => 
                $"Field '{fieldName}' expects Edm.Boolean but received {element.ValueKind}",
            
            "Collection(Edm.String)" or "Collection(Edm.Single)" when element.ValueKind != System.Text.Json.JsonValueKind.Array => 
                $"Field '{fieldName}' expects array type but received {element.ValueKind}",
            
            _ => null
        };
    }

    private static bool IsNumericType(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(double) || 
        type == typeof(float) || type == typeof(decimal) || type == typeof(short) ||
        type == typeof(byte);

    private static bool IsDateTimeType(Type type) =>
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(string);

    private static bool IsCollectionType(Type type) =>
        type.IsArray || (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type));

    private Task<IndexingResult> UploadDocumentAsync(
        string indexName,
        SearchIndex schema,
        IndexWriter writer,
        Term keyTerm,
        string key,
        Dictionary<string, object?> document,
        bool isMerge = false)
    {
        // Validate document fields against schema
        var validationErrors = ValidateDocument(schema, document, key);
        if (validationErrors.Any())
        {
            // Azure AI Search silently drops fields not in the schema.
            // Log at Debug instead of Warning to reduce noise, and strip the unknown fields.
            _logger.LogDebug(
                "Document '{Key}' has fields not in schema (will be dropped): {Errors}", 
                key, string.Join("; ", validationErrors));
            
            // Remove fields that are not defined in the index schema (matching Azure behavior)
            var schemaFields = schema.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keysToRemove = document.Keys
                .Where(k => !k.StartsWith("@search.") && !schemaFields.Contains(k))
                .ToList();
            foreach (var k in keysToRemove)
            {
                document.Remove(k);
            }
        }

        // Delete existing document if present
        writer.DeleteDocuments(keyTerm);

        // Create and add the new document
        var luceneDoc = LuceneDocumentMapper.ToLuceneDocument(document, schema);
        writer.AddDocument(luceneDoc);

        // Store vectors
        StoreVectors(indexName, schema, key, document);

        return Task.FromResult(new IndexingResult
        {
            Key = key,
            Status = true,
            StatusCode = isMerge ? 200 : 201
        });
    }

    private async Task<IndexingResult> MergeDocumentAsync(
        string indexName,
        SearchIndex schema,
        IndexWriter writer,
        Term keyTerm,
        string key,
        Dictionary<string, object?> document)
    {
        // Get existing document
        var existing = await GetDocumentAsync(indexName, key);
        if (existing == null)
        {
            return new IndexingResult
            {
                Key = key,
                Status = false,
                StatusCode = 404,
                ErrorMessage = "Document not found for merge"
            };
        }

        // Merge fields
        foreach (var kvp in document)
        {
            existing[kvp.Key] = kvp.Value;
        }

        return await UploadDocumentAsync(indexName, schema, writer, keyTerm, key, existing, isMerge: true);
    }

    private async Task<IndexingResult> MergeOrUploadDocumentAsync(
        string indexName,
        SearchIndex schema,
        IndexWriter writer,
        Term keyTerm,
        string key,
        Dictionary<string, object?> document)
    {
        // Get existing document
        var existing = await GetDocumentAsync(indexName, key);
        if (existing != null)
        {
            // Merge fields
            foreach (var kvp in document)
            {
                existing[kvp.Key] = kvp.Value;
            }
            document = existing;
        }

        return await UploadDocumentAsync(indexName, schema, writer, keyTerm, key, document, isMerge: existing != null);
    }

    private IndexingResult DeleteDocument(
        string indexName,
        IndexWriter writer,
        Term keyTerm,
        string key)
    {
        writer.DeleteDocuments(keyTerm);
        
        // Remove vectors from HNSW-backed search service
        _vectorSearchService.RemoveDocument(indexName, key);

        return new IndexingResult
        {
            Key = key,
            Status = true,
            StatusCode = 200
        };
    }

    private void StoreVectors(
        string indexName,
        SearchIndex schema,
        string key,
        Dictionary<string, object?> document)
    {
        foreach (var field in schema.Fields.Where(f => f.Type == "Collection(Edm.Single)"))
        {
            if (document.TryGetValue(field.Name, out var value) && value != null)
            {
                var vector = ConvertToFloatArray(value);
                if (vector != null)
                {
                    // Initialize index if needed (auto-detects dimensions from vector)
                    if (!_vectorSearchService.IndexExists(indexName, field.Name))
                    {
                        _vectorSearchService.InitializeIndex(indexName, field.Name, vector.Length);
                    }
                    
                    // Add vector to HNSW-backed search service
                    _vectorSearchService.AddVector(indexName, field.Name, key, vector);
                }
            }
        }
    }

    public async Task<Dictionary<string, object?>?> GetDocumentAsync(
        string indexName,
        string key,
        IEnumerable<string>? selectedFields = null)
    {
        var index = await _indexService.GetIndexAsync(indexName);
        if (index == null)
        {
            throw new KeyNotFoundException($"Index '{indexName}' not found");
        }

        var keyField = LuceneDocumentMapper.GetKeyFieldName(index);
        var searcher = _indexManager.GetSearcher(indexName);
        
        var termQuery = new Lucene.Net.Search.TermQuery(new Term(keyField, key));
        var topDocs = searcher.Search(termQuery, 1);

        if (topDocs.TotalHits == 0)
        {
            return null;
        }

        var luceneDoc = searcher.Doc(topDocs.ScoreDocs[0].Doc);
        return LuceneDocumentMapper.FromLuceneDocument(luceneDoc, selectedFields);
    }

    public Task<long> GetDocumentCountAsync(string indexName)
    {
        try
        {
            var searcher = _indexManager.GetSearcher(indexName);
            return Task.FromResult((long)searcher.IndexReader.NumDocs);
        }
        catch
        {
            return Task.FromResult(0L);
        }
    }

    public Task ClearIndexAsync(string indexName)
    {
        _indexManager.ClearIndex(indexName);
        _vectorSearchService.DeleteIndex(indexName);
        return Task.CompletedTask;
    }

    private static string ConvertToString(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? "",
                JsonValueKind.Number => je.GetRawText(),
                _ => je.GetRawText()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    private static float[]? ConvertToFloatArray(object? value)
    {
        if (value == null)
            return null;

        if (value is float[] floatArray)
            return floatArray;

        if (value is double[] doubleArray)
            return doubleArray.Select(d => (float)d).ToArray();

        // Check for JsonElement BEFORE IEnumerable<object> since JsonElement can match IEnumerable
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();
        }

        // Handle List<JsonElement> (from deserialization)
        if (value is IEnumerable<JsonElement> jsonElements)
        {
            return jsonElements.Select(e => e.GetSingle()).ToArray();
        }

        // Handle generic object lists - need to check element types
        if (value is IEnumerable<object> objList)
        {
            var list = objList.ToList();
            if (list.Count == 0)
                return Array.Empty<float>();
            
            // Check if elements are JsonElement (boxed)
            if (list[0] is JsonElement firstElement)
            {
                return list.Select(o => ((JsonElement)o).GetSingle()).ToArray();
            }
            
            // Try to convert each element individually
            return list.Select(o => ConvertToFloat(o)).ToArray();
        }

        return null;
    }
    
    private static float ConvertToFloat(object value)
    {
        if (value is JsonElement je)
        {
            return je.GetSingle();
        }
        return Convert.ToSingle(value);
    }
}
