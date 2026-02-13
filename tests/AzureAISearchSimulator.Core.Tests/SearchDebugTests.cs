using System.Text.Json;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Tests for the search debug feature: debug models, JSON serialization,
/// SearchRequest.Debug property, and per-document debug info.
/// </summary>
public class SearchDebugTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    #region SearchRequest.Debug Property Tests

    [Fact]
    public void SearchRequest_Debug_DefaultsToNull()
    {
        var request = new SearchRequest();
        Assert.Null(request.Debug);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("semantic")]
    [InlineData("vector")]
    [InlineData("queryRewrites")]
    [InlineData("innerHits")]
    [InlineData("all")]
    public void SearchRequest_Debug_AcceptsSingleModeStrings(string mode)
    {
        var request = new SearchRequest { Debug = mode };
        Assert.Equal(mode, request.Debug);
    }

    [Theory]
    [InlineData("semantic|vector")]
    [InlineData("vector|queryRewrites|innerHits")]
    [InlineData("all|semantic")]
    public void SearchRequest_Debug_AcceptsCombinedModes(string modes)
    {
        var request = new SearchRequest { Debug = modes };
        Assert.Equal(modes, request.Debug);
    }

    [Fact]
    public void SearchRequest_Debug_SerializesToJson()
    {
        var request = new SearchRequest
        {
            Search = "test",
            Debug = "vector|semantic"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        Assert.Contains("\"debug\"", json);
        Assert.Contains("\"vector|semantic\"", json);
    }

    [Fact]
    public void SearchRequest_Debug_DeserializesFromJson()
    {
        var json = """{"search":"test","debug":"all"}""";
        var request = JsonSerializer.Deserialize<SearchRequest>(json, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("all", request!.Debug);
    }

    [Fact]
    public void SearchRequest_Debug_NullOmittedFromJson()
    {
        var request = new SearchRequest { Search = "test" };
        var json = JsonSerializer.Serialize(request, JsonOptions);

        Assert.DoesNotContain("\"debug\"", json);
    }

    #endregion

    #region DebugInfo Model Tests

    [Fact]
    public void DebugInfo_DefaultProperties_AreNull()
    {
        var debugInfo = new DebugInfo();

        Assert.Null(debugInfo.QueryRewrites);
        Assert.Null(debugInfo.ParsedQuery);
        Assert.Null(debugInfo.ParsedFilter);
        Assert.Null(debugInfo.IsHybridSearch);
        Assert.Null(debugInfo.TextSearchTimeMs);
        Assert.Null(debugInfo.VectorSearchTimeMs);
        Assert.Null(debugInfo.TotalTimeMs);
        Assert.Null(debugInfo.TextMatchCount);
        Assert.Null(debugInfo.VectorMatchCount);
        Assert.Null(debugInfo.ScoreFusionMethod);
        Assert.Null(debugInfo.SearchableFields);
    }

    [Fact]
    public void DebugInfo_SimulatorProperties_CanBeSet()
    {
        var debugInfo = new DebugInfo
        {
            ParsedQuery = "+title:hotel +content:luxury",
            ParsedFilter = "rating:[4 TO *]",
            IsHybridSearch = true,
            TextSearchTimeMs = 12.5,
            VectorSearchTimeMs = 8.3,
            TotalTimeMs = 25.1,
            TextMatchCount = 42,
            VectorMatchCount = 10,
            ScoreFusionMethod = "WeightedAverage",
            SearchableFields = new List<string> { "title", "content", "description" }
        };

        Assert.Equal("+title:hotel +content:luxury", debugInfo.ParsedQuery);
        Assert.Equal("rating:[4 TO *]", debugInfo.ParsedFilter);
        Assert.True(debugInfo.IsHybridSearch);
        Assert.Equal(12.5, debugInfo.TextSearchTimeMs);
        Assert.Equal(8.3, debugInfo.VectorSearchTimeMs);
        Assert.Equal(25.1, debugInfo.TotalTimeMs);
        Assert.Equal(42, debugInfo.TextMatchCount);
        Assert.Equal(10, debugInfo.VectorMatchCount);
        Assert.Equal("WeightedAverage", debugInfo.ScoreFusionMethod);
        Assert.Equal(3, debugInfo.SearchableFields!.Count);
    }

    [Fact]
    public void DebugInfo_OfficialProperty_QueryRewrites_CanBeSet()
    {
        var debugInfo = new DebugInfo
        {
            QueryRewrites = new QueryRewritesDebugInfo
            {
                Text = new QueryRewritesValuesDebugInfo
                {
                    InputQuery = "luxury hotel spa",
                    Rewrites = new List<string>
                    {
                        "premium hotel with spa",
                        "upscale hotel spa facility"
                    }
                },
                Vectors = new List<QueryRewritesValuesDebugInfo>
                {
                    new()
                    {
                        InputQuery = "luxury hotel vector",
                        Rewrites = new List<string> { "premium accommodation" }
                    }
                }
            }
        };

        Assert.NotNull(debugInfo.QueryRewrites);
        Assert.NotNull(debugInfo.QueryRewrites.Text);
        Assert.Equal("luxury hotel spa", debugInfo.QueryRewrites.Text.InputQuery);
        Assert.Equal(2, debugInfo.QueryRewrites.Text.Rewrites!.Count);
        Assert.Single(debugInfo.QueryRewrites.Vectors!);
    }

    [Fact]
    public void DebugInfo_SerializesWithCorrectPropertyNames()
    {
        var debugInfo = new DebugInfo
        {
            ParsedQuery = "test query",
            IsHybridSearch = false,
            TotalTimeMs = 15.0,
            TextMatchCount = 5,
            SearchableFields = new List<string> { "title" }
        };

        var json = JsonSerializer.Serialize(debugInfo, JsonOptions);

        // Official properties
        Assert.DoesNotContain("\"queryRewrites\"", json); // null, should be omitted

        // Simulator-specific properties use "simulator." prefix
        Assert.Contains("\"simulator.parsedQuery\"", json);
        Assert.Contains("\"simulator.isHybridSearch\"", json);
        Assert.Contains("\"simulator.totalTimeMs\"", json);
        Assert.Contains("\"simulator.textMatchCount\"", json);
        Assert.Contains("\"simulator.searchableFields\"", json);
    }

    [Fact]
    public void DebugInfo_QueryRewrites_SerializesToJson()
    {
        var debugInfo = new DebugInfo
        {
            QueryRewrites = new QueryRewritesDebugInfo
            {
                Text = new QueryRewritesValuesDebugInfo
                {
                    InputQuery = "test",
                    Rewrites = new List<string> { "rewrite1" }
                }
            }
        };

        var json = JsonSerializer.Serialize(debugInfo, JsonOptions);
        Assert.Contains("\"queryRewrites\"", json);
        Assert.Contains("\"inputQuery\"", json);
        Assert.Contains("\"rewrites\"", json);
    }

    #endregion

    #region DocumentDebugInfo Model Tests

    [Fact]
    public void DocumentDebugInfo_DefaultProperties_AreNull()
    {
        var docDebug = new DocumentDebugInfo();

        Assert.Null(docDebug.Semantic);
        Assert.Null(docDebug.Vectors);
        Assert.Null(docDebug.InnerHits);
    }

    [Fact]
    public void DocumentDebugInfo_WithVectors_HasCorrectStructure()
    {
        var docDebug = new DocumentDebugInfo
        {
            Vectors = new VectorsDebugInfo
            {
                Subscores = new QueryResultDocumentSubscores
                {
                    Text = new TextResult { SearchScore = 3.14 },
                    DocumentBoost = 1.0,
                    Vectors = new Dictionary<string, SingleVectorFieldResult>
                    {
                        ["embedding"] = new SingleVectorFieldResult
                        {
                            SearchScore = 0.85,
                            VectorSimilarity = 0.92
                        }
                    }
                }
            }
        };

        Assert.NotNull(docDebug.Vectors);
        Assert.NotNull(docDebug.Vectors.Subscores);
        Assert.NotNull(docDebug.Vectors.Subscores.Text);
        Assert.Equal(3.14, docDebug.Vectors.Subscores.Text.SearchScore);
        Assert.Equal(1.0, docDebug.Vectors.Subscores.DocumentBoost);
        Assert.Single(docDebug.Vectors.Subscores.Vectors!);
        Assert.Equal(0.85, docDebug.Vectors.Subscores.Vectors["embedding"].SearchScore);
        Assert.Equal(0.92, docDebug.Vectors.Subscores.Vectors["embedding"].VectorSimilarity);
    }

    [Fact]
    public void DocumentDebugInfo_WithSemantic_HasCorrectStructure()
    {
        var docDebug = new DocumentDebugInfo
        {
            Semantic = new SemanticDebugInfo
            {
                TitleField = new QueryResultDocumentSemanticField
                {
                    Name = "title",
                    State = "used"
                },
                ContentFields = new List<QueryResultDocumentSemanticField>
                {
                    new() { Name = "description", State = "used" },
                    new() { Name = "content", State = "partiallyUsed" }
                },
                KeywordFields = new List<QueryResultDocumentSemanticField>
                {
                    new() { Name = "tags", State = "unused" }
                },
                RerankerInput = new QueryResultDocumentRerankerInput
                {
                    Title = "My Document Title",
                    Content = "Full content text here...",
                    Keywords = "azure search ai"
                }
            }
        };

        Assert.NotNull(docDebug.Semantic);
        Assert.Equal("title", docDebug.Semantic.TitleField!.Name);
        Assert.Equal("used", docDebug.Semantic.TitleField.State);
        Assert.Equal(2, docDebug.Semantic.ContentFields!.Count);
        Assert.Single(docDebug.Semantic.KeywordFields!);
        Assert.Equal("My Document Title", docDebug.Semantic.RerankerInput!.Title);
    }

    [Fact]
    public void DocumentDebugInfo_WithMultipleVectorFields()
    {
        var docDebug = new DocumentDebugInfo
        {
            Vectors = new VectorsDebugInfo
            {
                Subscores = new QueryResultDocumentSubscores
                {
                    Vectors = new Dictionary<string, SingleVectorFieldResult>
                    {
                        ["titleVector"] = new SingleVectorFieldResult
                        {
                            SearchScore = 0.75,
                            VectorSimilarity = 0.88
                        },
                        ["contentVector"] = new SingleVectorFieldResult
                        {
                            SearchScore = 0.82,
                            VectorSimilarity = 0.91
                        }
                    }
                }
            }
        };

        Assert.Equal(2, docDebug.Vectors!.Subscores!.Vectors!.Count);
        Assert.True(docDebug.Vectors.Subscores.Vectors.ContainsKey("titleVector"));
        Assert.True(docDebug.Vectors.Subscores.Vectors.ContainsKey("contentVector"));
    }

    #endregion

    #region SearchResult.DocumentDebugInfo Tests

    [Fact]
    public void SearchResult_DocumentDebugInfo_DefaultsToNull()
    {
        var result = new SearchResult { Score = 1.5 };
        Assert.Null(result.DocumentDebugInfo);
    }

    [Fact]
    public void SearchResult_DocumentDebugInfo_StoredInDictionary()
    {
        var debugInfo = new DocumentDebugInfo
        {
            Vectors = new VectorsDebugInfo
            {
                Subscores = new QueryResultDocumentSubscores
                {
                    Text = new TextResult { SearchScore = 2.5 }
                }
            }
        };

        var result = new SearchResult { Score = 2.5 };
        result.DocumentDebugInfo = debugInfo;

        // Verify it's stored under the correct key
        Assert.True(result.ContainsKey("@search.documentDebugInfo"));
        Assert.Same(debugInfo, result["@search.documentDebugInfo"]);
        Assert.Same(debugInfo, result.DocumentDebugInfo);
    }

    [Fact]
    public void SearchResult_DocumentDebugInfo_NullDoesNotAddKey()
    {
        var result = new SearchResult { Score = 1.0 };
        result.DocumentDebugInfo = null;

        Assert.False(result.ContainsKey("@search.documentDebugInfo"));
    }

    #endregion

    #region SearchResponse.SearchDebug Tests

    [Fact]
    public void SearchResponse_SearchDebug_DefaultsToNull()
    {
        var response = new SearchResponse();
        Assert.Null(response.SearchDebug);
    }

    [Fact]
    public void SearchResponse_SearchDebug_CanBeSet()
    {
        var debugInfo = new DebugInfo
        {
            TotalTimeMs = 50.0,
            TextMatchCount = 10,
            IsHybridSearch = false
        };

        var response = new SearchResponse { SearchDebug = debugInfo };

        Assert.NotNull(response.SearchDebug);
        Assert.Equal(50.0, response.SearchDebug.TotalTimeMs);
    }

    [Fact]
    public void SearchResponse_SearchDebug_SerializesToCorrectJsonKey()
    {
        var response = new SearchResponse
        {
            Value = new List<SearchResult>(),
            SearchDebug = new DebugInfo
            {
                TotalTimeMs = 30.0
            }
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        Assert.Contains("\"@search.debug\"", json);
    }

    [Fact]
    public void SearchResponse_SearchDebug_NullOmittedFromJson()
    {
        var response = new SearchResponse
        {
            Value = new List<SearchResult>()
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        Assert.DoesNotContain("\"@search.debug\"", json);
    }

    #endregion

    #region JSON Serialization Round-Trip Tests

    [Fact]
    public void DebugInfo_JsonRoundTrip_PreservesAllProperties()
    {
        var original = new DebugInfo
        {
            QueryRewrites = new QueryRewritesDebugInfo
            {
                Text = new QueryRewritesValuesDebugInfo
                {
                    InputQuery = "test query",
                    Rewrites = new List<string> { "rewrite1", "rewrite2" }
                }
            },
            ParsedQuery = "+title:test",
            ParsedFilter = "status:active",
            IsHybridSearch = true,
            TextSearchTimeMs = 5.5,
            VectorSearchTimeMs = 3.2,
            TotalTimeMs = 12.0,
            TextMatchCount = 15,
            VectorMatchCount = 8,
            ScoreFusionMethod = "WeightedAverage",
            SearchableFields = new List<string> { "title", "description" }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DebugInfo>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ParsedQuery, deserialized!.ParsedQuery);
        Assert.Equal(original.ParsedFilter, deserialized.ParsedFilter);
        Assert.Equal(original.IsHybridSearch, deserialized.IsHybridSearch);
        Assert.Equal(original.TextSearchTimeMs, deserialized.TextSearchTimeMs);
        Assert.Equal(original.VectorSearchTimeMs, deserialized.VectorSearchTimeMs);
        Assert.Equal(original.TotalTimeMs, deserialized.TotalTimeMs);
        Assert.Equal(original.TextMatchCount, deserialized.TextMatchCount);
        Assert.Equal(original.VectorMatchCount, deserialized.VectorMatchCount);
        Assert.Equal(original.ScoreFusionMethod, deserialized.ScoreFusionMethod);
        Assert.Equal(original.SearchableFields, deserialized.SearchableFields);
        Assert.NotNull(deserialized.QueryRewrites);
        Assert.Equal("test query", deserialized.QueryRewrites!.Text!.InputQuery);
        Assert.Equal(2, deserialized.QueryRewrites.Text.Rewrites!.Count);
    }

    [Fact]
    public void DocumentDebugInfo_JsonRoundTrip_PreservesVectorSubscores()
    {
        var original = new DocumentDebugInfo
        {
            Vectors = new VectorsDebugInfo
            {
                Subscores = new QueryResultDocumentSubscores
                {
                    Text = new TextResult { SearchScore = 3.14 },
                    DocumentBoost = 1.5,
                    Vectors = new Dictionary<string, SingleVectorFieldResult>
                    {
                        ["embedding"] = new SingleVectorFieldResult
                        {
                            SearchScore = 0.85,
                            VectorSimilarity = 0.92
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DocumentDebugInfo>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.Vectors?.Subscores);
        Assert.Equal(3.14, deserialized.Vectors!.Subscores!.Text!.SearchScore);
        Assert.Equal(1.5, deserialized.Vectors.Subscores.DocumentBoost);
        Assert.Single(deserialized.Vectors.Subscores.Vectors!);
        Assert.Equal(0.85, deserialized.Vectors.Subscores.Vectors["embedding"].SearchScore);
        Assert.Equal(0.92, deserialized.Vectors.Subscores.Vectors["embedding"].VectorSimilarity);
    }

    [Fact]
    public void DocumentDebugInfo_JsonRoundTrip_PreservesSemanticInfo()
    {
        var original = new DocumentDebugInfo
        {
            Semantic = new SemanticDebugInfo
            {
                TitleField = new QueryResultDocumentSemanticField { Name = "title", State = "used" },
                ContentFields = new List<QueryResultDocumentSemanticField>
                {
                    new() { Name = "body", State = "partiallyUsed" }
                },
                RerankerInput = new QueryResultDocumentRerankerInput
                {
                    Title = "Title Text",
                    Content = "Content Text",
                    Keywords = "keyword1 keyword2"
                }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DocumentDebugInfo>(json, JsonOptions);

        Assert.NotNull(deserialized?.Semantic);
        Assert.Equal("title", deserialized!.Semantic!.TitleField!.Name);
        Assert.Equal("used", deserialized.Semantic.TitleField.State);
        Assert.Single(deserialized.Semantic.ContentFields!);
        Assert.Equal("Title Text", deserialized.Semantic.RerankerInput!.Title);
        Assert.Equal("Content Text", deserialized.Semantic.RerankerInput.Content);
    }

    [Fact]
    public void DebugInfo_Json_ContainsCorrectPropertyPrefixes()
    {
        var debugInfo = new DebugInfo
        {
            ParsedQuery = "query",
            ParsedFilter = "filter",
            IsHybridSearch = true,
            TextSearchTimeMs = 1.0,
            VectorSearchTimeMs = 2.0,
            TotalTimeMs = 3.0,
            TextMatchCount = 10,
            VectorMatchCount = 5,
            ScoreFusionMethod = "RRF",
            SearchableFields = new List<string> { "f1" }
        };

        var json = JsonSerializer.Serialize(debugInfo, JsonOptions);

        // All simulator-specific properties must be prefixed with "simulator."
        Assert.Contains("\"simulator.parsedQuery\"", json);
        Assert.Contains("\"simulator.parsedFilter\"", json);
        Assert.Contains("\"simulator.isHybridSearch\"", json);
        Assert.Contains("\"simulator.textSearchTimeMs\"", json);
        Assert.Contains("\"simulator.vectorSearchTimeMs\"", json);
        Assert.Contains("\"simulator.totalTimeMs\"", json);
        Assert.Contains("\"simulator.textMatchCount\"", json);
        Assert.Contains("\"simulator.vectorMatchCount\"", json);
        Assert.Contains("\"simulator.scoreFusionMethod\"", json);
        Assert.Contains("\"simulator.searchableFields\"", json);

        // Should NOT contain un-prefixed simulator properties
        // (The json key should have "simulator." prefix, not bare names)
        Assert.DoesNotContain("\"parsedQuery\"", json);
        Assert.DoesNotContain("\"parsedFilter\"", json);
        Assert.DoesNotContain("\"scoreFusionMethod\"", json);
    }

    [Fact]
    public void VectorsDebugInfo_Json_HasSubscoresKey()
    {
        var vectors = new VectorsDebugInfo
        {
            Subscores = new QueryResultDocumentSubscores
            {
                Text = new TextResult { SearchScore = 1.0 }
            }
        };

        var json = JsonSerializer.Serialize(vectors, JsonOptions);

        Assert.Contains("\"subscores\"", json);
        Assert.Contains("\"text\"", json);
        Assert.Contains("\"searchScore\"", json);
    }

    [Fact]
    public void QueryResultDocumentSubscores_Json_HasCorrectKeys()
    {
        var subscores = new QueryResultDocumentSubscores
        {
            Text = new TextResult { SearchScore = 2.5 },
            DocumentBoost = 1.0,
            Vectors = new Dictionary<string, SingleVectorFieldResult>
            {
                ["myVector"] = new SingleVectorFieldResult
                {
                    SearchScore = 0.9,
                    VectorSimilarity = 0.95
                }
            }
        };

        var json = JsonSerializer.Serialize(subscores, JsonOptions);

        Assert.Contains("\"text\"", json);
        Assert.Contains("\"documentBoost\"", json);
        Assert.Contains("\"vectors\"", json);
        Assert.Contains("\"myVector\"", json);
        Assert.Contains("\"searchScore\"", json);
        Assert.Contains("\"vectorSimilarity\"", json);
    }

    #endregion

    #region Full SearchResponse with Debug Tests

    [Fact]
    public void SearchResponse_WithDebug_ContainsFullStructure()
    {
        var response = new SearchResponse
        {
            ODataContext = "https://localhost/indexes('test')/$metadata#docs(*)",
            SearchCoverage = 100.0,
            SearchDebug = new DebugInfo
            {
                ParsedQuery = "+title:hotel",
                IsHybridSearch = true,
                TotalTimeMs = 25.0,
                TextMatchCount = 3,
                VectorMatchCount = 5,
                ScoreFusionMethod = "WeightedAverage",
                SearchableFields = new List<string> { "title", "description" }
            },
            Value = new List<SearchResult>
            {
                CreateSearchResultWithDebug("doc1", 2.5, 1.8, 0.9, "embedding"),
                CreateSearchResultWithDebug("doc2", 1.2, 0.0, 0.85, "embedding")
            }
        };

        Assert.NotNull(response.SearchDebug);
        Assert.Equal(2, response.Value.Count);
        Assert.NotNull(response.Value[0].DocumentDebugInfo);
        Assert.NotNull(response.Value[1].DocumentDebugInfo);

        // First result has both text and vector subscores
        var firstDocDebug = response.Value[0].DocumentDebugInfo!;
        Assert.NotNull(firstDocDebug.Vectors?.Subscores?.Text);
        Assert.Equal(1.8, firstDocDebug.Vectors!.Subscores!.Text!.SearchScore);
        Assert.Equal(0.9, firstDocDebug.Vectors.Subscores.Vectors!["embedding"].VectorSimilarity);

        // Second result has only vector (text score = 0)
        var secondDocDebug = response.Value[1].DocumentDebugInfo!;
        Assert.Null(secondDocDebug.Vectors?.Subscores?.Text);
        Assert.Equal(0.85, secondDocDebug.Vectors!.Subscores!.Vectors!["embedding"].VectorSimilarity);
    }

    [Fact]
    public void SearchResponse_WithDebug_SerializesToValidJson()
    {
        var response = new SearchResponse
        {
            ODataContext = "https://localhost/indexes('test')/$metadata#docs(*)",
            SearchDebug = new DebugInfo
            {
                ParsedQuery = "test",
                TotalTimeMs = 10.0,
                SearchableFields = new List<string> { "title" }
            },
            Value = new List<SearchResult>
            {
                CreateSearchResultWithDebug("doc1", 1.0, 0.8, 0.95, "vec")
            }
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        // Verify response-level debug
        Assert.Contains("\"@search.debug\"", json);
        Assert.Contains("\"simulator.parsedQuery\"", json);
        Assert.Contains("\"simulator.totalTimeMs\"", json);

        // Verify per-document debug
        Assert.Contains("\"@search.documentDebugInfo\"", json);
        Assert.Contains("\"subscores\"", json);
        Assert.Contains("\"vectorSimilarity\"", json);

        // Verify valid JSON - should not throw
        var parsed = JsonDocument.Parse(json);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void SearchResponse_WithoutDebug_OmitsDebugProperties()
    {
        var response = new SearchResponse
        {
            Value = new List<SearchResult>
            {
                new()
                {
                    Score = 1.5,
                    ["id"] = "doc1",
                    ["title"] = "Test"
                }
            }
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.DoesNotContain("\"@search.debug\"", json);
        Assert.DoesNotContain("\"@search.documentDebugInfo\"", json);
        Assert.DoesNotContain("\"simulator.", json);
    }

    #endregion

    #region TextResult / SingleVectorFieldResult Tests

    [Fact]
    public void TextResult_SearchScore_CanBeSet()
    {
        var textResult = new TextResult { SearchScore = 4.25 };
        Assert.Equal(4.25, textResult.SearchScore);
    }

    [Fact]
    public void TextResult_SearchScore_DefaultIsZero()
    {
        var textResult = new TextResult();
        Assert.Equal(0, textResult.SearchScore);
    }

    [Fact]
    public void SingleVectorFieldResult_Properties_CanBeSet()
    {
        var result = new SingleVectorFieldResult
        {
            SearchScore = 0.85,
            VectorSimilarity = 0.92
        };

        Assert.Equal(0.85, result.SearchScore);
        Assert.Equal(0.92, result.VectorSimilarity);
    }

    [Fact]
    public void SingleVectorFieldResult_DefaultsAreZero()
    {
        var result = new SingleVectorFieldResult();
        Assert.Equal(0, result.SearchScore);
        Assert.Equal(0, result.VectorSimilarity);
    }

    #endregion

    #region QueryRewritesDebugInfo Tests

    [Fact]
    public void QueryRewritesDebugInfo_DefaultProperties_AreNull()
    {
        var qr = new QueryRewritesDebugInfo();
        Assert.Null(qr.Text);
        Assert.Null(qr.Vectors);
    }

    [Fact]
    public void QueryRewritesValuesDebugInfo_CanHoldRewriteValues()
    {
        var values = new QueryRewritesValuesDebugInfo
        {
            InputQuery = "original query",
            Rewrites = new List<string> { "rewrite 1", "rewrite 2", "rewrite 3" }
        };

        Assert.Equal("original query", values.InputQuery);
        Assert.Equal(3, values.Rewrites.Count);
    }

    [Fact]
    public void QueryRewritesDebugInfo_JsonRoundTrip()
    {
        var original = new QueryRewritesDebugInfo
        {
            Text = new QueryRewritesValuesDebugInfo
            {
                InputQuery = "hotels in paris",
                Rewrites = new List<string> { "parisian hotels", "paris accommodation" }
            },
            Vectors = new List<QueryRewritesValuesDebugInfo>
            {
                new()
                {
                    InputQuery = "hotels in paris vector",
                    Rewrites = new List<string> { "paris lodging" }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<QueryRewritesDebugInfo>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("hotels in paris", deserialized!.Text!.InputQuery);
        Assert.Equal(2, deserialized.Text.Rewrites!.Count);
        Assert.Single(deserialized.Vectors!);
        Assert.Equal("hotels in paris vector", deserialized.Vectors[0].InputQuery);
    }

    #endregion

    #region SemanticDebugInfo Tests

    [Fact]
    public void SemanticDebugInfo_DefaultProperties_AreNull()
    {
        var semantic = new SemanticDebugInfo();
        Assert.Null(semantic.ContentFields);
        Assert.Null(semantic.KeywordFields);
        Assert.Null(semantic.TitleField);
        Assert.Null(semantic.RerankerInput);
    }

    [Fact]
    public void SemanticDebugInfo_JsonRoundTrip()
    {
        var original = new SemanticDebugInfo
        {
            TitleField = new QueryResultDocumentSemanticField { Name = "title", State = "used" },
            ContentFields = new List<QueryResultDocumentSemanticField>
            {
                new() { Name = "body", State = "used" },
                new() { Name = "summary", State = "unused" }
            },
            KeywordFields = new List<QueryResultDocumentSemanticField>
            {
                new() { Name = "tags", State = "partiallyUsed" }
            },
            RerankerInput = new QueryResultDocumentRerankerInput
            {
                Title = "Document Title",
                Content = "Document body content...",
                Keywords = "tag1 tag2"
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SemanticDebugInfo>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("title", deserialized!.TitleField!.Name);
        Assert.Equal(2, deserialized.ContentFields!.Count);
        Assert.Equal("body", deserialized.ContentFields[0].Name);
        Assert.Single(deserialized.KeywordFields!);
        Assert.Equal("Document Title", deserialized.RerankerInput!.Title);
    }

    #endregion

    #region ParseDebugModes Tests (via reflection since it's private)

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("disabled", 0)]
    [InlineData("DISABLED", 0)]
    public void ParseDebugModes_DisabledOrEmpty_ReturnsEmptySet(string? input, int expectedCount)
    {
        var result = InvokeParseDebugModes(input);
        Assert.Equal(expectedCount, result.Count);
    }

    [Theory]
    [InlineData("vector", new[] { "vector" })]
    [InlineData("semantic", new[] { "semantic" })]
    [InlineData("queryRewrites", new[] { "queryrewrites" })]
    [InlineData("innerHits", new[] { "innerhits" })]
    [InlineData("all", new[] { "all" })]
    public void ParseDebugModes_SingleMode_ParsesCorrectly(string input, string[] expectedModes)
    {
        var result = InvokeParseDebugModes(input);
        Assert.Equal(expectedModes.Length, result.Count);
        foreach (var mode in expectedModes)
        {
            Assert.Contains(mode, result);
        }
    }

    [Fact]
    public void ParseDebugModes_CombinedModes_ParsesAll()
    {
        var result = InvokeParseDebugModes("semantic|vector");
        Assert.Equal(2, result.Count);
        Assert.Contains("semantic", result);
        Assert.Contains("vector", result);
    }

    [Fact]
    public void ParseDebugModes_MultipleCombinedModes_ParsesAll()
    {
        var result = InvokeParseDebugModes("vector|queryRewrites|innerHits");
        Assert.Equal(3, result.Count);
        Assert.Contains("vector", result);
        Assert.Contains("queryrewrites", result);
        Assert.Contains("innerhits", result);
    }

    [Fact]
    public void ParseDebugModes_CaseInsensitive()
    {
        var result = InvokeParseDebugModes("VECTOR|Semantic");
        Assert.Equal(2, result.Count);
        Assert.Contains("vector", result);
        Assert.Contains("semantic", result);
    }

    [Fact]
    public void ParseDebugModes_WithWhitespace_Trims()
    {
        var result = InvokeParseDebugModes(" vector | semantic ");
        Assert.Equal(2, result.Count);
        Assert.Contains("vector", result);
        Assert.Contains("semantic", result);
    }

    [Fact]
    public void ParseDebugModes_DisabledMixedIn_IgnoresDisabled()
    {
        var result = InvokeParseDebugModes("disabled|vector");
        Assert.Single(result);
        Assert.Contains("vector", result);
    }

    [Fact]
    public void ParseDebugModes_EmptyPipes_Ignored()
    {
        var result = InvokeParseDebugModes("vector||semantic");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseDebugModes_AllMode_ReturnsSingleEntry()
    {
        var result = InvokeParseDebugModes("all");
        Assert.Single(result);
        Assert.Contains("all", result);
    }

    /// <summary>
    /// Invoke the private static ParseDebugModes method via reflection.
    /// </summary>
    private static HashSet<string> InvokeParseDebugModes(string? debug)
    {
        var method = typeof(AzureAISearchSimulator.Search.SearchService)
            .GetMethod("ParseDebugModes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object?[] { debug });
        return (HashSet<string>)result!;
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void QueryResultDocumentSubscores_TextOnly_NoVectors()
    {
        var subscores = new QueryResultDocumentSubscores
        {
            Text = new TextResult { SearchScore = 5.0 }
        };

        Assert.NotNull(subscores.Text);
        Assert.Null(subscores.Vectors);
        Assert.Null(subscores.DocumentBoost);
    }

    [Fact]
    public void QueryResultDocumentSubscores_VectorsOnly_NoText()
    {
        var subscores = new QueryResultDocumentSubscores
        {
            Vectors = new Dictionary<string, SingleVectorFieldResult>
            {
                ["vec1"] = new SingleVectorFieldResult { SearchScore = 0.8, VectorSimilarity = 0.9 }
            }
        };

        Assert.Null(subscores.Text);
        Assert.NotNull(subscores.Vectors);
        Assert.Single(subscores.Vectors);
    }

    [Fact]
    public void DocumentDebugInfo_EmptyInnerHits()
    {
        var docDebug = new DocumentDebugInfo
        {
            InnerHits = new Dictionary<string, object>()
        };

        Assert.NotNull(docDebug.InnerHits);
        Assert.Empty(docDebug.InnerHits);
    }

    [Fact]
    public void DebugInfo_TextOnlySearch_HasOnlyTextMetrics()
    {
        var debugInfo = new DebugInfo
        {
            IsHybridSearch = false,
            ScoreFusionMethod = "TextOnly",
            TextSearchTimeMs = 5.0,
            TextMatchCount = 12,
            SearchableFields = new List<string> { "title", "content" }
        };

        Assert.False(debugInfo.IsHybridSearch);
        Assert.Equal("TextOnly", debugInfo.ScoreFusionMethod);
        Assert.Null(debugInfo.VectorSearchTimeMs);
        Assert.Null(debugInfo.VectorMatchCount);
    }

    [Fact]
    public void DebugInfo_VectorOnlySearch_HasOnlyVectorMetrics()
    {
        var debugInfo = new DebugInfo
        {
            IsHybridSearch = false,
            ScoreFusionMethod = "VectorOnly",
            VectorSearchTimeMs = 8.0,
            VectorMatchCount = 20
        };

        Assert.False(debugInfo.IsHybridSearch);
        Assert.Null(debugInfo.TextSearchTimeMs);
        Assert.Null(debugInfo.TextMatchCount);
    }

    #endregion

    #region Helper Methods

    private static SearchResult CreateSearchResultWithDebug(
        string id, double score, double textScore, double vectorSimilarity, string vectorField)
    {
        var result = new SearchResult
        {
            Score = score,
            ["id"] = id
        };

        var subscores = new QueryResultDocumentSubscores
        {
            DocumentBoost = 1.0
        };

        if (textScore > 0)
        {
            subscores.Text = new TextResult { SearchScore = textScore };
        }

        if (vectorSimilarity > 0)
        {
            subscores.Vectors = new Dictionary<string, SingleVectorFieldResult>
            {
                [vectorField] = new SingleVectorFieldResult
                {
                    SearchScore = score,
                    VectorSimilarity = vectorSimilarity
                }
            };
        }

        result.DocumentDebugInfo = new DocumentDebugInfo
        {
            Vectors = new VectorsDebugInfo
            {
                Subscores = subscores
            }
        };

        return result;
    }

    #endregion
}
