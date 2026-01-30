using Microsoft.AspNetCore.Http;

namespace AzureAISearchSimulator.Core.Services.Authentication;

/// <summary>
/// Interface for authentication handlers that validate different credential types.
/// Each handler is responsible for one authentication mode (API Key, Entra ID, Simulated).
/// </summary>
public interface IAuthenticationHandler
{
    /// <summary>
    /// The name of the authentication mode this handler supports.
    /// Examples: "ApiKey", "EntraId", "Simulated"
    /// </summary>
    string AuthenticationMode { get; }

    /// <summary>
    /// The priority of this handler. Lower values are checked first.
    /// API Key should have highest priority (lowest number) to match Azure behavior.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Determines whether this handler can process the authentication for the given request.
    /// This should be a fast check that looks for the presence of relevant credentials.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>True if this handler should attempt authentication.</returns>
    bool CanHandle(HttpContext context);

    /// <summary>
    /// Attempts to authenticate the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<AuthenticationResult> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);
}
