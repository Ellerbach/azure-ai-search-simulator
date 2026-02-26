using AzureAISearchSimulator.Api.Services;
using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AzureAISearchSimulator.Api.Tests.Services;

/// <summary>
/// Tests for scoring profile validation in IndexService.ValidateIndex().
/// Phase 4 of the scoring profile implementation plan.
/// </summary>
public class IndexServiceScoringProfileValidationTests
{
    private readonly IndexService _sut;

    public IndexServiceScoringProfileValidationTests()
    {
        var repositoryMock = new Mock<IIndexRepository>();
        var settings = Options.Create(new SimulatorSettings());
        var logger = new Mock<ILogger<IndexService>>();
        _sut = new IndexService(repositoryMock.Object, settings, logger.Object);
    }

    private static SearchIndex CreateIndexWithFields() => new()
    {
        Name = "test-index",
        Fields = new List<SearchField>
        {
            new() { Name = "id", Type = "Edm.String", Key = true },
            new() { Name = "title", Type = "Edm.String", Searchable = true },
            new() { Name = "description", Type = "Edm.String", Searchable = true },
            new() { Name = "rating", Type = "Edm.Double", Filterable = true },
            new() { Name = "count", Type = "Edm.Int32", Filterable = true },
            new() { Name = "bigcount", Type = "Edm.Int64", Filterable = true },
            new() { Name = "created", Type = "Edm.DateTimeOffset", Filterable = true },
            new() { Name = "location", Type = "Edm.GeographyPoint", Filterable = true },
            new() { Name = "tags", Type = "Collection(Edm.String)", Searchable = true, Filterable = true },
            new() { Name = "category", Type = "Edm.String", Searchable = true, Filterable = true },
            new() { Name = "active", Type = "Edm.Boolean", Filterable = true }
        }
    };

    // ─── Profile Name Validation ─────────────────────────────────────

