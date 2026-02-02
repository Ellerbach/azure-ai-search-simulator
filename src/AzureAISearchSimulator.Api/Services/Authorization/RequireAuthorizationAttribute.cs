using AzureAISearchSimulator.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AzureAISearchSimulator.Api.Services.Authorization;

/// <summary>
/// Action filter attribute that enforces authorization based on SearchOperation.
/// Apply to controller actions to require specific permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireAuthorizationAttribute : Attribute, IAsyncActionFilter
{
    private readonly SearchOperation _operation;

    public RequireAuthorizationAttribute(SearchOperation operation)
    {
        _operation = operation;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = authService.Authorize(context.HttpContext, _operation);

        if (!result.IsAuthorized)
        {
            var statusCode = result.ErrorCode == "Unauthorized" 
                ? StatusCodes.Status401Unauthorized 
                : StatusCodes.Status403Forbidden;

            var error = ODataError.Create(
                result.ErrorCode ?? "Forbidden",
                result.ErrorMessage ?? $"You do not have permission to perform this operation.");

            context.Result = new ObjectResult(error)
            {
                StatusCode = statusCode
            };
            return;
        }

        await next();
    }
}

/// <summary>
/// Extension methods for authorization in controllers.
/// </summary>
public static class ControllerAuthorizationExtensions
{
    /// <summary>
    /// Checks authorization and returns an error result if not authorized.
    /// Returns null if authorized.
    /// </summary>
    public static IActionResult? CheckAuthorization(
        this ControllerBase controller,
        IAuthorizationService authService,
        SearchOperation operation)
    {
        var result = authService.Authorize(controller.HttpContext, operation);
        
        if (!result.IsAuthorized)
        {
            var statusCode = result.ErrorCode == "Unauthorized"
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;

            var error = ODataError.Create(
                result.ErrorCode ?? "Forbidden",
                result.ErrorMessage ?? $"You do not have permission to perform this operation.");

            return new ObjectResult(error) { StatusCode = statusCode };
        }

        return null;
    }
}
