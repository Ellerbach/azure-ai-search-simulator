using AzureAISearchSimulator.Core.Configuration;
using AzureAISearchSimulator.Core.Services.Credentials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Controllers;

/// <summary>
/// Controller for diagnostics and debugging authentication configuration.
/// </summary>
[ApiController]
[Route("admin/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly ICredentialFactory _credentialFactory;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly IOptionsMonitor<OutboundAuthenticationSettings> _outboundSettings;

    public DiagnosticsController(
        ILogger<DiagnosticsController> logger,
        ICredentialFactory credentialFactory,
        IOptionsMonitor<AuthenticationSettings> authSettings,
        IOptionsMonitor<OutboundAuthenticationSettings> outboundSettings)
    {
        _logger = logger;
        _credentialFactory = credentialFactory;
        _authSettings = authSettings;
        _outboundSettings = outboundSettings;
    }

    /// <summary>
    /// Gets information about the current authentication configuration.
    /// </summary>
    [HttpGet("auth")]
    public ActionResult<AuthDiagnosticsResponse> GetAuthDiagnostics()
    {
        var authSettings = _authSettings.CurrentValue;
        var outboundSettings = _outboundSettings.CurrentValue;
        var credentialInfo = _credentialFactory.GetCredentialInfo();

        return Ok(new AuthDiagnosticsResponse
        {
            Inbound = new InboundAuthInfo
            {
                EnabledModes = authSettings.EnabledModes,
                DefaultMode = authSettings.DefaultMode,
                ApiKeyTakesPrecedence = authSettings.ApiKeyTakesPrecedence,
                EntraIdConfigured = !string.IsNullOrEmpty(authSettings.EntraId.TenantId),
                SimulatedEnabled = authSettings.Simulated.Enabled
            },
            Outbound = new OutboundAuthInfo
            {
                DefaultCredentialType = outboundSettings.DefaultCredentialType,
                TokenCacheEnabled = outboundSettings.TokenCache.Enabled,
                ManagedIdentityEnabled = outboundSettings.ManagedIdentity.Enabled,
                ServicePrincipalConfigured = !string.IsNullOrEmpty(outboundSettings.ServicePrincipal.ClientId),
                CredentialInfo = credentialInfo
            }
        });
    }

    /// <summary>
    /// Tests the outbound credential by attempting to acquire a token.
    /// </summary>
    [HttpGet("credentials/test")]
    public async Task<ActionResult<CredentialTestResponse>> TestCredentials(
        [FromQuery] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing outbound credentials");

        var result = await _credentialFactory.TestCredentialAsync(cancellationToken);

        return Ok(new CredentialTestResponse
        {
            Success = result.Success,
            CredentialType = result.CredentialType,
            TestedScope = result.TestedScope,
            TokenExpiration = result.TokenExpiration,
            ElapsedMilliseconds = result.ElapsedTime?.TotalMilliseconds,
            Error = result.Error
        });
    }

    /// <summary>
    /// Acquires a token for a specific scope (for testing purposes).
    /// </summary>
    [HttpPost("credentials/token")]
    public async Task<ActionResult<TokenAcquisitionResponse>> AcquireToken(
        [FromBody] TokenAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Scope))
        {
            return BadRequest(new { error = "Scope is required" });
        }

        _logger.LogInformation("Acquiring token for scope: {Scope}", request.Scope);

        try
        {
            var token = await _credentialFactory.GetTokenAsync(request.Scope, cancellationToken);

            return Ok(new TokenAcquisitionResponse
            {
                Success = true,
                Scope = request.Scope,
                ExpiresOn = token.ExpiresOn,
                // Don't return the actual token for security reasons
                TokenPreview = $"{token.Token[..10]}...{token.Token[^5..]}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire token for scope: {Scope}", request.Scope);

            return Ok(new TokenAcquisitionResponse
            {
                Success = false,
                Scope = request.Scope,
                Error = ex.Message
            });
        }
    }
}

#region Request/Response Models

public class AuthDiagnosticsResponse
{
    public InboundAuthInfo Inbound { get; set; } = new();
    public OutboundAuthInfo Outbound { get; set; } = new();
}

public class InboundAuthInfo
{
    public List<string> EnabledModes { get; set; } = new();
    public string DefaultMode { get; set; } = "";
    public bool ApiKeyTakesPrecedence { get; set; }
    public bool EntraIdConfigured { get; set; }
    public bool SimulatedEnabled { get; set; }
}

public class OutboundAuthInfo
{
    public string DefaultCredentialType { get; set; } = "";
    public bool TokenCacheEnabled { get; set; }
    public bool ManagedIdentityEnabled { get; set; }
    public bool ServicePrincipalConfigured { get; set; }
    public CredentialInfo? CredentialInfo { get; set; }
}

public class CredentialTestResponse
{
    public bool Success { get; set; }
    public string? CredentialType { get; set; }
    public string? TestedScope { get; set; }
    public DateTimeOffset? TokenExpiration { get; set; }
    public double? ElapsedMilliseconds { get; set; }
    public string? Error { get; set; }
}

public class TokenAcquisitionRequest
{
    public string Scope { get; set; } = "";
}

public class TokenAcquisitionResponse
{
    public bool Success { get; set; }
    public string Scope { get; set; } = "";
    public DateTimeOffset? ExpiresOn { get; set; }
    public string? TokenPreview { get; set; }
    public string? Error { get; set; }
}

#endregion
