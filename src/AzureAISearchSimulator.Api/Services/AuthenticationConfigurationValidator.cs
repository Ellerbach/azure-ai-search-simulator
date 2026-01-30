using AzureAISearchSimulator.Core.Configuration;
using Microsoft.Extensions.Options;

namespace AzureAISearchSimulator.Api.Services;

/// <summary>
/// Service that validates authentication configuration at startup.
/// </summary>
public class AuthenticationConfigurationValidator : IHostedService
{
    private readonly ILogger<AuthenticationConfigurationValidator> _logger;
    private readonly IOptionsMonitor<AuthenticationSettings> _authSettings;
    private readonly IOptionsMonitor<OutboundAuthenticationSettings> _outboundSettings;
    private readonly IOptionsMonitor<SimulatorSettings> _simulatorSettings;
    private readonly IHostEnvironment _environment;

    public AuthenticationConfigurationValidator(
        ILogger<AuthenticationConfigurationValidator> logger,
        IOptionsMonitor<AuthenticationSettings> authSettings,
        IOptionsMonitor<OutboundAuthenticationSettings> outboundSettings,
        IOptionsMonitor<SimulatorSettings> simulatorSettings,
        IHostEnvironment environment)
    {
        _logger = logger;
        _authSettings = authSettings;
        _outboundSettings = outboundSettings;
        _simulatorSettings = simulatorSettings;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var authSettings = _authSettings.CurrentValue;
        var outboundSettings = _outboundSettings.CurrentValue;
        var simulatorSettings = _simulatorSettings.CurrentValue;

        _logger.LogInformation("Validating authentication configuration...");

        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate enabled modes
        ValidateEnabledModes(authSettings, errors, warnings);

        // Validate API key settings
        ValidateApiKeySettings(authSettings, simulatorSettings, errors, warnings);

        // Validate simulated token settings
        ValidateSimulatedSettings(authSettings, errors, warnings);

        // Validate Entra ID settings
        ValidateEntraIdSettings(authSettings, errors, warnings);

        // Validate outbound settings
        ValidateOutboundSettings(outboundSettings, errors, warnings);

        // Environment-specific warnings
        ValidateEnvironmentSettings(authSettings, errors, warnings);

        // Log validation results
        foreach (var warning in warnings)
        {
            _logger.LogWarning("Authentication config warning: {Warning}", warning);
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _logger.LogError("Authentication config error: {Error}", error);
            }
            
            // In production, we might want to throw, but for development we just warn
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException(
                    $"Authentication configuration has {errors.Count} error(s). " +
                    string.Join(" ", errors));
            }
        }
        else
        {
            _logger.LogInformation(
                "Authentication configuration validated. EnabledModes: [{Modes}]", 
                string.Join(", ", authSettings.EnabledModes));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateEnabledModes(AuthenticationSettings settings, List<string> errors, List<string> warnings)
    {
        if (settings.EnabledModes == null || settings.EnabledModes.Count == 0)
        {
            errors.Add("No authentication modes are enabled. Set Authentication:EnabledModes in configuration.");
            return;
        }

        var validModes = new[] { "ApiKey", "Simulated", "EntraId" };
        foreach (var mode in settings.EnabledModes)
        {
            if (!validModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"Unknown authentication mode '{mode}'. Valid modes: {string.Join(", ", validModes)}");
            }
        }
    }

    private void ValidateApiKeySettings(
        AuthenticationSettings authSettings, 
        SimulatorSettings simulatorSettings, 
        List<string> errors, 
        List<string> warnings)
    {
        if (!authSettings.EnabledModes.Contains("ApiKey", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var adminKey = authSettings.ApiKey?.AdminApiKey ?? simulatorSettings.AdminApiKey;
        var queryKey = authSettings.ApiKey?.QueryApiKey ?? simulatorSettings.QueryApiKey;

        if (string.IsNullOrEmpty(adminKey))
        {
            warnings.Add("Admin API key is not configured. Set Simulator:AdminApiKey or Authentication:ApiKey:AdminApiKey.");
        }
        else if (adminKey == "admin-key-12345")
        {
            warnings.Add("Using default admin API key. Consider changing it for security.");
        }

        if (string.IsNullOrEmpty(queryKey))
        {
            warnings.Add("Query API key is not configured. Set Simulator:QueryApiKey or Authentication:ApiKey:QueryApiKey.");
        }
        else if (queryKey == "query-key-67890")
        {
            warnings.Add("Using default query API key. Consider changing it for security.");
        }
    }

    private void ValidateSimulatedSettings(AuthenticationSettings settings, List<string> errors, List<string> warnings)
    {
        if (!settings.EnabledModes.Contains("Simulated", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var simulated = settings.Simulated;
        if (simulated == null || !simulated.Enabled)
        {
            warnings.Add("Simulated mode is in EnabledModes but Simulated:Enabled is false.");
            return;
        }

        if (string.IsNullOrEmpty(simulated.SigningKey))
        {
            errors.Add("Simulated:SigningKey is required for simulated token authentication.");
        }
        else if (simulated.SigningKey.Length < 32)
        {
            errors.Add("Simulated:SigningKey must be at least 32 characters for HMAC-SHA256.");
        }
        else if (simulated.SigningKey == "SimulatorSigningKey-Change-This-In-Production-12345678")
        {
            warnings.Add("Using default simulated signing key. Change it for production.");
        }

        if (string.IsNullOrEmpty(simulated.Issuer))
        {
            warnings.Add("Simulated:Issuer is not set. Using default issuer.");
        }

        if (string.IsNullOrEmpty(simulated.Audience))
        {
            warnings.Add("Simulated:Audience is not set. Using default audience.");
        }
    }

    private void ValidateEntraIdSettings(AuthenticationSettings settings, List<string> errors, List<string> warnings)
    {
        if (!settings.EnabledModes.Contains("EntraId", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var entraId = settings.EntraId;
        if (entraId == null)
        {
            warnings.Add("EntraId mode is in EnabledModes but EntraId settings are not configured.");
            return;
        }

        if (string.IsNullOrEmpty(entraId.TenantId))
        {
            errors.Add("EntraId:TenantId is required for Entra ID authentication.");
        }
        else if (!Guid.TryParse(entraId.TenantId, out _) && 
                 entraId.TenantId != "common" && 
                 entraId.TenantId != "organizations" &&
                 entraId.TenantId != "consumers")
        {
            warnings.Add("EntraId:TenantId does not appear to be a valid GUID or special value.");
        }

        if (string.IsNullOrEmpty(entraId.Audience) || entraId.Audience == "https://search.azure.com")
        {
            _logger.LogDebug("Using Azure AI Search audience: {Audience}", entraId.Audience ?? "https://search.azure.com");
        }

        if (!entraId.RequireHttpsMetadata && _environment.IsProduction())
        {
            warnings.Add("EntraId:RequireHttpsMetadata is false in production. This is insecure.");
        }
    }

    private void ValidateOutboundSettings(OutboundAuthenticationSettings settings, List<string> errors, List<string> warnings)
    {
        if (settings == null)
        {
            return;
        }

        var credType = settings.DefaultCredentialType;
        
        if (credType == "ServicePrincipal")
        {
            var sp = settings.ServicePrincipal;
            if (sp == null || string.IsNullOrEmpty(sp.TenantId) || string.IsNullOrEmpty(sp.ClientId))
            {
                errors.Add("ServicePrincipal credential type requires TenantId and ClientId.");
            }
            else if (string.IsNullOrEmpty(sp.ClientSecret) && 
                     string.IsNullOrEmpty(sp.CertificatePath) && 
                     string.IsNullOrEmpty(sp.CertificateThumbprint))
            {
                warnings.Add("ServicePrincipal has no ClientSecret or Certificate configured. Ensure proper authentication.");
            }
        }
        else if (credType == "ManagedIdentity")
        {
            var mi = settings.ManagedIdentity;
            if (mi != null && !mi.Enabled)
            {
                warnings.Add("ManagedIdentity credential type is selected but ManagedIdentity:Enabled is false.");
            }
        }
    }

    private void ValidateEnvironmentSettings(AuthenticationSettings settings, List<string> errors, List<string> warnings)
    {
        // Warn about simulated mode in production
        if (_environment.IsProduction() && 
            settings.EnabledModes.Contains("Simulated", StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add("Simulated authentication mode is enabled in production. " +
                         "This should only be used for testing.");
        }

        // Suggest enabling at least one auth mode for development
        if (_environment.IsDevelopment() && 
            !settings.EnabledModes.Contains("Simulated", StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Tip: Enable 'Simulated' mode for local development with JWT tokens.");
        }
    }
}
