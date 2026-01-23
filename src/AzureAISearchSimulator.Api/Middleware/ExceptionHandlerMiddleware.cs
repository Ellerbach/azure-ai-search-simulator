using AzureAISearchSimulator.Core.Models;
using System.Net;
using System.Text.Json;

namespace AzureAISearchSimulator.Api.Middleware;

/// <summary>
/// Global exception handler middleware that returns OData-compliant error responses.
/// </summary>
public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlerMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        var (statusCode, error) = MapExceptionToODataError(exception);

        // Add inner error details in development mode
        if (_environment.IsDevelopment())
        {
            error.Error.InnerError = CreateInnerError(exception);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsJsonAsync(error, jsonOptions);
    }

    private static (HttpStatusCode, ODataError) MapExceptionToODataError(Exception exception)
    {
        return exception switch
        {
            // ArgumentNullException must come before ArgumentException (it's a subclass)
            ArgumentNullException nullEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.InvalidArgument, $"Required parameter '{nullEx.ParamName}' is missing", nullEx.ParamName)
            ),

            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.InvalidArgument, argEx.Message, argEx.ParamName)
            ),

            InvalidOperationException invalidOpEx when invalidOpEx.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) => (
                HttpStatusCode.NotFound,
                ODataError.Create(ErrorCodes.ResourceNotFound, invalidOpEx.Message)
            ),

            InvalidOperationException invalidOpEx when invalidOpEx.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) => (
                HttpStatusCode.Conflict,
                ODataError.Create(ErrorCodes.ResourceAlreadyExists, invalidOpEx.Message)
            ),

            InvalidOperationException invalidOpEx when invalidOpEx.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.OperationNotAllowed, invalidOpEx.Message)
            ),

            InvalidOperationException invalidOpEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.InvalidArgument, invalidOpEx.Message)
            ),

            KeyNotFoundException keyNotFoundEx => (
                HttpStatusCode.NotFound,
                ODataError.Create(ErrorCodes.ResourceNotFound, keyNotFoundEx.Message)
            ),

            UnauthorizedAccessException => (
                HttpStatusCode.Forbidden,
                ODataError.Create(ErrorCodes.Forbidden, "Access denied")
            ),

            NotSupportedException notSupportedEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.OperationNotAllowed, notSupportedEx.Message)
            ),

            NotImplementedException => (
                HttpStatusCode.NotImplemented,
                ODataError.Create(ErrorCodes.OperationNotAllowed, "This operation is not yet implemented in the simulator")
            ),

            TimeoutException => (
                HttpStatusCode.GatewayTimeout,
                ODataError.Create(ErrorCodes.ServiceUnavailable, "The operation timed out")
            ),

            OperationCanceledException => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.OperationNotAllowed, "The operation was cancelled")
            ),

            JsonException jsonEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.InvalidArgument, $"Invalid JSON: {jsonEx.Message}")
            ),

            FormatException formatEx => (
                HttpStatusCode.BadRequest,
                ODataError.Create(ErrorCodes.InvalidArgument, $"Invalid format: {formatEx.Message}")
            ),

            _ => (
                HttpStatusCode.InternalServerError,
                ODataError.Create(ErrorCodes.InternalServerError, "An internal error occurred")
            )
        };
    }

    private static ODataInnerError CreateInnerError(Exception exception)
    {
        var innerError = new ODataInnerError
        {
            Type = exception.GetType().FullName,
            StackTrace = exception.StackTrace
        };

        if (exception.InnerException != null)
        {
            innerError.InnerErrorDetails = CreateInnerError(exception.InnerException);
        }

        return innerError;
    }
}

/// <summary>
/// Extension methods for adding the exception handler middleware.
/// </summary>
public static class ExceptionHandlerMiddlewareExtensions
{
    /// <summary>
    /// Adds the global exception handler middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlerMiddleware>();
    }
}
