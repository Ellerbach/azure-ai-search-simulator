using System.Text.Json.Serialization;

namespace AzureAISearchSimulator.Core.Models;

/// <summary>
/// OData-style collection response wrapper.
/// </summary>
/// <typeparam name="T">Type of items in the collection.</typeparam>
public class ODataResponse<T>
{
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    [JsonPropertyName("@odata.count")]
    public int? ODataCount { get; set; }

    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = new();
}

/// <summary>
/// OData-compliant error response format for Azure AI Search API compatibility.
/// </summary>
public class ODataError
{
    /// <summary>
    /// The error details.
    /// </summary>
    [JsonPropertyName("error")]
    public ODataErrorBody Error { get; set; } = new();

    /// <summary>
    /// Creates a new OData error response.
    /// </summary>
    public static ODataError Create(string code, string message, string? target = null)
    {
        return new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = code,
                Message = message,
                Target = target
            }
        };
    }

    /// <summary>
    /// Creates a validation error with details.
    /// </summary>
    public static ODataError ValidationError(string message, IEnumerable<ODataErrorDetail> details)
    {
        return new ODataError
        {
            Error = new ODataErrorBody
            {
                Code = ErrorCodes.ValidationError,
                Message = message,
                Details = details.ToList()
            }
        };
    }
}

/// <summary>
/// Error body in OData format.
/// </summary>
public class ODataErrorBody
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ODataErrorDetail>? Details { get; set; }

    /// <summary>
    /// Inner error for debugging (only in development).
    /// </summary>
    [JsonPropertyName("innererror")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ODataInnerError? InnerError { get; set; }
}

/// <summary>
/// Individual error detail for validation errors.
/// </summary>
public class ODataErrorDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; set; }
}

/// <summary>
/// Inner error information for debugging.
/// </summary>
public class ODataInnerError
{
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("stacktrace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StackTrace { get; set; }

    [JsonPropertyName("innererror")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ODataInnerError? InnerErrorDetails { get; set; }
}

/// <summary>
/// Common error codes used in Azure AI Search.
/// </summary>
public static class ErrorCodes
{
    public const string InvalidArgument = "InvalidArgument";
    public const string InvalidApiKey = "InvalidApiKey";
    public const string Forbidden = "Forbidden";
    public const string ResourceNotFound = "ResourceNotFound";
    public const string ResourceAlreadyExists = "ResourceAlreadyExists";
    public const string OperationNotAllowed = "OperationNotAllowed";
    public const string QuotaExceeded = "QuotaExceeded";
    public const string ServiceUnavailable = "ServiceUnavailable";
    public const string InternalServerError = "InternalServerError";
    public const string ValidationError = "ValidationError";
    public const string IndexNotFound = "IndexNotFound";
    public const string DocumentNotFound = "DocumentNotFound";
    public const string InvalidDocument = "InvalidDocument";
    public const string MissingKeyField = "MissingKeyField";
    public const string InvalidSearchRequest = "InvalidSearchRequest";
    public const string InvalidFilter = "InvalidFilter";
}
