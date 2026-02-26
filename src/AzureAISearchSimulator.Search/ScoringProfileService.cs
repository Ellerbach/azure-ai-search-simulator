using System.Globalization;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using AzureAISearchSimulator.Core.Models;
using AzureAISearchSimulator.Core.Services;

namespace AzureAISearchSimulator.Search;

/// <summary>
/// Evaluates scoring profiles and computes document boost multipliers.
/// Implements the Azure AI Search scoring profile behavior:
/// - Text weights are returned for Lucene field-level boosting at query time.
/// - Scoring functions (freshness, magnitude, distance, tag) produce per-document boosts.
/// - Function results are combined via the configured aggregation method.
/// </summary>
public class ScoringProfileService : IScoringProfileService
{
    private readonly ILogger<ScoringProfileService> _logger;

    public ScoringProfileService(ILogger<ScoringProfileService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ScoringProfile? ResolveProfile(SearchIndex index, SearchRequest request)
    {
        string? profileName = null;

        // Explicit profile in the request takes precedence
        if (!string.IsNullOrWhiteSpace(request.ScoringProfile))
        {
            profileName = request.ScoringProfile;
        }
        // Fall back to the index's default scoring profile
        else if (!string.IsNullOrWhiteSpace(index.DefaultScoringProfile))
        {
            profileName = index.DefaultScoringProfile;
        }

        if (profileName == null)
        {
            return null;
        }

        var profile = index.ScoringProfiles?
            .FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            _logger.LogWarning("Scoring profile '{ProfileName}' not found in index '{IndexName}'",
                profileName, index.Name);
        }
        else
        {
            _logger.LogInformation("Using scoring profile '{ProfileName}' for search on index '{IndexName}'",
                profileName, index.Name);
        }

        return profile;
    }

    /// <inheritdoc />
    public Dictionary<string, double>? GetTextWeights(ScoringProfile? profile)
    {
        if (profile?.Text?.Weights == null || profile.Text.Weights.Count == 0)
        {
            return null;
        }

        return profile.Text.Weights;
    }

    /// <inheritdoc />
    public Dictionary<string, string> ParseScoringParameters(List<string>? scoringParameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scoringParameters == null || scoringParameters.Count == 0)
        {
            return result;
        }

        foreach (var param in scoringParameters)
        {
            if (string.IsNullOrWhiteSpace(param))
            {
                continue;
            }

            // Azure format: "parameterName-value"
            // For geo points with negative coordinates: "parameterName--47.6,122.3"
            // The first dash is always the separator.
            var dashIndex = param.IndexOf('-');
            if (dashIndex <= 0)
            {
                _logger.LogWarning("Invalid scoring parameter format: '{Parameter}'. Expected 'name-value'.", param);
                continue;
            }

            var name = param[..dashIndex];
            var value = param[(dashIndex + 1)..];

            result[name] = value;
        }

