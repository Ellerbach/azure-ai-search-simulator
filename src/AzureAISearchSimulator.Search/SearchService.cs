using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Microsoft.Extensions.Logging;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search.Hnsw;
using System.Text.Json;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Service for searching documents using Lucene.NET with vector search support.
/// </summary>
public class SearchService : ISearchService
{
    private readonly ILogger<SearchService> _logger;
    private readonly LuceneIndexManager _indexManager;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly IIndexService _indexService;

    public SearchService(
        ILogger<SearchService> logger,
        LuceneIndexManager indexManager,
        IVectorSearchService vectorSearchService,
        IIndexService indexService)
    {
        _logger = logger;
        _indexManager = indexManager;
        _vectorSearchService = vectorSearchService;
        _indexService = indexService;
    }

    public async Task<SearchResponse> SearchAsync(string indexName, SearchRequest request)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var debugModes = ParseDebugModes(request.Debug);
        var debugMode = debugModes.Count > 0;
        var debugVector = debugModes.Contains("vector") || debugModes.Contains("all");
        
        var index = await _indexService.GetIndexAsync(indexName);
        if (index == null)
        {
            throw new KeyNotFoundException($"Index '{indexName}' not found");
        }

        var response = new SearchResponse
        {
            ODataContext = $"https://localhost/indexes('{indexName}')/$metadata#docs(*)",
            Value = new List<SearchResult>()
        };

        // Initialize debug info if debug mode is enabled
        DebugInfo? debugInfo = null;
        if (debugMode)
        {
            debugInfo = new DebugInfo
            {
                SearchableFields = index.Fields
                    .Where(f => f.Searchable == true)
                    .Select(f => f.Name)
                    .ToList()
            };
            _logger.LogInformation("[DEBUG] Search request on index '{IndexName}': search='{Search}', filter='{Filter}', queryType='{QueryType}', debugModes='{DebugModes}'",
                indexName, request.Search, request.Filter, request.QueryType, request.Debug);
        }

        var searcher = _indexManager.GetSearcher(indexName);
        var keyField = LuceneDocumentMapper.GetKeyFieldName(index);

        // Build the Lucene query
        Query? textQuery = null;
        Query? filterQuery = null;
        var textSearchStopwatch = new System.Diagnostics.Stopwatch();
        
        if (!string.IsNullOrWhiteSpace(request.Search) && request.Search != "*")
        {
            textQuery = BuildTextQuery(indexName, index, request);
            if (debugMode && debugInfo != null)
            {
                debugInfo.ParsedQuery = textQuery?.ToString();
                _logger.LogInformation("[DEBUG] Parsed text query: {ParsedQuery}", debugInfo.ParsedQuery);
            }
        }

