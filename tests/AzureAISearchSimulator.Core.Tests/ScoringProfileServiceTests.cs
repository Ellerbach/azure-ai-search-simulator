using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;
using AzureAISearchSimulator.Search;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace AzureAISearchSimulator.Core.Tests;

/// <summary>
/// Unit tests for ScoringProfileService covering:
/// - Profile resolution (explicit, default, none, missing)
/// - Text weight extraction
/// - Scoring parameter parsing
/// - Freshness function evaluation
/// - Magnitude function evaluation
/// - Distance function evaluation
/// - Tag function evaluation
/// - Interpolation functions (linear, constant, quadratic, logarithmic)
/// - Aggregation modes (sum, average, minimum, maximum, firstMatching)
/// - Full document boost calculation
/// </summary>
public class ScoringProfileServiceTests
{
    private readonly ScoringProfileService _sut;

    public ScoringProfileServiceTests()
    {
        _sut = new ScoringProfileService(Mock.Of<ILogger<ScoringProfileService>>());
    }

    #region ResolveProfile

    [Fact]
    public void ResolveProfile_ExplicitProfileInRequest_ReturnsMatchingProfile()
    {
        var index = CreateIndexWithProfiles("profile-a", "profile-b");
        var request = new SearchRequest { ScoringProfile = "profile-b" };

        var result = _sut.ResolveProfile(index, request);

        Assert.NotNull(result);
        Assert.Equal("profile-b", result.Name);
    }

    [Fact]
    public void ResolveProfile_DefaultProfile_UsedWhenNoExplicit()
    {
        var index = CreateIndexWithProfiles("profile-a", "profile-b");
        index.DefaultScoringProfile = "profile-a";
        var request = new SearchRequest();

        var result = _sut.ResolveProfile(index, request);

        Assert.NotNull(result);
        Assert.Equal("profile-a", result.Name);
    }

    [Fact]
    public void ResolveProfile_ExplicitOverridesDefault()
    {
        var index = CreateIndexWithProfiles("profile-a", "profile-b");
        index.DefaultScoringProfile = "profile-a";
        var request = new SearchRequest { ScoringProfile = "profile-b" };

        var result = _sut.ResolveProfile(index, request);

        Assert.NotNull(result);
        Assert.Equal("profile-b", result.Name);
    }

    [Fact]
    public void ResolveProfile_NoProfileConfigured_ReturnsNull()
    {
        var index = CreateIndexWithProfiles("profile-a");
        var request = new SearchRequest();

        var result = _sut.ResolveProfile(index, request);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveProfile_NonExistentProfile_ReturnsNull()
    {
        var index = CreateIndexWithProfiles("profile-a");
        var request = new SearchRequest { ScoringProfile = "does-not-exist" };

        var result = _sut.ResolveProfile(index, request);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveProfile_CaseInsensitive()
    {
        var index = CreateIndexWithProfiles("MyProfile");
        var request = new SearchRequest { ScoringProfile = "myprofile" };

        var result = _sut.ResolveProfile(index, request);

        Assert.NotNull(result);
        Assert.Equal("MyProfile", result.Name);
    }

    [Fact]
    public void ResolveProfile_NullScoringProfiles_ReturnsNull()
    {
        var index = new SearchIndex { Name = "test", ScoringProfiles = null };
        var request = new SearchRequest { ScoringProfile = "any" };

        var result = _sut.ResolveProfile(index, request);

        Assert.Null(result);
    }

    #endregion

    #region GetTextWeights

    [Fact]
    public void GetTextWeights_WithWeights_ReturnsWeights()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            Text = new TextWeights
            {
                Weights = new Dictionary<string, double>
                {
                    ["title"] = 3.0,
                    ["description"] = 1.5
                }
            }
        };

        var result = _sut.GetTextWeights(profile);

        Assert.NotNull(result);
        Assert.Equal(3.0, result["title"]);
        Assert.Equal(1.5, result["description"]);
    }

    [Fact]
    public void GetTextWeights_NullProfile_ReturnsNull()
    {
        var result = _sut.GetTextWeights(null);
        Assert.Null(result);
    }