    [Fact]
    public void ValidateIndex_ScoringProfileWithEmptyName_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new() { Name = "" }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Scoring profile name is required"));
    }

    [Fact]
    public void ValidateIndex_DuplicateScoringProfileNames_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new() { Name = "myprofile" },
            new() { Name = "myprofile" }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate scoring profile name: 'myprofile'"));
    }

    [Fact]
    public void ValidateIndex_ValidScoringProfile_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "boost-title",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "title", 2.0 } }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // ─── DefaultScoringProfile Validation ────────────────────────────

    [Fact]
    public void ValidateIndex_DefaultScoringProfile_ReferencesExistingProfile_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new() { Name = "myprofile" }
        };
        index.DefaultScoringProfile = "myprofile";

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_DefaultScoringProfile_ReferencesNonExistentProfile_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new() { Name = "myprofile" }
        };
        index.DefaultScoringProfile = "nonexistent";

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("defaultScoringProfile 'nonexistent'") && e.Contains("does not exist"));
    }

    [Fact]
    public void ValidateIndex_DefaultScoringProfile_NoProfilesDefined_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.DefaultScoringProfile = "nonexistent";

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("defaultScoringProfile 'nonexistent'") && e.Contains("does not exist"));
    }

    // ─── Text Weight Validation ──────────────────────────────────────

    [Fact]
    public void ValidateIndex_TextWeight_ReferencesUnknownField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "nonexistent", 2.0 } }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown field 'nonexistent'"));
    }

    [Fact]
    public void ValidateIndex_TextWeight_ReferencesNonSearchableField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "rating", 2.0 } }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("field 'rating' must be searchable"));
    }

    [Fact]
    public void ValidateIndex_TextWeight_ZeroWeight_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "title", 0 } }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be positive"));
    }

    [Fact]
    public void ValidateIndex_TextWeight_NegativeWeight_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "title", -1.5 } }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be positive"));
    }

    [Fact]
    public void ValidateIndex_TextWeight_ValidSearchableFields_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double>
                    {
                        { "title", 5.0 },
                        { "description", 2.0 },
                        { "tags", 1.5 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // ─── FunctionAggregation Validation ──────────────────────────────

    [Fact]
    public void ValidateIndex_InvalidFunctionAggregation_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                FunctionAggregation = "invalid"
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("invalid functionAggregation 'invalid'"));
    }

    [Theory]
    [InlineData("sum")]
    [InlineData("average")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("firstMatching")]
    public void ValidateIndex_ValidFunctionAggregation_NoErrors(string aggregation)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                FunctionAggregation = aggregation
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // ─── Freshness Function Validation ───────────────────────────────

    [Fact]
    public void ValidateIndex_FreshnessFunction_ValidDateTimeOffsetField_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_FreshnessFunction_NonDateTimeField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "title",
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("freshness") && e.Contains("Edm.DateTimeOffset"));
    }

    [Fact]
    public void ValidateIndex_FreshnessFunction_MissingParameters_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Freshness = null
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing the 'freshness' parameters"));
    }

    [Fact]
    public void ValidateIndex_FreshnessFunction_MissingBoostingDuration_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing 'boostingDuration'"));
    }

    // ─── Magnitude Function Validation ───────────────────────────────

    [Theory]
    [InlineData("rating")]     // Edm.Double
    [InlineData("count")]      // Edm.Int32
    [InlineData("bigcount")]   // Edm.Int64
    public void ValidateIndex_MagnitudeFunction_ValidNumericField_NoErrors(string fieldName)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "magnitude",
                        FieldName = fieldName,
                        Boost = 2.0,
                        Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 100 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_MagnitudeFunction_NonNumericField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "magnitude",
                        FieldName = "title",
                        Boost = 2.0,
                        Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 100 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("magnitude") && e.Contains("numeric type"));
    }

    [Fact]
    public void ValidateIndex_MagnitudeFunction_MissingParameters_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "magnitude",
                        FieldName = "rating",
                        Boost = 2.0,
                        Magnitude = null
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing the 'magnitude' parameters"));
    }

    // ─── Distance Function Validation ────────────────────────────────

    [Fact]
    public void ValidateIndex_DistanceFunction_ValidGeographyField_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "distance",
                        FieldName = "location",
                        Boost = 2.0,
                        Distance = new DistanceFunction { ReferencePointParameter = "currentLocation", BoostingDistance = 10 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_DistanceFunction_NonGeographyField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "distance",
                        FieldName = "title",
                        Boost = 2.0,
                        Distance = new DistanceFunction { ReferencePointParameter = "currentLocation", BoostingDistance = 10 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("distance") && e.Contains("Edm.GeographyPoint"));
    }

    [Fact]
    public void ValidateIndex_DistanceFunction_MissingParameters_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "distance",
                        FieldName = "location",
                        Boost = 2.0,
                        Distance = null
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing the 'distance' parameters"));
    }

    [Fact]
    public void ValidateIndex_DistanceFunction_MissingReferencePointParameter_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "distance",
                        FieldName = "location",
                        Boost = 2.0,
                        Distance = new DistanceFunction { ReferencePointParameter = "", BoostingDistance = 10 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing 'referencePointParameter'"));
    }

    // ─── Tag Function Validation ─────────────────────────────────────

    [Theory]
    [InlineData("category")]  // Edm.String
    [InlineData("tags")]      // Collection(Edm.String)
    public void ValidateIndex_TagFunction_ValidStringField_NoErrors(string fieldName)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = fieldName,
                        Boost = 2.0,
                        Tag = new TagFunction { TagsParameter = "myTags" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_TagFunction_NonStringField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "rating",
                        Boost = 2.0,
                        Tag = new TagFunction { TagsParameter = "myTags" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("tag") && e.Contains("Edm.String or Collection(Edm.String)"));
    }

    [Fact]
    public void ValidateIndex_TagFunction_MissingParameters_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "tags",
                        Boost = 2.0,
                        Tag = null
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing the 'tag' parameters"));
    }

    [Fact]
    public void ValidateIndex_TagFunction_MissingTagsParameter_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "tags",
                        Boost = 2.0,
                        Tag = new TagFunction { TagsParameter = "" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing 'tagsParameter'"));
    }

    // ─── General Function Validation ─────────────────────────────────

    [Fact]
    public void ValidateIndex_FunctionWithInvalidType_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "custom",
                        FieldName = "title",
                        Boost = 2.0
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("invalid function type 'custom'"));
    }

    [Fact]
    public void ValidateIndex_FunctionWithNoType_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "",
                        FieldName = "title",
                        Boost = 2.0
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no type specified"));
    }

    [Fact]
    public void ValidateIndex_FunctionWithUnknownField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "nonexistent",
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown field 'nonexistent'"));
    }

    [Fact]
    public void ValidateIndex_FunctionWithNoFieldName_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "",
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no fieldName"));
    }

    [Fact]
    public void ValidateIndex_FunctionWithZeroBoost_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("non-zero boost"));
    }

    [Fact]
    public void ValidateIndex_FunctionWithInvalidInterpolation_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Interpolation = "invalid",
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("invalid interpolation 'invalid'"));
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("constant")]
    [InlineData("quadratic")]
    [InlineData("logarithmic")]
    public void ValidateIndex_FunctionWithValidInterpolation_NoErrors(string interpolation)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Interpolation = interpolation,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // ─── Complex Profiles ────────────────────────────────────────────

    [Fact]
    public void ValidateIndex_ComplexProfileWithMultipleFunctions_AllValid_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "complex-profile",
                FunctionAggregation = "sum",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double>
                    {
                        { "title", 5.0 },
                        { "description", 2.0 }
                    }
                },
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 2.0,
                        Interpolation = "linear",
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    },
                    new()
                    {
                        Type = "magnitude",
                        FieldName = "rating",
                        Boost = 3.0,
                        Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 5 }
                    },
                    new()
                    {
                        Type = "distance",
                        FieldName = "location",
                        Boost = 2.0,
                        Distance = new DistanceFunction { ReferencePointParameter = "currentLocation", BoostingDistance = 10 }
                    },
                    new()
                    {
                        Type = "tag",
                        FieldName = "tags",
                        Boost = 1.5,
                        Tag = new TagFunction { TagsParameter = "userTags" }
                    }
                }
            }
        };
        index.DefaultScoringProfile = "complex-profile";

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateIndex_MultipleProfiles_Mixed_ReturnsOnlyRelevantErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "valid-profile",
                Text = new TextWeights
                {
                    Weights = new Dictionary<string, double> { { "title", 2.0 } }
                }
            },
            new()
            {
                Name = "invalid-profile",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "title", // wrong type for freshness
                        Boost = 2.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        // Only errors from the invalid profile
        Assert.Contains(result.Errors, e => e.Contains("invalid-profile") && e.Contains("freshness"));
        // No errors should start with valid profile references
        Assert.DoesNotContain(result.Errors, e => e.Contains("'valid-profile'"));
    }

    [Fact]
    public void ValidateIndex_NoScoringProfiles_NoDefaultProfile_NoErrors()
    {
        var index = CreateIndexWithFields();
        // No scoring profiles, no default - should be fine
        var result = _sut.ValidateIndex(index);
        Assert.True(result.IsValid);
    }

    // ─── Boolean field on tag function ───────────────────────────────

    [Fact]
    public void ValidateIndex_TagFunction_BooleanField_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "active",
                        Boost = 2.0,
                        Tag = new TagFunction { TagsParameter = "myTags" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("tag") && e.Contains("Edm.String or Collection(Edm.String)"));
    }

    // ─── Phase 8: Azure Docs Compliance Gaps ─────────────────────────

    // Gap 4 — Tag function interpolation restriction

    [Theory]
    [InlineData("quadratic")]
    [InlineData("logarithmic")]
    public void ValidateIndex_TagFunction_UnsupportedInterpolation_ReturnsError(string interpolation)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "tags",
                        Boost = 2.0,
                        Interpolation = interpolation,
                        Tag = new TagFunction { TagsParameter = "myTags" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("tag function") && e.Contains("only supports 'linear' or 'constant'"));
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("constant")]
    public void ValidateIndex_TagFunction_SupportedInterpolation_NoErrors(string interpolation)
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "tag",
                        FieldName = "tags",
                        Boost = 2.0,
                        Interpolation = interpolation,
                        Tag = new TagFunction { TagsParameter = "myTags" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // Gap 5 — Filterable field requirement

    [Fact]
    public void ValidateIndex_FunctionField_NotFilterable_ReturnsError()
    {
        var index = CreateIndexWithFields();
        // 'description' is searchable but NOT filterable
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "magnitude",
                        FieldName = "description",
                        Boost = 2.0,
                        Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 100 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be filterable"));
    }

    [Fact]
    public void ValidateIndex_FunctionField_Filterable_NoFilterableError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "magnitude",
                        FieldName = "rating",
                        Boost = 2.0,
                        Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 5 }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // Gap 6 — Negative boost allowed

    [Fact]
    public void ValidateIndex_FunctionWithNegativeBoost_NoErrors()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = -10.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.True(result.IsValid);
    }

    // Gap 7 — Boost of 1.0 rejected

    [Fact]
    public void ValidateIndex_FunctionWithBoostOfOne_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = new List<ScoringProfile>
        {
            new()
            {
                Name = "profile1",
                Functions = new List<ScoringFunction>
                {
                    new()
                    {
                        Type = "freshness",
                        FieldName = "created",
                        Boost = 1.0,
                        Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
                    }
                }
            }
        };

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("boost must not be 1.0"));
    }

    // Gap 8 — Max 100 profiles

    [Fact]
    public void ValidateIndex_MoreThan100Profiles_ReturnsError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = Enumerable.Range(1, 101)
            .Select(i => new ScoringProfile { Name = $"profile{i}" })
            .ToList();

        var result = _sut.ValidateIndex(index);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("101 scoring profiles") && e.Contains("maximum is 100"));
    }

    [Fact]
    public void ValidateIndex_Exactly100Profiles_NoMaxError()
    {
        var index = CreateIndexWithFields();
        index.ScoringProfiles = Enumerable.Range(1, 100)
            .Select(i => new ScoringProfile { Name = $"profile{i}" })
            .ToList();

        var result = _sut.ValidateIndex(index);

        // Should not have the max profiles error (may have other errors but not the count one)
        Assert.DoesNotContain(result.Errors, e => e.Contains("maximum is 100"));
    }
}