        // Build filter query
        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            filterQuery = BuildFilterQuery(index, request.Filter);
            if (debugMode && debugInfo != null)
            {
                debugInfo.ParsedFilter = filterQuery?.ToString();
                _logger.LogInformation("[DEBUG] Parsed filter query: {ParsedFilter}", debugInfo.ParsedFilter);
            }
        }

        // Handle vector search
        var vectorSearchStopwatch = new System.Diagnostics.Stopwatch();
        Dictionary<string, double>? vectorScores = null;
        if (request.VectorQueries != null && request.VectorQueries.Any())
        {
            vectorSearchStopwatch.Start();
            vectorScores = ExecuteVectorSearch(indexName, request.VectorQueries);
            vectorSearchStopwatch.Stop();
            
            if (debugMode && debugInfo != null)
            {
                debugInfo.VectorSearchTimeMs = vectorSearchStopwatch.Elapsed.TotalMilliseconds;
                debugInfo.VectorMatchCount = vectorScores.Count;
                _logger.LogInformation("[DEBUG] Vector search completed in {TimeMs}ms, found {Count} matches",
                    debugInfo.VectorSearchTimeMs, debugInfo.VectorMatchCount);
                
                // Log top vector scores
                var topVectorResults = vectorScores.OrderByDescending(x => x.Value).Take(5);
                foreach (var (docKey, score) in topVectorResults)
                {
                    _logger.LogInformation("[DEBUG] Vector result: key='{Key}', score={Score:F4}", docKey, score);
                }
            }
        }

        // Execute Lucene text search
        var topN = (request.Skip ?? 0) + (request.Top ?? 50);
        TopDocs? topDocs = null;
        
        textSearchStopwatch.Start();
        if (textQuery != null)
        {
            // Apply OData filter if present
            Query finalQuery = textQuery;
            if (filterQuery != null)
            {
                var bq = new BooleanQuery();
                bq.Add(textQuery, Occur.MUST);
                bq.Add(filterQuery, Occur.MUST);
                finalQuery = bq;
            }

            var sort = BuildSort(request.OrderBy, index);
            topDocs = sort != null
                ? searcher.Search(finalQuery, topN, sort)
                : searcher.Search(finalQuery, topN);
        }
        else if (filterQuery != null)
        {
            // Filter-only search
            var sort = BuildSort(request.OrderBy, index);
            topDocs = sort != null
                ? searcher.Search(filterQuery, topN, sort)
                : searcher.Search(filterQuery, topN);
        }
        else if (vectorScores == null || !vectorScores.Any())
        {
            // Match all documents
            var matchAllQuery = new MatchAllDocsQuery();
            var sort = BuildSort(request.OrderBy, index);
            topDocs = sort != null
                ? searcher.Search(matchAllQuery, topN, sort)
                : searcher.Search(matchAllQuery, topN);
        }
        textSearchStopwatch.Stop();

        if (debugMode && debugInfo != null)
        {
            debugInfo.TextSearchTimeMs = textSearchStopwatch.Elapsed.TotalMilliseconds;
            debugInfo.TextMatchCount = topDocs?.TotalHits ?? 0;
            _logger.LogInformation("[DEBUG] Text search completed in {TimeMs}ms, found {Count} matches",
                debugInfo.TextSearchTimeMs, debugInfo.TextMatchCount);
            
            // Log top text scores
            if (topDocs != null)
            {
                foreach (var scoreDoc in topDocs.ScoreDocs.Take(5))
                {
                    var doc = searcher.Doc(scoreDoc.Doc);
                    var key = doc.Get(keyField);
                    _logger.LogInformation("[DEBUG] Text result: key='{Key}', score={Score:F4}", key, scoreDoc.Score);
                }
            }
        }

        // Determine if this is hybrid search
        var isHybridSearch = request.VectorQueries != null && request.VectorQueries.Any() && 
                            !string.IsNullOrWhiteSpace(request.Search) && request.Search != "*";
        
        if (debugMode && debugInfo != null)
        {
            debugInfo.IsHybridSearch = isHybridSearch;
            debugInfo.ScoreFusionMethod = isHybridSearch ? "WeightedAverage" : (vectorScores != null ? "VectorOnly" : "TextOnly");
            _logger.LogInformation("[DEBUG] Search mode: {Mode}, Hybrid: {IsHybrid}", 
                debugInfo.ScoreFusionMethod, isHybridSearch);
        }

        // Combine text and vector results (with debug tracking)
        var combinedResults = CombineResultsWithDebug(
            searcher, 
            keyField, 
            topDocs, 
            vectorScores, 
            request,
            debugVector);

        // Apply skip and top
        var finalResults = combinedResults.Results
            .Skip(request.Skip ?? 0)
            .Take(request.Top ?? 50)
            .ToList();

        // Parse selected fields
        var selectedFields = ParseSelect(request.Select);

        // Build response
        foreach (var (docId, score, docKey) in finalResults)
        {
            var luceneDoc = docId >= 0 ? searcher.Doc(docId) : null;
            var searchResult = new SearchResult();

            // Set score first to match Azure property ordering (@search.score before document fields)
            // Replace NaN with 1.0 (occurs when Lucene uses sorted search without scoring, e.g. wildcard/filter queries)
            searchResult.Score = double.IsNaN(score) ? 1.0 : score;
            
            if (luceneDoc != null)
            {
                var doc = LuceneDocumentMapper.FromLuceneDocument(luceneDoc, selectedFields);
                foreach (var kvp in doc)
                {
                    searchResult[kvp.Key] = kvp.Value;
                }
            }
            
            // Add highlights if requested
            if (textQuery != null && request.Highlight != null && luceneDoc != null)
            {
                var highlights = GetHighlights(
                    indexName, index, textQuery, luceneDoc, 
                    request.HighlightPreTag ?? "<em>",
                    request.HighlightPostTag ?? "</em>");
                if (highlights.Any())
                {
                    searchResult.Highlights = highlights;
                }
            }

            // Add per-document debug info (official @search.documentDebugInfo)
            if (debugVector && combinedResults.DocumentDebugInfos != null)
            {
                if (combinedResults.DocumentDebugInfos.TryGetValue(docKey, out var docDebugInfo))
                {
                    searchResult.DocumentDebugInfo = docDebugInfo;
                }
            }

            response.Value.Add(searchResult);
        }

        // Calculate facets if requested
        if (request.Facets != null && request.Facets.Any())
        {
            response.SearchFacets = CalculateFacets(searcher, index, request.Facets, textQuery, request.Filter);
        }

        // Include count if requested
        if (request.Count == true)
        {
            if (topDocs != null)
            {
                response.ODataCount = topDocs.TotalHits;
            }
            else if (vectorScores != null)
            {
                response.ODataCount = vectorScores.Count;
            }
        }

        // Search coverage - only include when minimumCoverage is specified (Azure behavior)
        if (request.MinimumCoverage.HasValue)
        {
            response.SearchCoverage = 100.0;
        }

        // Finalize debug info
        totalStopwatch.Stop();
        if (debugMode && debugInfo != null)
        {
            debugInfo.TotalTimeMs = totalStopwatch.Elapsed.TotalMilliseconds;
            debugInfo.IsHybridSearch = isHybridSearch;
            debugInfo.ScoreFusionMethod = isHybridSearch ? "WeightedAverage" : (vectorScores != null ? "VectorOnly" : "TextOnly");
            
            response.SearchDebug = debugInfo;
            
            _logger.LogInformation("[DEBUG] Search completed in {TotalMs}ms. Results: {Count}", 
                debugInfo.TotalTimeMs, response.Value.Count);
            
            // Log final result scores
            if (debugVector)
            {
                foreach (var result in finalResults)
                {
                    if (combinedResults.DocumentDebugInfos != null && 
                        combinedResults.DocumentDebugInfos.TryGetValue(result.Key, out var docDebug))
                    {
                        _logger.LogInformation(
                            "[DEBUG] Final result: key='{Key}', finalScore={FinalScore:F4}, textScore={TextScore}, vectorSimilarity={VectorSim}",
                            result.Key, result.Score, 
                            docDebug.Vectors?.Subscores?.Text?.SearchScore,
                            docDebug.Vectors?.Subscores?.Vectors?.Values.FirstOrDefault()?.VectorSimilarity);
                    }
                }
            }
        }

        _logger.LogDebug(
            "Search on {IndexName}: query='{Query}', filter='{Filter}', returned {Count} results",
            indexName, request.Search, request.Filter, response.Value.Count);

        return response;
    }

    private (IEnumerable<(int DocId, double Score, string Key)> Results, Dictionary<string, DocumentDebugInfo>? DocumentDebugInfos) CombineResultsWithDebug(
        IndexSearcher searcher,
        string keyField,
        TopDocs? textResults,
        Dictionary<string, double>? vectorScores,
        SearchRequest request,
        bool debugVector)
    {
        var combined = new Dictionary<string, (int DocId, double TextScore, double VectorScore)>();
        var textRanks = new Dictionary<string, int>();
        var vectorRanks = new Dictionary<string, int>();

        // Add text search results and track ranks
        if (textResults != null)
        {
            var rank = 1;
            foreach (var scoreDoc in textResults.ScoreDocs)
            {
                var doc = searcher.Doc(scoreDoc.Doc);
                var key = doc.Get(keyField);
                if (key != null)
                {
                    combined[key] = (scoreDoc.Doc, scoreDoc.Score, 0);
                    textRanks[key] = rank++;
                }
            }
        }

        // Add/merge vector search results and track ranks
        if (vectorScores != null)
        {
            var rank = 1;
            foreach (var (key, vectorScore) in vectorScores.OrderByDescending(x => x.Value))
            {
                vectorRanks[key] = rank++;
                
                if (combined.TryGetValue(key, out var existing))
                {
                    combined[key] = (existing.DocId, existing.TextScore, vectorScore);
                }
                else
                {
                    // Find doc by key
                    var termQuery = new TermQuery(new Term(keyField, key));
                    var topDocs = searcher.Search(termQuery, 1);
                    if (topDocs.TotalHits > 0)
                    {
                        combined[key] = (topDocs.ScoreDocs[0].Doc, 0, vectorScore);
                    }
                }
            }
        }

        // Determine vector field names for debug
        var vectorFieldNames = request.VectorQueries?
            .Where(vq => vq.Fields != null)
            .SelectMany(vq => vq.Fields!.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()))
            .Distinct()
            .ToList() ?? new List<string>();

        // Calculate final scores based on search mode
        var useHybrid = request.VectorQueries != null && request.VectorQueries.Any() && 
                        !string.IsNullOrWhiteSpace(request.Search) && request.Search != "*";
        
        Dictionary<string, DocumentDebugInfo>? documentDebugInfos = null;
        if (debugVector)
        {
            documentDebugInfos = new Dictionary<string, DocumentDebugInfo>();
        }

        var results = combined
            .Select(kvp =>
            {
                var (docId, textScore, vectorScore) = kvp.Value;
                double finalScore;
                
                if (useHybrid)
                {
                    // Hybrid: weighted average
                    finalScore = (textScore * 0.5) + (vectorScore * 0.5);
                }
                else if (vectorScores != null && vectorScores.Any())
                {
                    // Vector only
                    finalScore = vectorScore;
                }
                else
                {
                    // Text only
                    finalScore = textScore;
                }

                // Build per-document debug info matching official structure
                if (debugVector && documentDebugInfos != null)
                {
                    var subscores = new QueryResultDocumentSubscores();

                    // Text subscore
                    if (textScore > 0)
                    {
                        subscores.Text = new TextResult { SearchScore = textScore };
                    }

                    // Vector subscores - one entry per vector field
                    if (vectorScore > 0 && vectorFieldNames.Any())
                    {
                        subscores.Vectors = new Dictionary<string, SingleVectorFieldResult>();
                        foreach (var fieldName in vectorFieldNames)
                        {
                            subscores.Vectors[fieldName] = new SingleVectorFieldResult
                            {
                                SearchScore = finalScore, // The @search.score for a pure vector query
                                VectorSimilarity = vectorScore // The raw cosine similarity
                            };
                        }
                    }
                    else if (vectorScore > 0)
                    {
                        // Fallback when no field name specified
                        subscores.Vectors = new Dictionary<string, SingleVectorFieldResult>
                        {
                            ["vector"] = new SingleVectorFieldResult
                            {
                                SearchScore = finalScore,
                                VectorSimilarity = vectorScore
                            }
                        };
                    }

                    // Document boost (not used yet, placeholder for scoring profiles)
                    subscores.DocumentBoost = 1.0;

                    documentDebugInfos[kvp.Key] = new DocumentDebugInfo
                    {
                        Vectors = new VectorsDebugInfo
                        {
                            Subscores = subscores
                        }
                    };
                }

                return (DocId: docId, Score: finalScore, Key: kvp.Key);
            })
            .OrderByDescending(r => r.Score);

        return (results, documentDebugInfos);
    }

    /// <summary>
    /// Parse the debug mode string into a set of active debug modes.
    /// Supports: "disabled", "semantic", "vector", "queryRewrites", "innerHits", "all".
    /// Can be combined with '|', e.g. "semantic|vector".
    /// </summary>
    private static HashSet<string> ParseDebugModes(string? debug)
    {
        var modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(debug) || debug.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return modes;
        }

        foreach (var part in debug.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim().ToLowerInvariant();
            if (trimmed != "disabled")
            {
                modes.Add(trimmed);
            }
        }

        return modes;
    }

    private Query BuildTextQuery(string indexName, SearchIndex schema, SearchRequest request)
    {
        var searchableFields = schema.Fields
            .Where(f => f.Searchable == true)
            .Select(f => f.Name)
            .ToArray();

        if (!searchableFields.Any())
        {
            return new MatchAllDocsQuery();
        }

        var analyzer = _indexManager.GetAnalyzer(indexName);
        var parser = new MultiFieldQueryParser(
            LuceneDocumentMapper.AppLuceneVersion,
            searchableFields,
            analyzer);

        // Configure query parser based on queryType
        if (request.QueryType?.ToLowerInvariant() == "full")
        {
            parser.DefaultOperator = Operator.AND;
            parser.AllowLeadingWildcard = true;
        }
        else
        {
            // Simple query syntax
            parser.DefaultOperator = Operator.OR;
        }

        try
        {
            var searchText = EscapeForSimpleQuery(request.Search ?? "*", request.QueryType);
            return parser.Parse(searchText);
        }
        catch (ParseException ex)
        {
            _logger.LogWarning(ex, "Failed to parse query '{Query}', falling back to match all", request.Search);
            return new MatchAllDocsQuery();
        }
    }

    private string EscapeForSimpleQuery(string query, string? queryType)
    {
        if (queryType?.ToLowerInvariant() == "full")
        {
            // Full Lucene syntax - minimal escaping
            return query;
        }

        // Simple syntax - escape special characters but preserve basic operators
        var escaped = query
            .Replace("\\", "\\\\")
            .Replace(":", "\\:")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("^", "\\^")
            .Replace("~", "\\~");

        // Preserve AND, OR, NOT operators
        return escaped;
    }

    private Query? BuildFilterQuery(SearchIndex schema, string filter)
    {
        // Simple OData filter parser - handles basic cases
        // Full OData support would require a proper parser
        
        try
        {
            var boolQuery = new BooleanQuery();
            
            // Split by 'and' (case insensitive)
            var parts = filter.Split(new[] { " and ", " AND " }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                var query = ParseFilterExpression(schema, trimmed);
                if (query != null)
                {
                    boolQuery.Add(query, Occur.MUST);
                }
            }

            return boolQuery.Clauses.Count > 0 ? boolQuery : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse filter '{Filter}'", filter);
            return null;
        }
    }

    private Query? ParseFilterExpression(SearchIndex schema, string expression)
    {
        // Handle eq expressions: field eq 'value' or field eq value
        var eqMatch = System.Text.RegularExpressions.Regex.Match(
            expression, @"(\w+)\s+eq\s+(?:'([^']*)'|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (eqMatch.Success)
        {
            var fieldName = eqMatch.Groups[1].Value;
            var value = !string.IsNullOrEmpty(eqMatch.Groups[2].Value) 
                ? eqMatch.Groups[2].Value 
                : eqMatch.Groups[3].Value;
            
            return new TermQuery(new Term(fieldName, value));
        }

        // Handle ne expressions: field ne 'value'
        var neMatch = System.Text.RegularExpressions.Regex.Match(
            expression, @"(\w+)\s+ne\s+(?:'([^']*)'|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (neMatch.Success)
        {
            var fieldName = neMatch.Groups[1].Value;
            var value = !string.IsNullOrEmpty(neMatch.Groups[2].Value)
                ? neMatch.Groups[2].Value
                : neMatch.Groups[3].Value;

            var boolQuery = new BooleanQuery
            {
                { new MatchAllDocsQuery(), Occur.MUST },
                { new TermQuery(new Term(fieldName, value)), Occur.MUST_NOT }
            };
            return boolQuery;
        }

        // Handle gt, lt, ge, le for numeric/date fields
        var rangeMatch = System.Text.RegularExpressions.Regex.Match(
            expression, @"(\w+)\s+(gt|lt|ge|le)\s+(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (rangeMatch.Success)
        {
            var fieldName = rangeMatch.Groups[1].Value;
            var op = rangeMatch.Groups[2].Value.ToLowerInvariant();
            var value = rangeMatch.Groups[3].Value;

            var field = schema.Fields.FirstOrDefault(f => 
                f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            
            if (field != null && IsNumericType(field.Type))
            {
                return BuildNumericRangeQuery(fieldName, op, value, field.Type);
            }
        }

        // Handle search.in(field, 'val1,val2,val3')
        var searchInMatch = System.Text.RegularExpressions.Regex.Match(
            expression, @"search\.in\((\w+),\s*'([^']*)'\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (searchInMatch.Success)
        {
            var fieldName = searchInMatch.Groups[1].Value;
            var values = searchInMatch.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            var boolQuery = new BooleanQuery();
            foreach (var val in values)
            {
                boolQuery.Add(new TermQuery(new Term(fieldName, val.Trim())), Occur.SHOULD);
            }
            return boolQuery;
        }

        _logger.LogWarning("Unrecognized filter expression: {Expression}", expression);
        return null;
    }

    private bool IsNumericType(string edmType)
    {
        return edmType.ToLowerInvariant() switch
        {
            "edm.int32" => true,
            "edm.int64" => true,
            "edm.double" => true,
            "edm.datetimeoffset" => true,
            _ => false
        };
    }

    private Query BuildNumericRangeQuery(string fieldName, string op, string value, string edmType)
    {
        // Handle double/single types
        if (edmType.Equals("edm.double", StringComparison.OrdinalIgnoreCase) ||
            edmType.Equals("edm.single", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
            {
                return op switch
                {
                    "gt" => NumericRangeQuery.NewDoubleRange(fieldName, doubleValue, double.MaxValue, false, true),
                    "ge" => NumericRangeQuery.NewDoubleRange(fieldName, doubleValue, double.MaxValue, true, true),
                    "lt" => NumericRangeQuery.NewDoubleRange(fieldName, double.MinValue, doubleValue, true, false),
                    "le" => NumericRangeQuery.NewDoubleRange(fieldName, double.MinValue, doubleValue, true, true),
                    _ => new MatchAllDocsQuery()
                };
            }
        }

        // Handle integer types
        if (long.TryParse(value, out var longValue))
        {
            return op switch
            {
                "gt" => NumericRangeQuery.NewInt64Range(fieldName, longValue + 1, long.MaxValue, true, true),
                "ge" => NumericRangeQuery.NewInt64Range(fieldName, longValue, long.MaxValue, true, true),
                "lt" => NumericRangeQuery.NewInt64Range(fieldName, long.MinValue, longValue - 1, true, true),
                "le" => NumericRangeQuery.NewInt64Range(fieldName, long.MinValue, longValue, true, true),
                _ => new MatchAllDocsQuery()
            };
        }

        // Fallback: try parsing as double even for non-double types (e.g. "4.0" on an int field)
        if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var fallbackDouble))
        {
            var fallbackLong = (long)fallbackDouble;
            return op switch
            {
                "gt" => NumericRangeQuery.NewInt64Range(fieldName, fallbackLong + 1, long.MaxValue, true, true),
                "ge" => NumericRangeQuery.NewInt64Range(fieldName, fallbackLong, long.MaxValue, true, true),
                "lt" => NumericRangeQuery.NewInt64Range(fieldName, long.MinValue, fallbackLong - 1, true, true),
                "le" => NumericRangeQuery.NewInt64Range(fieldName, long.MinValue, fallbackLong, true, true),
                _ => new MatchAllDocsQuery()
            };
        }

        return new MatchAllDocsQuery();
    }

    private Sort? BuildSort(string? orderBy, SearchIndex index)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return null;
        }

        var sortFields = new List<SortField>();
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fieldName = tokens[0];
            var reverse = tokens.Length > 1 && 
                tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            if (fieldName.Equals("search.score()", StringComparison.OrdinalIgnoreCase))
            {
                sortFields.Add(SortField.FIELD_SCORE);
            }
            else
            {
                // Determine the correct sort field type based on the index schema
                var sortFieldType = GetSortFieldType(fieldName, index);
                sortFields.Add(new SortField(fieldName + "_sort", sortFieldType, reverse));
            }
        }

        return sortFields.Any() ? new Sort(sortFields.ToArray()) : null;
    }

    private static SortFieldType GetSortFieldType(string fieldName, SearchIndex index)
    {
        var field = index.Fields?.FirstOrDefault(f => 
            f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        
        if (field == null)
        {
            return SortFieldType.STRING;
        }

        // Map Azure Search field types to Lucene sort field types
        return field.Type?.ToLowerInvariant() switch
        {
            "edm.int32" => SortFieldType.INT32,
            "edm.int64" => SortFieldType.INT64,
            "edm.double" => SortFieldType.DOUBLE,
            "edm.single" => SortFieldType.SINGLE,
            "edm.datetimeoffset" => SortFieldType.INT64, // Dates stored as ticks
            _ => SortFieldType.STRING
        };
    }

    private Dictionary<string, double> ExecuteVectorSearch(string indexName, List<VectorQuery> vectors)
    {
        var combinedScores = new Dictionary<string, double>();

        foreach (var vectorQuery in vectors)
        {
            if (vectorQuery.Vector == null || !vectorQuery.Vector.Any())
            {
                continue;
            }

            var results = _vectorSearchService.Search(
                indexName,
                vectorQuery.Fields ?? string.Empty,
                vectorQuery.Vector.ToArray(),
                vectorQuery.K > 0 ? vectorQuery.K : 50);

            foreach (var result in results)
            {
                if (combinedScores.TryGetValue(result.DocumentId, out var existing))
                {
                    // Take max similarity if multiple vector queries
                    combinedScores[result.DocumentId] = Math.Max(existing, result.Score);
                }
                else
                {
                    combinedScores[result.DocumentId] = result.Score;
                }
            }
        }

        return combinedScores;
    }

    private IEnumerable<string>? ParseSelect(string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
        {
            return null;
        }

        return select.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    private Dictionary<string, List<string>> GetHighlights(
        string indexName,
        SearchIndex schema,
        Query query,
        Document doc,
        string preTag,
        string postTag)
    {
        var highlights = new Dictionary<string, List<string>>();
        
        try
        {
            var analyzer = _indexManager.GetAnalyzer(indexName);
            var scorer = new QueryScorer(query);
            var formatter = new SimpleHTMLFormatter(preTag, postTag);
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new SimpleFragmenter(150)
            };

            foreach (var field in schema.Fields.Where(f => f.Searchable == true))
            {
                var text = doc.Get(field.Name);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var tokenStream = analyzer.GetTokenStream(field.Name, new StringReader(text));
                var fragments = highlighter.GetBestFragments(tokenStream, text, 3);
                
                if (fragments.Length > 0)
                {
                    highlights[field.Name] = fragments.ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating highlights");
        }

        return highlights;
    }

    public async Task<SuggestResponse> SuggestAsync(string indexName, SuggestRequest request)
    {
        var index = await _indexService.GetIndexAsync(indexName);
        if (index == null)
        {
            throw new KeyNotFoundException($"Index '{indexName}' not found");
        }

        var response = new SuggestResponse
        {
            ODataContext = $"https://localhost/indexes('{indexName}')/$metadata#docs(*)",
            Value = new List<SuggestResult>()
        };

        // Simple prefix-based suggestion using Lucene
        var searcher = _indexManager.GetSearcher(indexName);
        var searchableFields = index.Fields
            .Where(f => f.Searchable == true)
            .Select(f => f.Name)
            .ToArray();

        if (!searchableFields.Any() || string.IsNullOrWhiteSpace(request.Search))
        {
            return response;
        }

        // Build prefix query
        var boolQuery = new BooleanQuery();
        foreach (var field in searchableFields)
        {
            boolQuery.Add(new PrefixQuery(new Term(field, request.Search.ToLowerInvariant())), Occur.SHOULD);
        }

        var topDocs = searcher.Search(boolQuery, request.Top ?? 5);
        var selectedFields = ParseSelect(request.Select);

        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var result = new SuggestResult();
            
            var docFields = LuceneDocumentMapper.FromLuceneDocument(doc, selectedFields);
            foreach (var kvp in docFields)
            {
                result[kvp.Key] = kvp.Value;
            }

            // Find the matching text for @search.text
            foreach (var field in searchableFields)
            {
                var text = doc.Get(field);
                if (text != null && text.ToLowerInvariant().Contains(request.Search.ToLowerInvariant()))
                {
                    result.Text = text;
                    break;
                }
            }

            response.Value.Add(result);
        }

        return response;
    }

    public async Task<AutocompleteResponse> AutocompleteAsync(string indexName, AutocompleteRequest request)
    {
        var index = await _indexService.GetIndexAsync(indexName);
        if (index == null)
        {
            throw new KeyNotFoundException($"Index '{indexName}' not found");
        }

        var response = new AutocompleteResponse
        {
            ODataContext = $"https://localhost/indexes('{indexName}')/$metadata#docs(*)",
            Value = new List<AutocompleteItem>()
        };

        if (string.IsNullOrWhiteSpace(request.Search))
        {
            return response;
        }

        // Simple term-based autocomplete
        var reader = _indexManager.GetSearcher(indexName).IndexReader;
        var searchableFields = index.Fields
            .Where(f => f.Searchable == true)
            .Select(f => f.Name)
            .ToArray();

        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchLower = request.Search.ToLowerInvariant();

        foreach (var field in searchableFields)
        {
            var terms = MultiFields.GetTerms(reader, field);
            if (terms == null) continue;

            var termsEnum = terms.GetEnumerator();
            while (termsEnum.MoveNext())
            {
                var termText = termsEnum.Term.Utf8ToString();
                if (termText.ToLowerInvariant().StartsWith(searchLower))
                {
                    suggestions.Add(termText);
                    if (suggestions.Count >= (request.Top ?? 5))
                    {
                        break;
                    }
                }
            }

            if (suggestions.Count >= (request.Top ?? 5))
            {
                break;
            }
        }

        foreach (var suggestion in suggestions.Take(request.Top ?? 5))
        {
            response.Value.Add(new AutocompleteItem
            {
                Text = suggestion,
                QueryPlusText = suggestion
            });
        }

        return response;
    }

    /// <summary>
    /// Calculates facets for the search results.
    /// </summary>
    private Dictionary<string, List<FacetResult>> CalculateFacets(
        IndexSearcher searcher,
        SearchIndex schema,
        List<string> facetSpecs,
        Query? textQuery,
        string? filter)
    {
        var facets = new Dictionary<string, List<FacetResult>>();
        var reader = searcher.IndexReader;

        foreach (var facetSpec in facetSpecs)
        {
            var parsed = ParseFacetSpec(facetSpec);
            var fieldName = parsed.FieldName;
            var count = parsed.Count ?? 10;
            var interval = parsed.Interval;

            // Verify field is facetable
            var field = schema.Fields.FirstOrDefault(f => 
                f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            
            if (field == null || field.Facetable != true)
            {
                _logger.LogWarning("Field '{FieldName}' is not facetable, skipping", fieldName);
                continue;
            }

            if (interval.HasValue)
            {
                // Range/interval facet (for numeric fields)
                facets[fieldName] = CalculateIntervalFacet(reader, fieldName, field.Type, interval.Value, count);
            }
            else
            {
                // Value facet
                facets[fieldName] = CalculateValueFacet(reader, fieldName, count);
            }
        }

        return facets;
    }

    /// <summary>
    /// Parses a facet specification like "category,count:5" or "rating,interval:1".
    /// </summary>
    private (string FieldName, int? Count, double? Interval) ParseFacetSpec(string facetSpec)
    {
        var parts = facetSpec.Split(',');
        var fieldName = parts[0].Trim();
        int? count = null;
        double? interval = null;

        foreach (var part in parts.Skip(1))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("count:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed.Substring(6), out var c))
                {
                    count = c;
                }
            }
            else if (trimmed.StartsWith("interval:", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(trimmed.Substring(9), System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out var i))
                {
                    interval = i;
                }
            }
        }

        return (fieldName, count, interval);
    }

    /// <summary>
    /// Calculates value facets by counting unique values.
    /// </summary>
    private List<FacetResult> CalculateValueFacet(IndexReader reader, string fieldName, int maxCount)
    {
        var valueCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Iterate through all terms in the field
        var terms = MultiFields.GetTerms(reader, fieldName);
        if (terms != null)
        {
            var termsEnum = terms.GetEnumerator();
            while (termsEnum.MoveNext())
            {
                var termText = termsEnum.Term.Utf8ToString();
                var docFreq = termsEnum.DocFreq;
                
                if (!string.IsNullOrWhiteSpace(termText))
                {
                    valueCounts[termText] = docFreq;
                }
            }
        }

        // Return top N by count
        return valueCounts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .Take(maxCount)
            .Select(kvp => new FacetResult
            {
                Value = kvp.Key,
                Count = kvp.Value
            })
            .ToList();
    }

    /// <summary>
    /// Calculates interval facets for numeric fields.
    /// </summary>
    private List<FacetResult> CalculateIntervalFacet(
        IndexReader reader, 
        string fieldName, 
        string fieldType, 
        double interval, 
        int maxCount)
    {
        var rangeCounts = new Dictionary<(double From, double To), long>();

        // Read all numeric values from the index
        var values = new List<double>();
        
        for (int i = 0; i < reader.MaxDoc; i++)
        {
            if (reader.HasDeletions && !reader.Document(i).Fields.Any())
                continue;

            var doc = reader.Document(i);
            var field = doc.GetField(fieldName);
            
            if (field != null)
            {
                double? numValue = fieldType switch
                {
                    "Edm.Double" => field.GetDoubleValue(),
                    "Edm.Int32" => field.GetInt32Value(),
                    "Edm.Int64" => field.GetInt64Value(),
                    _ => null
                };

                if (numValue.HasValue)
                {
                    values.Add(numValue.Value);
                }
            }
        }

        if (!values.Any())
        {
            return new List<FacetResult>();
        }

        // Calculate ranges based on interval
        var min = values.Min();
        var max = values.Max();
        
        // Round down to nearest interval
        var rangeStart = Math.Floor(min / interval) * interval;
        var rangeEnd = rangeStart + interval;

        while (rangeStart <= max)
        {
            var from = rangeStart;
            var to = rangeEnd;
            var count = values.Count(v => v >= from && v < to);
            
            if (count > 0)
            {
                rangeCounts[(from, to)] = count;
            }

            rangeStart = rangeEnd;
            rangeEnd += interval;
        }

        // Return ranges
        return rangeCounts
            .OrderBy(kvp => kvp.Key.From)
            .Take(maxCount)
            .Select(kvp => new FacetResult
            {
                From = kvp.Key.From,
                To = kvp.Key.To,
                Count = kvp.Value
            })
            .ToList();
    }
}
