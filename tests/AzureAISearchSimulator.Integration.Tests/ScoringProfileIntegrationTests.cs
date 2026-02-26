using Xunit;
using AzureAISearchSimulator.Core.Models;

namespace AzureAISearchSimulator.Integration.Tests;

/// <summary>
/// Integration tests for scoring profiles, exercising the full pipeline:
/// index registration → document upload → search with scoring profiles → result ordering.
/// </summary>
[Collection("Scoring Profile Integration Tests")]
public class ScoringProfileIntegrationTests
{
    private readonly ScoringProfileTestFixture _fixture;

    public ScoringProfileIntegrationTests(ScoringProfileTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // ─── Text Weights ────────────────────────────────────────────────

    [Fact]
    public async Task Search_WithTextWeights_BoostsRelevantField()
    {
        var indexName = UniqueId("tw");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true }
            },
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-title",
                    Text = new TextWeights
                    {
                        Weights = new Dictionary<string, double> { { "title", 10.0 }, { "description", 1.0 } }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        // Doc A: has "azure" in description only
        // Doc B: has "azure" in title
        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "desc-match",
                ["title"] = "General overview of cloud computing",
                ["description"] = "Azure provides many cloud services"
            },
            new Dictionary<string, object?>
            {
                ["id"] = "title-match",
                ["title"] = "Azure cloud platform",
                ["description"] = "A general overview of cloud services"
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "azure",
            ScoringProfile = "boost-title"
        });

        Assert.True(response.Value.Count >= 2);
        // Title-match document should rank first because of 10x title weight
        Assert.Equal("title-match", response.Value[0]["id"]?.ToString());
    }

    // ─── Magnitude Function ──────────────────────────────────────────

    [Fact]
    public async Task Search_WithMagnitude_HighRatingFirst()
    {
        var indexName = UniqueId("mag");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            },
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-rating",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "magnitude",
                            FieldName = "rating",
                            Boost = 10.0,
                            Interpolation = "linear",
                            Magnitude = new MagnitudeFunction
                            {
                                BoostingRangeStart = 0,
                                BoostingRangeEnd = 5
                            }
                        }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "low-rating",
                ["title"] = "Hotel with pool and spa",
                ["rating"] = 1.0
            },
            new Dictionary<string, object?>
            {
                ["id"] = "high-rating",
                ["title"] = "Hotel with pool and spa amenities",
                ["rating"] = 4.8
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "hotel pool",
            ScoringProfile = "boost-rating"
        });

        Assert.True(response.Value.Count >= 2);
        // High-rating document should rank first due to magnitude boost
        Assert.Equal("high-rating", response.Value[0]["id"]?.ToString());
    }

    // ─── Freshness Function ──────────────────────────────────────────

    [Fact]
    public async Task Search_WithFreshness_RecentDocFirst()
    {
        var indexName = UniqueId("fresh");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "lastUpdated", Type = "Edm.DateTimeOffset", Filterable = true }
            },
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-recent",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "freshness",
                            FieldName = "lastUpdated",
                            Boost = 10.0,
                            Interpolation = "linear",
                            Freshness = new FreshnessFunction
                            {
                                BoostingDuration = "P365D"
                            }
                        }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "old-doc",
                ["title"] = "Guide to machine learning algorithms",
                ["lastUpdated"] = DateTimeOffset.UtcNow.AddYears(-3).ToString("O")
            },
            new Dictionary<string, object?>
            {
                ["id"] = "recent-doc",
                ["title"] = "Guide to machine learning techniques",
                ["lastUpdated"] = DateTimeOffset.UtcNow.AddDays(-5).ToString("O")
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "machine learning",
            ScoringProfile = "boost-recent"
        });

        Assert.True(response.Value.Count >= 2);
        // Recent doc should rank first due to freshness boost
        Assert.Equal("recent-doc", response.Value[0]["id"]?.ToString());
    }

    // ─── Tag Function ────────────────────────────────────────────────

    [Fact]
    public async Task Search_WithTagBoost_MatchingTagFirst()
    {
        var indexName = UniqueId("tag");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "tags", Type = "Collection(Edm.String)", Filterable = true, Searchable = true }
            },
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-tags",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "tag",
                            FieldName = "tags",
                            Boost = 10.0,
                            Tag = new TagFunction { TagsParameter = "myTags" }
                        }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "no-match-tag",
                ["title"] = "Luxury resort and spa",
                ["tags"] = new List<string> { "resort", "beach" }
            },
            new Dictionary<string, object?>
            {
                ["id"] = "match-tag",
                ["title"] = "Luxury hotel and spa",
                ["tags"] = new List<string> { "luxury", "spa" }
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "luxury",
            ScoringProfile = "boost-tags",
            ScoringParameters = new List<string> { "myTags-luxury,spa" }
        });

        Assert.True(response.Value.Count >= 2);
        // Document with matching tags should rank first
        Assert.Equal("match-tag", response.Value[0]["id"]?.ToString());
    }

    // ─── Default Profile ─────────────────────────────────────────────

    [Fact]
    public async Task Search_DefaultProfile_AppliedAutomatically()
    {
        var indexName = UniqueId("def");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            },
            DefaultScoringProfile = "default-boost",
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "default-boost",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "magnitude",
                            FieldName = "rating",
                            Boost = 10.0,
                            Magnitude = new MagnitudeFunction
                            {
                                BoostingRangeStart = 0,
                                BoostingRangeEnd = 5
                            }
                        }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "low",
                ["title"] = "Test document about search",
                ["rating"] = 0.5
            },
            new Dictionary<string, object?>
            {
                ["id"] = "high",
                ["title"] = "Test document about search engines",
                ["rating"] = 4.9
            });

        // Search WITHOUT specifying a scoring profile — default should kick in
        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "search"
        });

        Assert.True(response.Value.Count >= 2);
        // High-rating document should rank first because default profile applies
        Assert.Equal("high", response.Value[0]["id"]?.ToString());
    }

    // ─── Explicit Profile Overrides Default ──────────────────────────

    [Fact]
    public async Task Search_ExplicitProfile_OverridesDefault()
    {
        var indexName = UniqueId("over");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "description", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            },
            DefaultScoringProfile = "boost-rating",
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-rating",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "magnitude",
                            FieldName = "rating",
                            Boost = 10.0,
                            Magnitude = new MagnitudeFunction
                            {
                                BoostingRangeStart = 0,
                                BoostingRangeEnd = 5
                            }
                        }
                    }
                },
                new()
                {
                    Name = "boost-title",
                    Text = new TextWeights
                    {
                        Weights = new Dictionary<string, double> { { "title", 10.0 }, { "description", 1.0 } }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "high-rating-weak-title",
                ["title"] = "General overview",
                ["description"] = "This covers cloud computing and Azure services",
                ["rating"] = 4.9
            },
            new Dictionary<string, object?>
            {
                ["id"] = "low-rating-strong-title",
                ["title"] = "Cloud computing in Azure",
                ["description"] = "General overview of services",
                ["rating"] = 1.0
            });

        // Use explicit boost-title profile, which overrides the default boost-rating
        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "cloud",
            ScoringProfile = "boost-title"
        });

        Assert.True(response.Value.Count >= 2);
        // Title-match should win over high rating because we use boost-title, not boost-rating
        Assert.Equal("low-rating-strong-title", response.Value[0]["id"]?.ToString());
    }

    // ─── Invalid Profile Returns Error ───────────────────────────────

    [Fact]
    public async Task Search_InvalidProfile_ThrowsArgumentException()
    {
        var indexName = UniqueId("inv");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "doc1",
                ["title"] = "Test document"
            });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fixture.SearchService.SearchAsync(indexName, new SearchRequest
            {
                Search = "test",
                ScoringProfile = "nonexistent-profile"
            }));
    }

    // ─── Debug Output Shows DocumentBoost ────────────────────────────

    [Fact]
    public async Task Search_WithDebug_ShowsDocumentBoost()
    {
        var indexName = UniqueId("dbg");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true },
                new() { Name = "rating", Type = "Edm.Double", Filterable = true }
            },
            ScoringProfiles = new List<ScoringProfile>
            {
                new()
                {
                    Name = "boost-rating",
                    Functions = new List<ScoringFunction>
                    {
                        new()
                        {
                            Type = "magnitude",
                            FieldName = "rating",
                            Boost = 5.0,
                            Magnitude = new MagnitudeFunction
                            {
                                BoostingRangeStart = 0,
                                BoostingRangeEnd = 5
                            }
                        }
                    }
                }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "rated-doc",
                ["title"] = "A test document for debugging",
                ["rating"] = 4.0
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "test",
            ScoringProfile = "boost-rating",
            Debug = "all"
        });

        Assert.NotEmpty(response.Value);
        var result = response.Value[0];
        
        // Debug info should include document boost > 1.0 since we have a scoring function
        Assert.NotNull(result.DocumentDebugInfo);
        Assert.NotNull(result.DocumentDebugInfo!.Vectors?.Subscores);
        Assert.True(result.DocumentDebugInfo.Vectors!.Subscores!.DocumentBoost > 1.0,
            $"Expected DocumentBoost > 1.0, but got {result.DocumentDebugInfo.Vectors.Subscores.DocumentBoost}");
    }

    // ─── No Profile: DocumentBoost is 1.0 ────────────────────────────

    [Fact]
    public async Task Search_WithoutProfile_DocumentBoostIsOne()
    {
        var indexName = UniqueId("nop");
        var index = new SearchIndex
        {
            Name = indexName,
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true },
                new() { Name = "title", Type = "Edm.String", Searchable = true }
            }
        };
        _fixture.RegisterIndex(index);

        await _fixture.UploadDocuments(indexName,
            new Dictionary<string, object?>
            {
                ["id"] = "plain-doc",
                ["title"] = "A simple test document"
            });

        var response = await _fixture.SearchService.SearchAsync(indexName, new SearchRequest
        {
            Search = "simple test",
            Debug = "all"
        });

        Assert.NotEmpty(response.Value);
        var result = response.Value[0];
        Assert.NotNull(result.DocumentDebugInfo);
        Assert.NotNull(result.DocumentDebugInfo!.Vectors?.Subscores);
        Assert.Equal(1.0, result.DocumentDebugInfo.Vectors!.Subscores!.DocumentBoost);
    }
}