        return result;
    }

    /// <inheritdoc />
    public double CalculateDocumentBoost(
        ScoringProfile profile,
        Dictionary<string, object?> documentFields,
        Dictionary<string, string> scoringParameters)
    {
        if (profile.Functions == null || profile.Functions.Count == 0)
        {
            return 1.0;
        }

        var boosts = new List<double>();

        foreach (var function in profile.Functions)
        {
            var fieldValue = documentFields.GetValueOrDefault(function.FieldName);

            double? rawNormalized = function.Type.ToLowerInvariant() switch
            {
                "freshness" => EvaluateFreshnessFunction(function, fieldValue),
                "magnitude" => EvaluateMagnitudeFunction(function, fieldValue),
                "distance" => EvaluateDistanceFunction(function, fieldValue, scoringParameters),
                "tag" => EvaluateTagFunction(function, fieldValue, scoringParameters),
                _ => null
            };

            if (rawNormalized == null)
            {
                _logger.LogDebug(
                    "Scoring function '{Type}' on field '{Field}' returned null (field missing or unsupported type), skipping",
                    function.Type, function.FieldName);

                // For firstMatching aggregation, skip functions that don't match
                if (string.Equals(profile.FunctionAggregation, "firstMatching", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // For other aggregation modes, a missing field contributes 0
                boosts.Add(0);
                continue;
            }

            // Apply interpolation to the normalized value
            var interpolated = ApplyInterpolation(rawNormalized.Value, function.Interpolation);

            // Scale by the function's boost factor
            var functionBoost = interpolated * function.Boost;
            boosts.Add(functionBoost);

            _logger.LogDebug(
                "Scoring function '{Type}' on field '{Field}': raw={Raw:F4}, interpolated={Interpolated:F4}, boost={Boost}, result={Result:F4}",
                function.Type, function.FieldName, rawNormalized.Value, interpolated, function.Boost, functionBoost);
        }

        if (boosts.Count == 0)
        {
            return 1.0;
        }

        var aggregated = AggregateBoosts(boosts, profile.FunctionAggregation);

        // The document boost is 1 + aggregated function boosts (Azure behavior: boosts are additive to base score of 1)
        var documentBoost = 1.0 + aggregated;

        _logger.LogDebug(
            "Document boost from scoring profile '{Profile}': aggregation={Aggregation}, boosts=[{Boosts}], result={Result:F4}",
            profile.Name, profile.FunctionAggregation ?? "sum",
            string.Join(", ", boosts.Select(b => b.ToString("F4"))), documentBoost);

        return documentBoost;
    }

    /// <summary>
    /// Evaluates a freshness scoring function.
    /// Returns a normalized value in [0, 1] where 1 = now, 0 = boostingDuration away or further.
    /// </summary>
    internal double? EvaluateFreshnessFunction(ScoringFunction function, object? fieldValue)
    {
        if (function.Freshness == null)
        {
            return null;
        }

        var dateValue = ParseDateTimeOffset(fieldValue);
        if (dateValue == null)
        {
            return null;
        }

        var boostingDuration = ParseIsoDuration(function.Freshness.BoostingDuration);
        if (boostingDuration == null || boostingDuration.Value.TotalSeconds <= 0)
        {
            _logger.LogWarning("Invalid boostingDuration '{Duration}' in freshness function", function.Freshness.BoostingDuration);
            return null;
        }

        var timeDelta = Math.Abs((DateTime.UtcNow - dateValue.Value.UtcDateTime).TotalSeconds);
        var durationSeconds = boostingDuration.Value.TotalSeconds;

        // Normalized: 1.0 when timeDelta=0, 0.0 when timeDelta >= durationSeconds
        var normalized = Math.Max(0, 1.0 - (timeDelta / durationSeconds));
        return normalized;
    }

    /// <summary>
    /// Evaluates a magnitude scoring function.
    /// Returns a normalized value in [0, 1] based on where the field value falls within the range.
    /// </summary>
    internal double? EvaluateMagnitudeFunction(ScoringFunction function, object? fieldValue)
    {
        if (function.Magnitude == null)
        {
            return null;
        }

        var numericValue = ParseNumericValue(fieldValue);
        if (numericValue == null)
        {
            return null;
        }

        var rangeStart = function.Magnitude.BoostingRangeStart;
        var rangeEnd = function.Magnitude.BoostingRangeEnd;

        if (Math.Abs(rangeEnd - rangeStart) < double.Epsilon)
        {
            // Degenerate range — if value equals the range, full boost; otherwise none
            return Math.Abs(numericValue.Value - rangeStart) < double.Epsilon ? 1.0 : 0.0;
        }

        // Normalize the value within the range
        var normalized = (numericValue.Value - rangeStart) / (rangeEnd - rangeStart);

        if (normalized < 0 || normalized > 1)
        {
            if (function.Magnitude.ConstantBoostBeyondRange == true)
            {
                // Clamp to nearest boundary
                normalized = Math.Clamp(normalized, 0, 1);
            }
            else
            {
                // No boost outside range
                return 0.0;
            }
        }

        return normalized;
    }

    /// <summary>
    /// Evaluates a distance scoring function.
    /// Returns a normalized value in [0, 1] where 1 = at reference point, 0 = at boostingDistance or further.
    /// </summary>
    internal double? EvaluateDistanceFunction(ScoringFunction function, object? fieldValue, Dictionary<string, string> scoringParameters)
    {
        if (function.Distance == null)
        {
            return null;
        }

        var paramName = function.Distance.ReferencePointParameter;
        if (!scoringParameters.TryGetValue(paramName, out var paramValue))
        {
            _logger.LogDebug("Distance scoring parameter '{ParamName}' not found, skipping", paramName);
            return null;
        }

        // Parse reference point (lat,lon)
        var referencePoint = ParseGeoPoint(paramValue);
        if (referencePoint == null)
        {
            _logger.LogWarning("Failed to parse geo reference point '{Value}'", paramValue);
            return null;
        }

        // Parse document's geo field
        var documentPoint = ParseGeoPoint(fieldValue);
        if (documentPoint == null)
        {
            return null;
        }

        var distanceKm = HaversineDistance(referencePoint.Value.Lat, referencePoint.Value.Lon,
            documentPoint.Value.Lat, documentPoint.Value.Lon);

        var boostingDistance = function.Distance.BoostingDistance;
        if (boostingDistance <= 0)
        {
            return distanceKm < 0.001 ? 1.0 : 0.0;
        }

        var normalized = Math.Max(0, 1.0 - (distanceKm / boostingDistance));
        return normalized;
    }

    /// <summary>
    /// Evaluates a tag scoring function.
    /// Returns 1.0 if any tag matches, 0.0 otherwise (binary).
    /// </summary>
    internal double? EvaluateTagFunction(ScoringFunction function, object? fieldValue, Dictionary<string, string> scoringParameters)
    {
        if (function.Tag == null)
        {
            return null;
        }

        var paramName = function.Tag.TagsParameter;
        if (!scoringParameters.TryGetValue(paramName, out var paramValue))
        {
            _logger.LogDebug("Tag scoring parameter '{ParamName}' not found, skipping", paramName);
            return null;
        }

        // Parse the tag parameter values (comma-separated)
        var tagValues = paramValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tagValues.Count == 0)
        {
            return 0.0;
        }

        // Get document field values
        var documentTags = GetDocumentTagValues(fieldValue);

        // Binary match: any overlap = full boost
        var hasMatch = documentTags.Any(t => tagValues.Contains(t));
        return hasMatch ? 1.0 : 0.0;
    }

    /// <summary>
    /// Applies an interpolation function to a normalized value in [0, 1].
    /// </summary>
    internal static double ApplyInterpolation(double normalizedValue, string? interpolation)
    {
        // Clamp input
        normalizedValue = Math.Clamp(normalizedValue, 0, 1);

        return (interpolation?.ToLowerInvariant()) switch
        {
            "constant" => normalizedValue > 0 ? 1.0 : 0.0,
            "quadratic" => normalizedValue * normalizedValue,
            "logarithmic" => normalizedValue == 0
                ? 0.0
                : 1.0 - Math.Log(1.0 + (1.0 - normalizedValue) * (Math.E - 1.0)),
            // "linear" is the default
            _ => normalizedValue,
        };
    }

    /// <summary>
    /// Aggregates multiple function boost values using the specified method.
    /// </summary>
    internal static double AggregateBoosts(List<double> boosts, string? aggregation)
    {
        if (boosts.Count == 0)
        {
            return 0.0;
        }

        return (aggregation?.ToLowerInvariant()) switch
        {
            "average" => boosts.Average(),
            "minimum" => boosts.Min(),
            "maximum" => boosts.Max(),
            "firstmatching" => boosts.FirstOrDefault(b => b > 0),
            // "sum" is the default
            _ => boosts.Sum(),
        };
    }

    #region Parsing Helpers

    /// <summary>
    /// Parses a DateTimeOffset from various representations (string, DateTime, DateTimeOffset, JsonElement).
    /// </summary>
    internal static DateTimeOffset? ParseDateTimeOffset(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        if (value is DateTime dt)
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(je.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        var str = value.ToString();
        if (str != null && DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Parses a numeric value from various representations.
    /// </summary>
    internal static double? ParseNumericValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is decimal dec) return (double)dec;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var jd))
            {
                return jd;
            }
            return null;
        }

        var str = value.ToString();
        if (str != null && double.TryParse(str, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Parses an ISO 8601 duration string (e.g., "P365D", "P30D", "PT12H", "P1Y6M").
    /// Also handles legacy format without the 'P' prefix (e.g., "365D").
    /// </summary>
    internal static TimeSpan? ParseIsoDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        try
        {
            // XmlConvert handles ISO 8601 durations
            return XmlConvert.ToTimeSpan(duration);
        }
        catch
        {
            // Fallback: try prepending 'P' for legacy formats like "365D", "30D", "12H"
            if (!duration.StartsWith('P') && !duration.StartsWith('p'))
            {
                try
                {
                    return XmlConvert.ToTimeSpan("P" + duration);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Parses a geographic point from various representations.
    /// Supports: "lat,lon", "-lat,lon", GeoJSON, POINT(lon lat) WKT.
    /// </summary>
    internal static (double Lat, double Lon)? ParseGeoPoint(object? value)
    {
        if (value == null)
        {
            return null;
        }

        string? str = null;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object)
            {
                // GeoJSON format: { "type": "Point", "coordinates": [lon, lat] }
                if (je.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Array)
                {
                    var arr = coords.EnumerateArray().ToList();
                    if (arr.Count >= 2)
                    {
                        var lon = arr[0].GetDouble();
                        var lat = arr[1].GetDouble();
                        return (lat, lon);
                    }
                }
                return null;
            }
            else if (je.ValueKind == JsonValueKind.String)
            {
                str = je.GetString();
            }
            else
            {
                return null;
            }
        }
        else
        {
            str = value.ToString();
        }

        if (string.IsNullOrWhiteSpace(str))
        {
            return null;
        }

        // Try "lat,lon" format (used in scoring parameters)
        var parts = str.Split(',');
        if (parts.Length == 2
            && double.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var lat1)
            && double.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var lon1))
        {
            return (lat1, lon1);
        }

        // Try WKT POINT(lon lat)
        if (str.StartsWith("POINT(", StringComparison.OrdinalIgnoreCase) && str.EndsWith(")"))
        {
            var inner = str[6..^1].Trim();
            var wktParts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (wktParts.Length == 2
                && double.TryParse(wktParts[0], CultureInfo.InvariantCulture, out var lon2)
                && double.TryParse(wktParts[1], CultureInfo.InvariantCulture, out var lat2))
            {
                return (lat2, lon2);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts tag values from a document field (string or collection of strings).
    /// </summary>
    internal static List<string> GetDocumentTagValues(object? fieldValue)
    {
        if (fieldValue == null)
        {
            return new List<string>();
        }

        if (fieldValue is string s)
        {
            return new List<string> { s };
        }

        if (fieldValue is List<string> list)
        {
            return list;
        }

        if (fieldValue is IEnumerable<object> enumerable)
        {
            return enumerable
                .Select(o => o?.ToString())
                .Where(o => o != null)
                .Cast<string>()
                .ToList();
        }

        if (fieldValue is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
            }
            else if (je.ValueKind == JsonValueKind.String)
            {
                return new List<string> { je.GetString()! };
            }
        }

        var str = fieldValue.ToString();
        return str != null ? new List<string> { str } : new List<string>();
    }

    /// <summary>
    /// Calculates the Haversine distance between two points in kilometers.
    /// </summary>
    internal static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth radius in km

        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    #endregion
}