    [Fact]
    public void GetTextWeights_NoTextSection_ReturnsNull()
    {
        var profile = new ScoringProfile { Name = "test" };
        var result = _sut.GetTextWeights(profile);
        Assert.Null(result);
    }

    [Fact]
    public void GetTextWeights_EmptyWeights_ReturnsNull()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            Text = new TextWeights { Weights = new Dictionary<string, double>() }
        };

        var result = _sut.GetTextWeights(profile);
        Assert.Null(result);
    }

    #endregion

    #region ParseScoringParameters

    [Fact]
    public void ParseScoringParameters_Null_ReturnsEmptyDict()
    {
        var result = _sut.ParseScoringParameters(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseScoringParameters_SimpleValues()
    {
        var parameters = new List<string>
        {
            "preferredTags-swimming,hiking",
            "rating-5"
        };

        var result = _sut.ParseScoringParameters(parameters);

        Assert.Equal(2, result.Count);
        Assert.Equal("swimming,hiking", result["preferredTags"]);
        Assert.Equal("5", result["rating"]);
    }

    [Fact]
    public void ParseScoringParameters_GeoPointWithNegativeCoordinates()
    {
        // Azure format: paramName--47.6,122.3 (double dash because the lat is negative)
        var parameters = new List<string> { "currentLocation--47.6062,-122.3321" };

        var result = _sut.ParseScoringParameters(parameters);

        Assert.Single(result);
        Assert.Equal("-47.6062,-122.3321", result["currentLocation"]);
    }

    [Fact]
    public void ParseScoringParameters_SkipsEmptyAndInvalid()
    {
        var parameters = new List<string> { "", "  ", "noDash", "valid-value" };

        var result = _sut.ParseScoringParameters(parameters);

        Assert.Single(result);
        Assert.Equal("value", result["valid"]);
    }

    [Fact]
    public void ParseScoringParameters_CaseInsensitiveLookup()
    {
        var parameters = new List<string> { "MyParam-myValue" };
        var result = _sut.ParseScoringParameters(parameters);

        Assert.True(result.ContainsKey("myparam"));
        Assert.True(result.ContainsKey("MyParam"));
        Assert.Equal("myValue", result["MYPARAM"]);
    }

    #endregion

    #region Freshness Function

    [Fact]
    public void EvaluateFreshness_RecentDoc_GetsHighBoost()
    {
        var function = CreateFreshnessFunction("P365D", boost: 2.0);
        var recentDate = DateTime.UtcNow.AddDays(-10);

        var result = _sut.EvaluateFreshnessFunction(function, recentDate.ToString("O"));

        Assert.NotNull(result);
        Assert.True(result.Value > 0.9, $"Expected > 0.9 but got {result.Value}");
    }

    [Fact]
    public void EvaluateFreshness_OldDoc_GetsLowBoost()
    {
        var function = CreateFreshnessFunction("P30D", boost: 2.0);
        var oldDate = DateTime.UtcNow.AddDays(-25);

        var result = _sut.EvaluateFreshnessFunction(function, oldDate.ToString("O"));

        Assert.NotNull(result);
        Assert.True(result.Value < 0.2, $"Expected < 0.2 but got {result.Value}");
    }

    [Fact]
    public void EvaluateFreshness_BeyondDuration_GetsZero()
    {
        var function = CreateFreshnessFunction("P30D", boost: 2.0);
        var veryOldDate = DateTime.UtcNow.AddDays(-60);

        var result = _sut.EvaluateFreshnessFunction(function, veryOldDate.ToString("O"));

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value);
    }

    [Fact]
    public void EvaluateFreshness_NullField_ReturnsNull()
    {
        var function = CreateFreshnessFunction("P30D", boost: 2.0);
        var result = _sut.EvaluateFreshnessFunction(function, null);
        Assert.Null(result);
    }

    [Fact]
    public void EvaluateFreshness_DateTimeOffsetValue()
    {
        var function = CreateFreshnessFunction("P365D", boost: 1.0);
        var recentDate = DateTimeOffset.UtcNow.AddDays(-1);

        var result = _sut.EvaluateFreshnessFunction(function, recentDate);

        Assert.NotNull(result);
        Assert.True(result.Value > 0.99);
    }

    [Fact]
    public void EvaluateFreshness_JsonElementDate()
    {
        var function = CreateFreshnessFunction("P365D", boost: 1.0);
        var dateStr = DateTime.UtcNow.AddDays(-10).ToString("O");
        var jsonElement = JsonDocument.Parse($"\"{dateStr}\"").RootElement;

        var result = _sut.EvaluateFreshnessFunction(function, jsonElement);

        Assert.NotNull(result);
        Assert.True(result.Value > 0.9);
    }

    #endregion

    #region Magnitude Function

    [Fact]
    public void EvaluateMagnitude_MiddleOfRange()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0);

        var result = _sut.EvaluateMagnitudeFunction(function, 50.0);

        Assert.NotNull(result);
        Assert.Equal(0.5, result.Value, 2);
    }

    [Fact]
    public void EvaluateMagnitude_AtRangeStart_GetsZero()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0);

        var result = _sut.EvaluateMagnitudeFunction(function, 0.0);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value, 2);
    }

    [Fact]
    public void EvaluateMagnitude_AtRangeEnd_GetsOne()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0);

        var result = _sut.EvaluateMagnitudeFunction(function, 100.0);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value, 2);
    }

    [Fact]
    public void EvaluateMagnitude_BeyondRange_WithConstantBoost()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0, constantBeyondRange: true);

        var result = _sut.EvaluateMagnitudeFunction(function, 150.0);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value); // Clamped to max
    }

    [Fact]
    public void EvaluateMagnitude_BeyondRange_WithoutConstantBoost()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0, constantBeyondRange: false);

        var result = _sut.EvaluateMagnitudeFunction(function, 150.0);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value); // No boost outside range
    }

    [Fact]
    public void EvaluateMagnitude_BelowRange_WithConstantBoost()
    {
        var function = CreateMagnitudeFunction(10, 100, boost: 2.0, constantBeyondRange: true);

        var result = _sut.EvaluateMagnitudeFunction(function, 5.0);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value); // Clamped to min
    }

    [Fact]
    public void EvaluateMagnitude_NullField_ReturnsNull()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 2.0);
        var result = _sut.EvaluateMagnitudeFunction(function, null);
        Assert.Null(result);
    }

    [Fact]
    public void EvaluateMagnitude_IntValue()
    {
        var function = CreateMagnitudeFunction(0, 10, boost: 1.0);
        var result = _sut.EvaluateMagnitudeFunction(function, 7);

        Assert.NotNull(result);
        Assert.Equal(0.7, result.Value, 2);
    }

    [Fact]
    public void EvaluateMagnitude_JsonElementNumber()
    {
        var function = CreateMagnitudeFunction(0, 100, boost: 1.0);
        var jsonElement = JsonDocument.Parse("75").RootElement;

        var result = _sut.EvaluateMagnitudeFunction(function, jsonElement);

        Assert.NotNull(result);
        Assert.Equal(0.75, result.Value, 2);
    }

    #endregion

    #region Distance Function

    [Fact]
    public void EvaluateDistance_CloseDistance_HighBoost()
    {
        var function = CreateDistanceFunction("currentLocation", 100, boost: 2.0);
        // Seattle: 47.6062, -122.3321
        var scoringParams = new Dictionary<string, string> { ["currentLocation"] = "47.6062,-122.3321" };
        // Very close to Seattle
        var docPoint = "47.6063,-122.3322";

        var result = _sut.EvaluateDistanceFunction(function, docPoint, scoringParams);

        Assert.NotNull(result);
        Assert.True(result.Value > 0.99, $"Expected high boost for close distance, got {result.Value}");
    }

    [Fact]
    public void EvaluateDistance_FarDistance_LowBoost()
    {
        var function = CreateDistanceFunction("currentLocation", 50, boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["currentLocation"] = "47.6062,-122.3321" };
        // Portland is ~280 km from Seattle, well beyond 50 km boosting distance
        var docPoint = "45.5152,-122.6784";

        var result = _sut.EvaluateDistanceFunction(function, docPoint, scoringParams);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value);
    }

    [Fact]
    public void EvaluateDistance_MissingParameter_ReturnsNull()
    {
        var function = CreateDistanceFunction("currentLocation", 100, boost: 2.0);
        var scoringParams = new Dictionary<string, string>(); // No currentLocation param

        var result = _sut.EvaluateDistanceFunction(function, "47.6062,-122.3321", scoringParams);

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateDistance_NullFieldValue_ReturnsNull()
    {
        var function = CreateDistanceFunction("currentLocation", 100, boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["currentLocation"] = "47.6062,-122.3321" };

        var result = _sut.EvaluateDistanceFunction(function, null, scoringParams);

        Assert.Null(result);
    }

    #endregion

    #region Tag Function

    [Fact]
    public void EvaluateTag_MatchingTag_ReturnsOne()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming,hiking" };

        // Document has "swimming" tag
        var result = _sut.EvaluateTagFunction(function, "swimming", scoringParams);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }

    [Fact]
    public void EvaluateTag_NoMatch_ReturnsZero()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming,hiking" };

        var result = _sut.EvaluateTagFunction(function, "cycling", scoringParams);

        Assert.NotNull(result);
        Assert.Equal(0.0, result.Value);
    }

    [Fact]
    public void EvaluateTag_CollectionField_MatchesAny()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming,hiking" };

        // Document has multiple tags as a list
        var docTags = new List<string> { "cycling", "hiking", "running" };
        var result = _sut.EvaluateTagFunction(function, docTags, scoringParams);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }

    [Fact]
    public void EvaluateTag_CaseInsensitive()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "Swimming" };

        var result = _sut.EvaluateTagFunction(function, "swimming", scoringParams);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }

    [Fact]
    public void EvaluateTag_MissingParameter_ReturnsNull()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string>();

        var result = _sut.EvaluateTagFunction(function, "swimming", scoringParams);

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateTag_JsonElementArray()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming" };

        var jsonElement = JsonDocument.Parse("[\"swimming\", \"hiking\"]").RootElement;
        var result = _sut.EvaluateTagFunction(function, jsonElement, scoringParams);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }

    [Fact]
    public void EvaluateTag_JsonElementString()
    {
        var function = CreateTagFunction("preferredTags", boost: 2.0);
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming" };

        var jsonElement = JsonDocument.Parse("\"swimming\"").RootElement;
        var result = _sut.EvaluateTagFunction(function, jsonElement, scoringParams);

        Assert.NotNull(result);
        Assert.Equal(1.0, result.Value);
    }

    #endregion

    #region Interpolation

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 1.0)]
    public void Interpolation_Linear(double input, double expected)
    {
        var result = ScoringProfileService.ApplyInterpolation(input, "linear");
        Assert.Equal(expected, result, 4);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.01, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1.0, 1.0)]
    public void Interpolation_Constant(double input, double expected)
    {
        var result = ScoringProfileService.ApplyInterpolation(input, "constant");
        Assert.Equal(expected, result, 4);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.25)]
    [InlineData(1.0, 1.0)]
    public void Interpolation_Quadratic(double input, double expected)
    {
        var result = ScoringProfileService.ApplyInterpolation(input, "quadratic");
        Assert.Equal(expected, result, 4);
    }

    [Fact]
    public void Interpolation_Logarithmic_ZeroReturnsZero()
    {
        var result = ScoringProfileService.ApplyInterpolation(0.0, "logarithmic");
        Assert.Equal(0.0, result, 4);
    }

    [Fact]
    public void Interpolation_Logarithmic_OneReturnsOne()
    {
        var result = ScoringProfileService.ApplyInterpolation(1.0, "logarithmic");
        Assert.Equal(1.0, result, 4);
    }

    [Fact]
    public void Interpolation_Logarithmic_MiddleValue()
    {
        var result = ScoringProfileService.ApplyInterpolation(0.5, "logarithmic");
        // Logarithmic should produce a value between 0 and 1, and > 0.5 (fast initial decay)
        Assert.True(result > 0.0 && result < 1.0, $"Expected (0,1) but got {result}");
    }

    [Fact]
    public void Interpolation_NullDefaultsToLinear()
    {
        var result = ScoringProfileService.ApplyInterpolation(0.5, null);
        Assert.Equal(0.5, result, 4);
    }

    [Fact]
    public void Interpolation_ClampsNegativeInput()
    {
        var result = ScoringProfileService.ApplyInterpolation(-0.5, "linear");
        Assert.Equal(0.0, result, 4);
    }

    [Fact]
    public void Interpolation_ClampsAboveOneInput()
    {
        var result = ScoringProfileService.ApplyInterpolation(1.5, "linear");
        Assert.Equal(1.0, result, 4);
    }

    #endregion

    #region Aggregation

    [Fact]
    public void Aggregation_Sum()
    {
        var boosts = new List<double> { 1.0, 2.0, 3.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, "sum");
        Assert.Equal(6.0, result);
    }

    [Fact]
    public void Aggregation_Average()
    {
        var boosts = new List<double> { 1.0, 2.0, 3.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, "average");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Aggregation_Minimum()
    {
        var boosts = new List<double> { 1.0, 2.0, 3.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, "minimum");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Aggregation_Maximum()
    {
        var boosts = new List<double> { 1.0, 2.0, 3.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, "maximum");
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Aggregation_FirstMatching()
    {
        var boosts = new List<double> { 0.0, 2.0, 3.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, "firstMatching");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Aggregation_DefaultIsSum()
    {
        var boosts = new List<double> { 1.0, 2.0 };
        var result = ScoringProfileService.AggregateBoosts(boosts, null);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Aggregation_EmptyList_ReturnsZero()
    {
        var result = ScoringProfileService.AggregateBoosts(new List<double>(), "sum");
        Assert.Equal(0.0, result);
    }

    #endregion

    #region CalculateDocumentBoost

    [Fact]
    public void CalculateDocumentBoost_NoFunctions_ReturnsOne()
    {
        var profile = new ScoringProfile { Name = "test", Functions = null };
        var docFields = new Dictionary<string, object?>();
        var scoringParams = new Dictionary<string, string>();

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateDocumentBoost_EmptyFunctions_ReturnsOne()
    {
        var profile = new ScoringProfile { Name = "test", Functions = new List<ScoringFunction>() };
        var docFields = new Dictionary<string, object?>();
        var scoringParams = new Dictionary<string, string>();

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public void CalculateDocumentBoost_MagnitudeFunction_ProducesBoost()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            Functions = new List<ScoringFunction>
            {
                new()
                {
                    Type = "magnitude",
                    FieldName = "rating",
                    Boost = 2.0,
                    Magnitude = new MagnitudeFunction
                    {
                        BoostingRangeStart = 0,
                        BoostingRangeEnd = 5
                    }
                }
            }
        };

        var docFields = new Dictionary<string, object?> { ["rating"] = 5.0 };
        var scoringParams = new Dictionary<string, string>();

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        // normalized=1.0, interpolated(linear)=1.0, functionBoost=1.0*2.0=2.0
        // documentBoost = 1.0 + 2.0 = 3.0
        Assert.Equal(3.0, result, 2);
    }

    [Fact]
    public void CalculateDocumentBoost_TagFunction_MatchingTag()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            Functions = new List<ScoringFunction>
            {
                new()
                {
                    Type = "tag",
                    FieldName = "tags",
                    Boost = 5.0,
                    Tag = new TagFunction { TagsParameter = "preferredTags" }
                }
            }
        };

        var docFields = new Dictionary<string, object?> { ["tags"] = "swimming" };
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming,hiking" };

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        // Tag match → 1.0. functionBoost = 1.0 * 5.0 = 5.0. documentBoost = 1.0 + 5.0 = 6.0
        Assert.Equal(6.0, result, 2);
    }

    [Fact]
    public void CalculateDocumentBoost_MultipleFunctions_SumAggregation()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            FunctionAggregation = "sum",
            Functions = new List<ScoringFunction>
            {
                new()
                {
                    Type = "magnitude",
                    FieldName = "rating",
                    Boost = 2.0,
                    Magnitude = new MagnitudeFunction
                    {
                        BoostingRangeStart = 0,
                        BoostingRangeEnd = 10
                    }
                },
                new()
                {
                    Type = "tag",
                    FieldName = "tags",
                    Boost = 3.0,
                    Tag = new TagFunction { TagsParameter = "preferredTags" }
                }
            }
        };

        var docFields = new Dictionary<string, object?>
        {
            ["rating"] = 5.0,  // normalized=0.5, boost=2.0 → 1.0
            ["tags"] = "swimming"  // match → 1.0 * 3.0 = 3.0
        };
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming" };

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        // sum(1.0, 3.0) = 4.0 → documentBoost = 1.0 + 4.0 = 5.0
        Assert.Equal(5.0, result, 2);
    }

    [Fact]
    public void CalculateDocumentBoost_MultipleFunctions_AverageAggregation()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            FunctionAggregation = "average",
            Functions = new List<ScoringFunction>
            {
                new()
                {
                    Type = "magnitude",
                    FieldName = "rating",
                    Boost = 2.0,
                    Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 10 }
                },
                new()
                {
                    Type = "tag",
                    FieldName = "tags",
                    Boost = 4.0,
                    Tag = new TagFunction { TagsParameter = "preferredTags" }
                }
            }
        };

        var docFields = new Dictionary<string, object?>
        {
            ["rating"] = 5.0,      // normalized=0.5, functionBoost=0.5*2.0=1.0
            ["tags"] = "swimming"   // match → functionBoost=1.0*4.0=4.0
        };
        var scoringParams = new Dictionary<string, string> { ["preferredTags"] = "swimming" };

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        // average(1.0, 4.0) = 2.5 → documentBoost = 1.0 + 2.5 = 3.5
        Assert.Equal(3.5, result, 2);
    }

    [Fact]
    public void CalculateDocumentBoost_MissingField_FunctionContributesZero()
    {
        var profile = new ScoringProfile
        {
            Name = "test",
            Functions = new List<ScoringFunction>
            {
                new()
                {
                    Type = "magnitude",
                    FieldName = "nonexistent",
                    Boost = 2.0,
                    Magnitude = new MagnitudeFunction { BoostingRangeStart = 0, BoostingRangeEnd = 10 }
                }
            }
        };

        var docFields = new Dictionary<string, object?>();
        var scoringParams = new Dictionary<string, string>();

        var result = _sut.CalculateDocumentBoost(profile, docFields, scoringParams);

        // Missing field → 0, sum(0) = 0, documentBoost = 1.0 + 0.0 = 1.0
        Assert.Equal(1.0, result, 2);
    }

    #endregion

    #region Parsing Helpers

    [Fact]
    public void ParseIsoDuration_Days()
    {
        var result = ScoringProfileService.ParseIsoDuration("P365D");
        Assert.NotNull(result);
        Assert.Equal(365, result.Value.TotalDays);
    }

    [Fact]
    public void ParseIsoDuration_Hours()
    {
        var result = ScoringProfileService.ParseIsoDuration("PT12H");
        Assert.NotNull(result);
        Assert.Equal(12, result.Value.TotalHours);
    }

    [Fact]
    public void ParseIsoDuration_InvalidFormat_ReturnsNull()
    {
        var result = ScoringProfileService.ParseIsoDuration("invalid");
        Assert.Null(result);
    }

    [Fact]
    public void ParseIsoDuration_Null_ReturnsNull()
    {
        var result = ScoringProfileService.ParseIsoDuration(null);
        Assert.Null(result);
    }

    [Fact]
    public void ParseIsoDuration_WithoutPPrefix_Days()
    {
        var result = ScoringProfileService.ParseIsoDuration("365D");
        Assert.NotNull(result);
        Assert.Equal(365, result.Value.TotalDays);
    }

    [Fact]
    public void ParseIsoDuration_WithoutPPrefix_DaysAndHours()
    {
        var result = ScoringProfileService.ParseIsoDuration("30DT12H");
        Assert.NotNull(result);
        Assert.Equal(30.5, result.Value.TotalDays);
    }

    [Fact]
    public void ParseGeoPoint_LatLonString()
    {
        var result = ScoringProfileService.ParseGeoPoint("47.6062,-122.3321");
        Assert.NotNull(result);
        Assert.Equal(47.6062, result.Value.Lat, 4);
        Assert.Equal(-122.3321, result.Value.Lon, 4);
    }

    [Fact]
    public void ParseGeoPoint_WKT()
    {
        var result = ScoringProfileService.ParseGeoPoint("POINT(-122.3321 47.6062)");
        Assert.NotNull(result);
        Assert.Equal(47.6062, result.Value.Lat, 4);
        Assert.Equal(-122.3321, result.Value.Lon, 4);
    }

    [Fact]
    public void ParseGeoPoint_GeoJson()
    {
        var jsonElement = JsonDocument.Parse("{\"type\":\"Point\",\"coordinates\":[-122.3321,47.6062]}").RootElement;
        var result = ScoringProfileService.ParseGeoPoint(jsonElement);
        Assert.NotNull(result);
        Assert.Equal(47.6062, result.Value.Lat, 4);
        Assert.Equal(-122.3321, result.Value.Lon, 4);
    }

    [Fact]
    public void ParseGeoPoint_Null_ReturnsNull()
    {
        var result = ScoringProfileService.ParseGeoPoint(null);
        Assert.Null(result);
    }

    [Fact]
    public void ParseNumericValue_Double()
    {
        Assert.Equal(3.14, ScoringProfileService.ParseNumericValue(3.14));
    }

    [Fact]
    public void ParseNumericValue_Int()
    {
        Assert.Equal(42.0, ScoringProfileService.ParseNumericValue(42));
    }

    [Fact]
    public void ParseNumericValue_JsonElement()
    {
        var je = JsonDocument.Parse("99.5").RootElement;
        Assert.Equal(99.5, ScoringProfileService.ParseNumericValue(je));
    }

    [Fact]
    public void ParseNumericValue_Null_ReturnsNull()
    {
        Assert.Null(ScoringProfileService.ParseNumericValue(null));
    }

    [Fact]
    public void HaversineDistance_SamePoint_IsZero()
    {
        var distance = ScoringProfileService.HaversineDistance(47.6062, -122.3321, 47.6062, -122.3321);
        Assert.Equal(0.0, distance, 4);
    }

    [Fact]
    public void HaversineDistance_SeattleToPortland()
    {
        // ~280 km
        var distance = ScoringProfileService.HaversineDistance(47.6062, -122.3321, 45.5152, -122.6784);
        Assert.True(distance > 230 && distance < 280, $"Expected ~230-280 km, got {distance}");
    }

    [Fact]
    public void GetDocumentTagValues_String()
    {
        var result = ScoringProfileService.GetDocumentTagValues("tag1");
        Assert.Single(result);
        Assert.Equal("tag1", result[0]);
    }

    [Fact]
    public void GetDocumentTagValues_ListOfStrings()
    {
        var result = ScoringProfileService.GetDocumentTagValues(new List<string> { "a", "b" });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetDocumentTagValues_Null_ReturnsEmpty()
    {
        var result = ScoringProfileService.GetDocumentTagValues(null);
        Assert.Empty(result);
    }

    #endregion

    #region Helpers

    private static SearchIndex CreateIndexWithProfiles(params string[] profileNames)
    {
        return new SearchIndex
        {
            Name = "test-index",
            Fields = new List<SearchField>
            {
                new() { Name = "id", Type = "Edm.String", Key = true }
            },
            ScoringProfiles = profileNames.Select(n => new ScoringProfile { Name = n }).ToList()
        };
    }

    private static ScoringFunction CreateFreshnessFunction(string boostingDuration, double boost)
    {
        return new ScoringFunction
        {
            Type = "freshness",
            FieldName = "lastModified",
            Boost = boost,
            Freshness = new FreshnessFunction { BoostingDuration = boostingDuration }
        };
    }

    private static ScoringFunction CreateMagnitudeFunction(double start, double end, double boost, bool? constantBeyondRange = null)
    {
        return new ScoringFunction
        {
            Type = "magnitude",
            FieldName = "rating",
            Boost = boost,
            Magnitude = new MagnitudeFunction
            {
                BoostingRangeStart = start,
                BoostingRangeEnd = end,
                ConstantBoostBeyondRange = constantBeyondRange
            }
        };
    }

    private static ScoringFunction CreateDistanceFunction(string paramName, double boostingDistance, double boost)
    {
        return new ScoringFunction
        {
            Type = "distance",
            FieldName = "location",
            Boost = boost,
            Distance = new DistanceFunction
            {
                ReferencePointParameter = paramName,
                BoostingDistance = boostingDistance
            }
        };
    }

    private static ScoringFunction CreateTagFunction(string paramName, double boost)
    {
        return new ScoringFunction
        {
            Type = "tag",
            FieldName = "tags",
            Boost = boost,
            Tag = new TagFunction { TagsParameter = paramName }
        };
    }

    #endregion

    #region NormalizeParameters Tests

    [Fact]
    public void NormalizeParameters_Freshness_MapsParametersToFreshnessProperty()
    {
        var json = """{"boostingDuration": "P365D"}""";
        var func = new ScoringFunction
        {
            Type = "freshness",
            FieldName = "lastUpdated",
            Boost = 2.0,
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        Assert.NotNull(func.Freshness);
        Assert.Equal("P365D", func.Freshness.BoostingDuration);
        Assert.Null(func.Parameters);
    }

    [Fact]
    public void NormalizeParameters_Magnitude_MapsParametersToMagnitudeProperty()
    {
        var json = """{"boostingRangeStart": 1, "boostingRangeEnd": 5, "constantBoostBeyondRange": true}""";
        var func = new ScoringFunction
        {
            Type = "magnitude",
            FieldName = "rating",
            Boost = 2.0,
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        Assert.NotNull(func.Magnitude);
        Assert.Equal(1, func.Magnitude.BoostingRangeStart);
        Assert.Equal(5, func.Magnitude.BoostingRangeEnd);
        Assert.True(func.Magnitude.ConstantBoostBeyondRange);
        Assert.Null(func.Parameters);
    }

    [Fact]
    public void NormalizeParameters_Distance_MapsParametersToDistanceProperty()
    {
        var json = """{"referencePointParameter": "currentLocation", "boostingDistance": 10}""";
        var func = new ScoringFunction
        {
            Type = "distance",
            FieldName = "location",
            Boost = 5.0,
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        Assert.NotNull(func.Distance);
        Assert.Equal("currentLocation", func.Distance.ReferencePointParameter);
        Assert.Equal(10, func.Distance.BoostingDistance);
        Assert.Null(func.Parameters);
    }

    [Fact]
    public void NormalizeParameters_Tag_MapsParametersToTagProperty()
    {
        var json = """{"tagsParameter": "myTags"}""";
        var func = new ScoringFunction
        {
            Type = "tag",
            FieldName = "tags",
            Boost = 2.0,
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        Assert.NotNull(func.Tag);
        Assert.Equal("myTags", func.Tag.TagsParameter);
        Assert.Null(func.Parameters);
    }

    [Fact]
    public void NormalizeParameters_DoesNotOverrideExistingTypeProperty()
    {
        var json = """{"boostingDuration": "P30D"}""";
        var func = new ScoringFunction
        {
            Type = "freshness",
            FieldName = "lastUpdated",
            Boost = 2.0,
            Freshness = new FreshnessFunction { BoostingDuration = "P365D" },
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        // Original value preserved — parameters alias ignored when type-specific property already set
        Assert.Equal("P365D", func.Freshness.BoostingDuration);
    }

    [Fact]
    public void NormalizeParameters_NoParameters_DoesNothing()
    {
        var func = new ScoringFunction
        {
            Type = "freshness",
            FieldName = "lastUpdated",
            Boost = 2.0,
            Freshness = new FreshnessFunction { BoostingDuration = "P365D" }
        };

        func.NormalizeParameters();

        Assert.Equal("P365D", func.Freshness.BoostingDuration);
    }

    [Fact]
    public void NormalizeParameters_LegacyDurationFormat_WorksViaSerialization()
    {
        // The "365D" format from Azure docs example
        var json = """{"boostingDuration": "365D"}""";
        var func = new ScoringFunction
        {
            Type = "freshness",
            FieldName = "lastUpdated",
            Boost = 2.0,
            Parameters = JsonDocument.Parse(json).RootElement
        };

        func.NormalizeParameters();

        Assert.NotNull(func.Freshness);
        Assert.Equal("365D", func.Freshness.BoostingDuration);
    }

    #endregion
}
