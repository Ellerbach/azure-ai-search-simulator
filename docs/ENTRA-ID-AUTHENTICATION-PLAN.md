# Entra ID Authentication Implementation Plan

## Overview

This document outlines the plan to implement comprehensive Entra ID (Azure AD) authentication for the Azure AI Search Simulator. The implementation covers both:

1. **Inbound Authentication** - How clients authenticate TO the simulator
2. **Outbound Authentication** - How the simulator authenticates to external Azure services

The goal is to provide a realistic authentication experience that mirrors Azure AI Search while maintaining the ability to run locally without Azure dependencies.

---

## How Real Azure AI Search Handles Entra ID Authentication

Before implementing, it's important to understand the actual Azure AI Search authentication protocol:

### Token Passing Protocol

**Header Format:** Tokens are passed via the standard HTTP `Authorization` header:

```http
Authorization: Bearer <access-token>
```

**Example Request:**

```http
POST https://my-search.search.windows.net/indexes/hotels/docs/search?api-version=2025-09-01 HTTP/1.1
Content-Type: application/json
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIs...

{
    "queryType": "simple",
    "search": "pool",
    "select": "HotelName,Description"
}
```

### Token Acquisition

To obtain a token for Azure AI Search, clients request tokens with the **audience** `https://search.azure.com/.default`:

**Azure CLI:**

```bash
az account get-access-token --scope https://search.azure.com/.default
```

**PowerShell:**

```powershell
Get-AzAccessToken -ResourceUrl https://search.azure.com
```

**Azure SDK (C#):**

```csharp
DefaultAzureCredential credential = new();
SearchClient searchClient = new(new Uri(endpoint), indexName, credential);
// SDK handles token acquisition and Authorization header automatically
```

### Sovereign Cloud Audiences

| Cloud | Audience |
| ----- | -------- |
| Azure Public | `https://search.azure.com` |
| Azure Government | `https://search.azure.us` |
| Azure China (21Vianet) | `https://search.azure.cn` |
| Azure Germany | `https://search.microsoftazure.de` |

### Built-in Azure Roles for Search

#### Data Plane Roles (Search-Specific)

| Role | Role ID | Permissions |
| ---- | ------- | ----------- |
| **Search Service Contributor** | `7ca78c08-252a-4471-8644-bb5ff32d4ba0` | Create/manage indexes, indexers, skillsets, data sources, synonym maps |
| **Search Index Data Contributor** | `8ebe5a00-799e-43f5-93ac-243d3dce84a7` | Load/index documents, run indexing jobs |
| **Search Index Data Reader** | `1407120a-92aa-4202-b7e9-c0e197c71c8f` | Query indexes (search, autocomplete, suggest) |

#### Control Plane Roles (Azure-Wide)

| Role | Role ID | Permissions |
| ---- | ------- | ----------- |
| **Owner** | `8e3af657-a8ff-443c-a75c-2fe8c4bcb635` | Full control plane + Search Service Contributor data access (no query/index) |
| **Contributor** | `b24988ac-6180-42a0-ab88-20f7382dd24c` | Same as Owner minus role assignment |
| **Reader** | `acdd72a7-3385-48ef-bd42-f606fba81ae7` | Read service info, metrics, object definitions (no content access) |

### Permission Matrix

| Operation | Index Data Reader | Index Data Contributor | Service Contributor | Owner/Contributor |
| --------- | :---------------: | :--------------------: | :-----------------: | :---------------: |
| **Query index (search/suggest/autocomplete)** | ✅ | ✅ | ❌ | ❌ |
| **Upload/merge/delete documents** | ❌ | ✅ | ❌ | ❌ |
| **Create/update/delete indexes** | ❌ | ❌ | ✅ | ✅ |
| **Create/update/delete indexers** | ❌ | ❌ | ✅ | ✅ |
| **Create/update/delete data sources** | ❌ | ❌ | ✅ | ✅ |
| **Create/update/delete skillsets** | ❌ | ❌ | ✅ | ✅ |
| **Create/update/delete synonym maps** | ❌ | ❌ | ✅ | ✅ |
| **Run/reset indexers** | ❌ | ❌ | ✅ | ✅ |
| **Get service statistics** | ❌ | ❌ | ✅ | ✅ |
| **List indexes/indexers/etc** | ❌ | ❌ | ✅ | ✅ |

> **Note:** For full development access, you need: `Search Service Contributor` + `Search Index Data Contributor` + `Search Index Data Reader`

### Important: API Key vs Bearer Token Precedence

> ⚠️ **From Azure Documentation:** "If you configure role-based access for a service or index and you also provide an API key on the request, the search service uses the API key to authenticate."

This means:

- If both `api-key` header and `Authorization: Bearer` are present, **API key takes precedence**
- Clients should use **one or the other**, not both
- Our simulator should follow this same behavior

---

## Part 1: Inbound Authentication (Hybrid Mode)

### 1.1 Authentication Modes

The simulator will support three authentication modes, configurable via `appsettings.json`:

| Mode | Description | Use Case | Azure Required |
| ---- | ----------- | -------- | -------------- |
| `ApiKey` | Current API key authentication | Simple testing, backward compatibility | No |
| `EntraId` | Real Azure AD token validation | Production testing, integration testing | Yes |
| `Simulated` | Mock JWT tokens for local dev | Unit testing, offline development | No |

The system should support **multiple modes simultaneously**, allowing both API key and token-based authentication.

### 1.2 Configuration Schema

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "EntraId", "Simulated"],
    "DefaultMode": "ApiKey",
    "ApiKeyTakesPrecedence": true,
    
    "ApiKey": {
      "AdminApiKey": "admin-key-12345",
      "QueryApiKey": "query-key-67890"
    },
    
    "EntraId": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "your-tenant-id",
      "ClientId": "your-app-registration-client-id",
      "Audience": "https://search.azure.com",
      "ValidAudiences": [
        "https://search.azure.com",
        "https://search.azure.us",
        "https://search.azure.cn"
      ],
      "ValidIssuers": [
        "https://login.microsoftonline.com/{tenantId}/v2.0",
        "https://sts.windows.net/{tenantId}/"
      ],
      "RequireHttpsMetadata": true,
      "AllowMultipleTenants": false
    },
    
    "Simulated": {
      "Enabled": true,
      "Issuer": "https://simulator.local/",
      "Audience": "https://search.azure.com",
      "SigningKey": "base64-encoded-256-bit-key-for-testing-only",
      "TokenLifetimeMinutes": 60,
      "AllowedAppIds": ["test-app-1", "test-app-2"]
    },
    
    "RoleMapping": {
      "OwnerRoles": [
        "Owner",
        "8e3af657-a8ff-443c-a75c-2fe8c4bcb635"
      ],
      "ContributorRoles": [
        "Contributor",
        "b24988ac-6180-42a0-ab88-20f7382dd24c"
      ],
      "ReaderRoles": [
        "Reader",
        "acdd72a7-3385-48ef-bd42-f606fba81ae7"
      ],
      "ServiceContributorRoles": [
        "Search Service Contributor",
        "7ca78c08-252a-4471-8644-bb5ff32d4ba0"
      ],
      "IndexDataContributorRoles": [
        "Search Index Data Contributor", 
        "8ebe5a00-799e-43f5-93ac-243d3dce84a7"
      ],
      "IndexDataReaderRoles": [
        "Search Index Data Reader",
        "1407120a-92aa-4202-b7e9-c0e197c71c8f"
      ]
    }
  }
}
```

### 1.3 Implementation Tasks

#### Task 1.3.1: Create Authentication Configuration Models

**Files to create:**

- `src/AzureAISearchSimulator.Core/Configuration/AuthenticationSettings.cs`

**Models needed:**

```csharp
public class AuthenticationSettings
{
    public const string SectionName = "Authentication";
    
    public List<string> EnabledModes { get; set; } = new() { "ApiKey" };
    public string DefaultMode { get; set; } = "ApiKey";
    public ApiKeySettings ApiKey { get; set; } = new();
    public EntraIdSettings EntraId { get; set; } = new();
    public SimulatedAuthSettings Simulated { get; set; } = new();
    public RoleMappingSettings RoleMapping { get; set; } = new();
}

public class EntraIdSettings
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Audience { get; set; } = "";
    public List<string> ValidIssuers { get; set; } = new();
    public bool RequireHttpsMetadata { get; set; } = true;
}

public class SimulatedAuthSettings
{
    public bool Enabled { get; set; } = true;
    public string Issuer { get; set; } = "https://simulator.local/";
    public string Audience { get; set; } = "api://azure-ai-search-simulator";
    public string SigningKey { get; set; } = "";
    public int TokenLifetimeMinutes { get; set; } = 60;
    public List<string> AllowedRoles { get; set; } = new();
    public List<string> AllowedAppIds { get; set; } = new();
}

public class RoleMappingSettings
{
    /// <summary>
    /// Owner role - full control plane + service contributor data access
    /// </summary>
    public List<string> OwnerRoles { get; set; } = new() 
    { 
        "Owner", 
        "8e3af657-a8ff-443c-a75c-2fe8c4bcb635" 
    };
    
    /// <summary>
    /// Contributor role - same as Owner minus role assignment
    /// </summary>
    public List<string> ContributorRoles { get; set; } = new() 
    { 
        "Contributor", 
        "b24988ac-6180-42a0-ab88-20f7382dd24c" 
    };
    
    /// <summary>
    /// Reader role - read service info, metrics, object definitions
    /// </summary>
    public List<string> ReaderRoles { get; set; } = new() 
    { 
        "Reader", 
        "acdd72a7-3385-48ef-bd42-f606fba81ae7" 
    };
    
    /// <summary>
    /// Search Service Contributor - manage indexes, indexers, skillsets, etc.
    /// </summary>
    public List<string> ServiceContributorRoles { get; set; } = new() 
    { 
        "Search Service Contributor", 
        "7ca78c08-252a-4471-8644-bb5ff32d4ba0" 
    };
    
    /// <summary>
    /// Search Index Data Contributor - load/index documents
    /// </summary>
    public List<string> IndexDataContributorRoles { get; set; } = new() 
    { 
        "Search Index Data Contributor", 
        "8ebe5a00-799e-43f5-93ac-243d3dce84a7" 
    };
    
    /// <summary>
    /// Search Index Data Reader - query indexes only
    /// </summary>
    public List<string> IndexDataReaderRoles { get; set; } = new() 
    { 
        "Search Index Data Reader", 
        "1407120a-92aa-4202-b7e9-c0e197c71c8f" 
    };
}
```

#### Task 1.3.2: Create Authentication Handler Interface

**Files to create:**

- `src/AzureAISearchSimulator.Core/Services/Authentication/IAuthenticationHandler.cs`
- `src/AzureAISearchSimulator.Core/Services/Authentication/AuthenticationResult.cs`

**Interface design:**

```csharp
public interface IAuthenticationHandler
{
    string AuthenticationMode { get; }
    bool CanHandle(HttpContext context);
    Task<AuthenticationResult> AuthenticateAsync(HttpContext context);
}

public class AuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public string? IdentityType { get; set; } // "User", "ServicePrincipal", "ManagedIdentity", "ApiKey"
    public string? IdentityId { get; set; }
    public string? IdentityName { get; set; }
    public string? TenantId { get; set; }
    public AccessLevel AccessLevel { get; set; } // Admin, Query, None
    public List<string> Roles { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Access levels matching Azure AI Search RBAC model
/// </summary>
public enum AccessLevel
{
    /// <summary>No access</summary>
    None,
    
    /// <summary>Search Index Data Reader - Query indexes only</summary>
    IndexDataReader,
    
    /// <summary>Search Index Data Contributor - Load documents + query</summary>
    IndexDataContributor,
    
    /// <summary>Search Service Contributor - Manage indexes, indexers, skillsets (no query/index)</summary>
    ServiceContributor,
    
    /// <summary>Full access (Owner/Contributor + all data roles)</summary>
    FullAccess
}
```

#### Task 1.3.3: Implement API Key Authentication Handler

**Files to create:**

- `src/AzureAISearchSimulator.Api/Services/Authentication/ApiKeyAuthenticationHandler.cs`

**Refactor existing middleware** into a handler implementing `IAuthenticationHandler`:

- Extract key validation logic
- Return `AuthenticationResult` instead of directly writing response
- Support both `api-key` header and query parameter (for compatibility)

#### Task 1.3.4: Implement Entra ID Authentication Handler

**Files to create:**

- `src/AzureAISearchSimulator.Api/Services/Authentication/EntraIdAuthenticationHandler.cs`

**NuGet packages required:**

- `Microsoft.Identity.Web` (for token validation)
- `Microsoft.AspNetCore.Authentication.JwtBearer`

**Implementation details:**

```csharp
public class EntraIdAuthenticationHandler : IAuthenticationHandler
{
    public string AuthenticationMode => "EntraId";
    
    public bool CanHandle(HttpContext context)
    {
        // Check for Authorization: Bearer <token> header
        return context.Request.Headers.Authorization
            .ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }
    
    public async Task<AuthenticationResult> AuthenticateAsync(HttpContext context)
    {
        // 1. Extract token from Authorization header
        // 2. Validate token against Azure AD
        // 3. Extract claims (oid, tid, appid, roles, scp)
        // 4. Map roles to AccessLevel
        // 5. Return AuthenticationResult
    }
}
```

**Claims to extract:**

| Claim | Description | Used For |
| ----- | ----------- | -------- |
| `aud` | Audience | Must be `https://search.azure.com` (or sovereign cloud equivalent) |
| `iss` | Issuer | Token issuer URL for validation |
| `oid` | Object ID | Identity identification (user or service principal) |
| `tid` | Tenant ID | Multi-tenant scenarios |
| `appid` / `azp` | Application ID | Service principal identification |
| `roles` | App roles | Permission mapping (e.g., `Search Index Data Reader`) |
| `scp` | Scopes | Delegated permission mapping (user tokens) |
| `name` / `preferred_username` | Display name | Logging |
| `idtyp` | Identity type | `app` for service principal, `user` for user |
| `exp` | Expiration | Token validity check |
| `nbf` | Not before | Token validity check |

#### Task 1.3.5: Implement Simulated Token Authentication Handler

**Files to create:**

- `src/AzureAISearchSimulator.Api/Services/Authentication/SimulatedAuthenticationHandler.cs`

**Implementation details:**

- Accept tokens signed with the configured test signing key
- Validate token structure matches Entra ID format
- Support configurable roles and app IDs
- Useful for unit testing without Azure connectivity

#### Task 1.3.6: Create Simulated Token Generator

**Files to create:**

- `src/AzureAISearchSimulator.Api/Services/Authentication/SimulatedTokenService.cs`
- `src/AzureAISearchSimulator.Api/Controllers/TokenController.cs` (optional, for testing)

**Functionality:**

- Generate JWT tokens that mimic Entra ID structure
- Support creating tokens for different identities (user, service principal)
- Useful for SDK testing and integration tests

**Token structure to mimic (matching real Azure tokens):**

```json
{
  "aud": "https://search.azure.com",
  "iss": "https://simulator.local/",
  "iat": 1706620800,
  "nbf": 1706620800,
  "exp": 1706624400,
  "oid": "00000000-0000-0000-0000-000000000001",
  "tid": "00000000-0000-0000-0000-000000000000",
  "appid": "test-app-1",
  "idtyp": "app",
  "roles": ["Search Index Data Contributor", "Search Index Data Reader"],
  "ver": "2.0"
}
```

**Alternative for user tokens (delegated):**

```json
{
  "aud": "https://search.azure.com",
  "iss": "https://simulator.local/",
  "iat": 1706620800,
  "nbf": 1706620800,
  "exp": 1706624400,
  "oid": "00000000-0000-0000-0000-000000000002",
  "tid": "00000000-0000-0000-0000-000000000000",
  "name": "Test User",
  "preferred_username": "testuser@contoso.com",
  "idtyp": "user",
  "scp": "user_impersonation",
  "ver": "2.0"
}
```

#### Task 1.3.7: Create Unified Authentication Middleware

**Files to modify:**

- `src/AzureAISearchSimulator.Api/Middleware/ApiKeyAuthenticationMiddleware.cs` → Rename to `AuthenticationMiddleware.cs`

**New middleware design:**

```csharp
public class AuthenticationMiddleware
{
    private readonly IEnumerable<IAuthenticationHandler> _handlers;
    private readonly AuthenticationSettings _settings;
    
    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Check if path requires authentication (skip health, swagger)
        if (ShouldSkipAuthentication(context.Request.Path))
        {
            await _next(context);
            return;
        }
        
        // 2. Check for API key first (Azure behavior: API key takes precedence)
        //    This matches real Azure AI Search: "If you provide an API key on the 
        //    request, the search service uses the API key to authenticate."
        if (_settings.ApiKeyTakesPrecedence && 
            context.Request.Headers.ContainsKey("api-key"))
        {
            var apiKeyHandler = _handlers.OfType<ApiKeyAuthenticationHandler>().First();
            var result = await apiKeyHandler.AuthenticateAsync(context);
            if (result.IsAuthenticated)
            {
                SetAuthenticationResult(context, result);
                await _next(context);
                return;
            }
        }
        
        // 3. Try each enabled handler in configured order
        foreach (var handler in _handlers.Where(h => _settings.EnabledModes.Contains(h.AuthenticationMode)))
        {
            if (handler.CanHandle(context))
            {
                var result = await handler.AuthenticateAsync(context);
                if (result.IsAuthenticated)
                {
                    SetAuthenticationResult(context, result);
                    await _next(context);
                    return;
                }
            }
        }
        
        // 4. No handler succeeded - return 401
        await WriteUnauthorizedResponse(context);
    }
}
```

#### Task 1.3.8: Update Authorization Logic

**Files to modify:**

- Create `src/AzureAISearchSimulator.Api/Services/Authorization/AuthorizationService.cs`

**Functionality:**

- Check if authenticated identity has required access level
- Support role-based authorization
- Map Entra ID roles to simulator permissions
- Provide consistent authorization across all authentication modes

### 1.4 Testing Requirements

#### Unit Tests

- `ApiKeyAuthenticationHandlerTests.cs`
- `EntraIdAuthenticationHandlerTests.cs`
- `SimulatedAuthenticationHandlerTests.cs`
- `AuthorizationServiceTests.cs`

#### Integration Tests

- Test API key authentication (existing)
- Test simulated token authentication
- Test mixed authentication scenarios
- Test role-based access control

#### Manual Testing

- Create HTTP requests with different authentication methods
- Test with Azure SDK using different credentials

---

## Part 2: Outbound Authentication (DefaultAzureCredential)

### 2.1 Current State

The data source connectors already support `DefaultAzureCredential`:

```csharp
// In AzureBlobStorageConnector.cs
var serviceClient = new BlobServiceClient(new Uri(connectionString), new DefaultAzureCredential());
```

### 2.2 Enhancement Goals

1. **Unified credential management** across all connectors
2. **Configurable credential chain** for different environments
3. **Support for explicit service principal** when managed identity isn't available
4. **Credential caching** for performance
5. **Detailed logging** for troubleshooting authentication issues

### 2.3 Configuration Schema

```json
{
  "OutboundAuthentication": {
    "Mode": "DefaultCredential",
    "DefaultCredential": {
      "ExcludeEnvironmentCredential": false,
      "ExcludeManagedIdentityCredential": false,
      "ExcludeAzureCliCredential": false,
      "ExcludeVisualStudioCredential": false,
      "ExcludeVisualStudioCodeCredential": false,
      "ExcludeInteractiveBrowserCredential": true,
      "ManagedIdentityClientId": null,
      "TenantId": null
    },
    "ServicePrincipal": {
      "TenantId": "",
      "ClientId": "",
      "ClientSecret": "",
      "CertificatePath": "",
      "CertificatePassword": ""
    },
    "TokenCache": {
      "Enabled": true,
      "CacheDurationMinutes": 45
    }
  }
}
```

### 2.4 Implementation Tasks

#### Task 2.4.1: Create Outbound Authentication Configuration

**Files to create:**

- `src/AzureAISearchSimulator.Core/Configuration/OutboundAuthenticationSettings.cs`

#### Task 2.4.2: Create Credential Factory Service

**Files to create:**

- `src/AzureAISearchSimulator.Core/Services/Authentication/ICredentialFactory.cs`
- `src/AzureAISearchSimulator.Core/Services/Authentication/CredentialFactory.cs`

**Interface:**

```csharp
public interface ICredentialFactory
{
    TokenCredential GetCredential();
    TokenCredential GetCredential(string? managedIdentityClientId);
    Task<AccessToken> GetTokenAsync(string[] scopes, CancellationToken cancellationToken = default);
}
```

**Implementation:**

```csharp
public class CredentialFactory : ICredentialFactory
{
    private readonly OutboundAuthenticationSettings _settings;
    private readonly ILogger<CredentialFactory> _logger;
    private TokenCredential? _cachedCredential;
    
    public TokenCredential GetCredential()
    {
        if (_cachedCredential != null) return _cachedCredential;
        
        _cachedCredential = _settings.Mode switch
        {
            "DefaultCredential" => CreateDefaultAzureCredential(),
            "ServicePrincipal" => CreateServicePrincipalCredential(),
            "ManagedIdentity" => CreateManagedIdentityCredential(),
            _ => new DefaultAzureCredential()
        };
        
        return _cachedCredential;
    }
    
    private TokenCredential CreateDefaultAzureCredential()
    {
        var options = new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = _settings.DefaultCredential.ExcludeEnvironmentCredential,
            ExcludeManagedIdentityCredential = _settings.DefaultCredential.ExcludeManagedIdentityCredential,
            ExcludeAzureCliCredential = _settings.DefaultCredential.ExcludeAzureCliCredential,
            // ... other exclusions
            TenantId = _settings.DefaultCredential.TenantId,
            ManagedIdentityClientId = _settings.DefaultCredential.ManagedIdentityClientId
        };
        
        return new DefaultAzureCredential(options);
    }
}
```

#### Task 2.4.3: Update Data Source Connectors

**Files to modify:**

- `src/AzureAISearchSimulator.DataSources/AzureBlobStorageConnector.cs`
- `src/AzureAISearchSimulator.DataSources/AdlsGen2Connector.cs`

**Changes:**

- Inject `ICredentialFactory` instead of creating credentials inline
- Use factory for all Azure service authentication
- Add detailed logging for credential resolution

#### Task 2.4.4: Create Credential Diagnostics Endpoint

**Files to create:**

- `src/AzureAISearchSimulator.Api/Controllers/DiagnosticsController.cs`

**Functionality:**

- Endpoint to test outbound credential configuration
- Show which credential type is being used
- Test connectivity to Azure services
- Useful for troubleshooting in different environments

```csharp
[HttpGet("diagnostics/credentials")]
public async Task<IActionResult> TestCredentials()
{
    // Returns info about current credential chain
    // Tests token acquisition for common scopes
}
```

### 2.5 Data Source Credential Configuration

Support specifying credentials per data source:

```json
{
  "name": "my-blob-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "https://mystorageaccount.blob.core.windows.net"
  },
  "identity": {
    "type": "SystemAssigned"
  }
}
```

or with user-assigned managed identity:

```json
{
  "identity": {
    "type": "UserAssigned",
    "userAssignedIdentity": "/subscriptions/.../managedIdentities/my-identity"
  }
}
```

---

## Part 3: Resource-Level Identity Configuration

Azure AI Search supports specifying managed identities at the resource level for custom skills, skillsets, data sources, and indexers. This allows fine-grained control over which identity is used for outbound connections.

### 3.1 Custom WebApiSkill Authentication

**Azure AI Search Real Behavior:**

When a custom skill needs to authenticate to a protected endpoint (e.g., Azure Function with Entra ID auth), the search service can acquire a token on behalf of the indexer using its managed identity.

**Skill Definition with Authentication:**

```json
{
  "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
  "name": "my-secure-skill",
  "description": "Calls a protected Azure Function",
  "uri": "https://my-function.azurewebsites.net/api/process",
  "httpMethod": "POST",
  "timeout": "PT60S",
  "batchSize": 10,
  "authResourceId": "api://my-function-app-client-id",
  "authIdentity": null,
  "inputs": [...],
  "outputs": [...]
}
```

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `authResourceId` | string | The application (client) ID or app URI that the skill should authenticate to. Accepted formats: `api://<appId>`, `<appId>/.default`, `api://<appId>/.default`. When set, the indexer acquires a token for this audience using its managed identity. |
| `authIdentity` | object | Specifies which identity to use. `null` = system-assigned managed identity. For user-assigned, provide the identity object. |

**authIdentity Object (User-Assigned):**

```json
{
  "authIdentity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}"
  }
}
```

**How Azure AI Search Handles This:**

1. Indexer calls custom skill endpoint
2. Search service detects `authResourceId` is set
3. Search service uses its managed identity (or specified `authIdentity`) to get a token from Entra ID
4. Token audience is set to `authResourceId`
5. Token is passed to the skill endpoint in the `Authorization: Bearer <token>` header

#### Task 3.1.1: Update Skill Model

**Files to modify:**

- `src/AzureAISearchSimulator.Core/Models/Skillset.cs`

**Add properties:**

```csharp
/// <summary>
/// For CustomWebApiSkill: The application ID or URI to authenticate to.
/// When set, the indexer acquires a token for this audience.
/// Formats: api://{appId}, {appId}/.default, api://{appId}/.default
/// </summary>
[JsonPropertyName("authResourceId")]
public string? AuthResourceId { get; set; }

/// <summary>
/// For CustomWebApiSkill: The managed identity to use for authentication.
/// Null = system-assigned. Provide identity object for user-assigned.
/// </summary>
[JsonPropertyName("authIdentity")]
public SearchIdentity? AuthIdentity { get; set; }
```

#### Task 3.1.2: Update CustomWebApiSkillExecutor

**Files to modify:**

- `src/AzureAISearchSimulator.Search/Skills/CustomWebApiSkillExecutor.cs`

**Changes:**

```csharp
public class CustomWebApiSkillExecutor : ISkillExecutor
{
    private readonly ICredentialFactory _credentialFactory;
    
    public async Task<SkillExecutionResult> ExecuteAsync(Skill skill, ...)
    {
        var client = _httpClientFactory.CreateClient();
        
        // If authResourceId is set, acquire token and add Authorization header
        if (!string.IsNullOrEmpty(skill.AuthResourceId))
        {
            var token = await AcquireTokenForSkill(skill);
            client.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", token);
        }
        
        // ... rest of execution
    }
    
    private async Task<string> AcquireTokenForSkill(Skill skill)
    {
        // Normalize the resource ID to a scope
        var scope = NormalizeToScope(skill.AuthResourceId);
        
        // Get credential (system or user-assigned based on authIdentity)
        var credential = skill.AuthIdentity != null
            ? _credentialFactory.GetCredential(skill.AuthIdentity.UserAssignedIdentity)
            : _credentialFactory.GetCredential();
            
        var tokenResult = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { scope }), 
            CancellationToken.None);
            
        return tokenResult.Token;
    }
    
    private static string NormalizeToScope(string authResourceId)
    {
        // api://app-id -> api://app-id/.default
        // app-id -> api://app-id/.default  
        // api://app-id/.default -> api://app-id/.default
        if (authResourceId.EndsWith("/.default"))
            return authResourceId;
        if (authResourceId.StartsWith("api://"))
            return $"{authResourceId}/.default";
        return $"api://{authResourceId}/.default";
    }
}
```

### 3.2 Skillset Cognitive Services Identity

**Azure AI Search Real Behavior:**

Skillsets can specify how to authenticate to Azure AI Services (for built-in skills billing):

**Types supported:**

| Type | Description |
| ---- | ----------- |
| `#Microsoft.Azure.Search.CognitiveServicesByKey` | API key (regional endpoint) |
| `#Microsoft.Azure.Search.AIServicesByKey` | API key (subdomain endpoint) |
| `#Microsoft.Azure.Search.AIServicesByIdentity` | Managed identity (keyless) |

**Skillset with Managed Identity:**

```json
{
  "name": "my-skillset",
  "skills": [...],
  "cognitiveServices": {
    "@odata.type": "#Microsoft.Azure.Search.AIServicesByIdentity",
    "description": "Using managed identity for AI Services billing",
    "subdomainUrl": "https://my-ai-resource.cognitiveservices.azure.com",
    "identity": null
  }
}
```

**With User-Assigned Identity:**

```json
{
  "cognitiveServices": {
    "@odata.type": "#Microsoft.Azure.Search.AIServicesByIdentity",
    "subdomainUrl": "https://my-ai-resource.cognitiveservices.azure.com",
    "identity": {
      "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
      "userAssignedIdentity": "/subscriptions/.../userAssignedIdentities/my-identity"
    }
  }
}
```

#### Task 3.2.1: Update CognitiveServices Model

**Files to create/modify:**

- `src/AzureAISearchSimulator.Core/Models/CognitiveServicesAccount.cs`

```csharp
public class CognitiveServicesAccount
{
    [JsonPropertyName("@odata.type")]
    public string ODataType { get; set; } = "#Microsoft.Azure.Search.CognitiveServicesByKey";
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    /// <summary>
    /// For ByKey types: The API key for Azure AI Services.
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }
    
    /// <summary>
    /// For AIServicesByKey and AIServicesByIdentity: The subdomain URL.
    /// Format: https://{resource-name}.cognitiveservices.azure.com
    /// </summary>
    [JsonPropertyName("subdomainUrl")]
    public string? SubdomainUrl { get; set; }
    
    /// <summary>
    /// For AIServicesByIdentity: The managed identity to use.
    /// Null = system-assigned managed identity.
    /// </summary>
    [JsonPropertyName("identity")]
    public SearchIdentity? Identity { get; set; }
}
```

### 3.3 Data Source Identity

**Azure AI Search Real Behavior:**

Data sources can specify which managed identity to use when connecting:

```json
{
  "name": "my-blob-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "ResourceId=/subscriptions/.../storageAccounts/myaccount;"
  },
  "container": {
    "name": "my-container"
  },
  "identity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/.../userAssignedIdentities/my-identity"
  }
}
```

**Identity Types:**

| Type | Description |
| ---- | ----------- |
| `null` (or omitted) | System-assigned managed identity |
| `#Microsoft.Azure.Search.DataNone` | No managed identity (use connection string auth) |
| `#Microsoft.Azure.Search.DataUserAssignedIdentity` | Specific user-assigned identity |

#### Task 3.3.1: Update DataSource Model

**Files to modify:**

- `src/AzureAISearchSimulator.Core/Models/DataSource.cs`

**Add property:**

```csharp
/// <summary>
/// The managed identity to use for connecting to the data source.
/// Null = system-assigned. DataNone = no identity (use credentials).
/// </summary>
[JsonPropertyName("identity")]
public SearchIdentity? Identity { get; set; }
```

### 3.4 Indexer Identity

**Azure AI Search Real Behavior:**

Indexers can also specify which identity to use:

```json
{
  "name": "my-indexer",
  "dataSourceName": "my-datasource",
  "targetIndexName": "my-index",
  "skillsetName": "my-skillset",
  "identity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/.../userAssignedIdentities/my-identity"
  }
}
```

The indexer identity is used for:

- Connecting to data sources (if data source doesn't specify its own identity)
- Running custom skills with `authResourceId`
- Any other outbound connections during indexing

#### Task 3.4.1: Update Indexer Model

**Files to modify:**

- `src/AzureAISearchSimulator.Core/Models/Indexer.cs`

**Add property:**

```csharp
/// <summary>
/// The managed identity to use for this indexer's operations.
/// </summary>
[JsonPropertyName("identity")]
public SearchIdentity? Identity { get; set; }
```

### 3.5 Common Identity Model

**Files to create:**

- `src/AzureAISearchSimulator.Core/Models/SearchIdentity.cs`

```csharp
/// <summary>
/// Represents a managed identity configuration for Azure AI Search resources.
/// </summary>
public class SearchIdentity
{
    [JsonPropertyName("@odata.type")]
    public string? ODataType { get; set; }
    
    /// <summary>
    /// For user-assigned identity: The resource ID of the managed identity.
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}
    /// </summary>
    [JsonPropertyName("userAssignedIdentity")]
    public string? UserAssignedIdentity { get; set; }
}

/// <summary>
/// Identity type constants matching Azure AI Search API.
/// </summary>
public static class SearchIdentityTypes
{
    public const string None = "#Microsoft.Azure.Search.DataNone";
    public const string UserAssigned = "#Microsoft.Azure.Search.DataUserAssignedIdentity";
}
```

### 3.6 Simulator Behavior for Identity

Since the simulator runs locally, it needs to handle identity configuration appropriately:

| Scenario | Simulator Behavior |
| -------- | ----------------- |
| `identity: null` | Use `DefaultAzureCredential` (tries multiple methods) |
| `identity.userAssignedIdentity` specified | Use `ManagedIdentityCredential` with client ID extracted from resource ID |
| `identity.@odata.type = DataNone` | Use connection string only (no managed identity) |
| `authResourceId` on custom skill | Acquire token for the specified audience |

**Configuration to control simulation:**

```json
{
  "OutboundAuthentication": {
    "SimulateIdentities": true,
    "IdentityMapping": {
      "/subscriptions/.../userAssignedIdentities/my-identity": {
        "ClientId": "guid-of-identity",
        "TenantId": "tenant-guid"
      }
    }
  }
}
```

---

## Part 4: Implementation Phases

### Phase 1: Foundation (Week 1)

| Task | Priority | Estimate |
| ---- | -------- | -------- |
| Create authentication configuration models | High | 2 hours |
| Create authentication handler interface | High | 1 hour |
| Refactor API key handler | High | 2 hours |
| Create unified authentication middleware | High | 3 hours |
| Update DI registration | High | 1 hour |
| Basic unit tests | High | 2 hours |

**Deliverable:** Existing API key auth works through new architecture

### Phase 2: Simulated Entra ID (Week 2)

| Task | Priority | Estimate |
| ---- | -------- | -------- |
| Implement simulated token handler | High | 3 hours |
| Create simulated token generator | High | 2 hours |
| Create test token endpoint | Medium | 1 hour |
| Role-based authorization | High | 2 hours |
| Integration tests | High | 3 hours |
| Update sample requests | Medium | 1 hour |

**Deliverable:** Local token-based auth without Azure

### Phase 3: Real Entra ID (Week 3)

| Task | Priority | Estimate |
| ---- | -------- | -------- |
| Add Microsoft.Identity.Web | High | 1 hour |
| Implement Entra ID handler | High | 4 hours |
| Configure token validation | High | 2 hours |
| Test with Azure App Registration | High | 2 hours |
| Multi-tenant support | Medium | 2 hours |
| Documentation | High | 2 hours |

**Deliverable:** Real Azure AD token validation

### Phase 4: Outbound Authentication (Week 4)

| Task | Priority | Estimate |
| ---- | -------- | -------- |
| Create credential factory | High | 2 hours |
| Outbound auth configuration | High | 1 hour |
| Update blob connector | High | 2 hours |
| Update ADLS connector | High | 2 hours |
| Diagnostics endpoint | Medium | 2 hours |
| Per-datasource identity | Medium | 3 hours |
| Testing & documentation | High | 3 hours |

**Deliverable:** Flexible outbound Azure authentication

### Phase 5: Resource-Level Identity (Week 5)

| Task | Priority | Estimate |
| ---- | -------- | -------- |
| Create SearchIdentity model | High | 1 hour |
| Add authResourceId/authIdentity to Skill model | High | 1 hour |
| Update CustomWebApiSkillExecutor for token acquisition | High | 4 hours |
| Update CognitiveServicesAccount model | Medium | 2 hours |
| Add identity to DataSource model | High | 1 hour |
| Add identity to Indexer model | High | 1 hour |
| Update data source connectors for identity | High | 3 hours |
| Integration tests for custom skill auth | High | 3 hours |
| Documentation | Medium | 2 hours |

**Deliverable:** Full identity support matching Azure AI Search API

---

## Part 5: API Changes

### 5.1 New Endpoints

| Endpoint | Method | Description |
| -------- | ------ | ----------- |
| `/admin/token` | POST | Generate simulated test tokens |
| `/admin/token/validate` | POST | Validate a token (any mode) |
| `/diagnostics/auth` | GET | Show current auth configuration |
| `/diagnostics/credentials` | GET | Test outbound credentials |

### 5.2 Updated Request Headers

| Header | Modes | Description |
| ------ | ----- | ----------- |
| `api-key` | ApiKey | Existing API key |
| `Authorization: Bearer <token>` | EntraId, Simulated | JWT bearer token |

### 5.3 Response Changes

Error responses will include authentication mode:

```json
{
  "error": {
    "code": "Unauthorized",
    "message": "Invalid or expired token",
    "details": {
      "authenticationMode": "EntraId",
      "reason": "Token expired at 2026-01-30T10:00:00Z"
    }
  }
}
```

---

## Part 6: Security Considerations

### 6.1 Simulated Mode Security

⚠️ **Warning:** Simulated mode should only be used for development/testing.

- Signing key should be randomly generated per environment
- Consider disabling in production builds
- Log warnings when simulated mode is active
- Never use in production Azure environments

### 6.2 Key Storage

- Support Azure Key Vault for production secrets
- Support environment variables for CI/CD
- Never log tokens or keys
- Implement key rotation support

### 6.3 Token Validation

- Always validate token signature
- Check token expiration
- Validate audience matches configuration
- Validate issuer against allowed list
- Check for required roles/scopes

---

## Part 7: Documentation Updates

### 7.1 Files to Update

| File | Changes |
| ---- | ------- |
| `README.md` | Add authentication section |
| `docs/CONFIGURATION.md` | Add authentication configuration |
| `docs/API-REFERENCE.md` | Update authentication headers |
| `samples/sample-requests.http` | Add bearer token examples |

### 7.2 New Files Created

| File | Content |
| ---- | ------- |
| `docs/AUTHENTICATION.md` | Comprehensive auth guide |
| `samples/entra-id-auth-requests.http` | Complete Entra ID authentication samples |
| `samples/entra-id-setup.md` | Azure AD app registration guide |

---

## Part 8: Dependencies

### 8.1 New NuGet Packages

| Package | Version | Purpose |
| ------- | ------- | ------- |
| `Microsoft.Identity.Web` | 2.x | Entra ID token validation |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.x | JWT bearer authentication |
| `System.IdentityModel.Tokens.Jwt` | 7.x | JWT token handling |

### 8.2 Existing Packages (Already Used)

| Package | Purpose |
| ------- | ------- |
| `Azure.Identity` | DefaultAzureCredential for outbound |
| `Azure.Storage.Blobs` | Blob storage client |

---

## Part 9: Success Criteria

### 9.1 Functional Requirements

- [ ] API key authentication continues to work (backward compatible)
- [ ] Simulated tokens can be generated and validated locally
- [ ] Real Entra ID tokens are validated correctly
- [ ] Multiple auth modes can be enabled simultaneously
- [ ] Role-based access control works across all modes
- [ ] Outbound credentials use configurable DefaultAzureCredential
- [ ] Data source connectors support managed identity
- [ ] Custom WebApiSkill supports `authResourceId` and `authIdentity`
- [ ] Custom skills can authenticate to protected endpoints using managed identity
- [ ] Skillsets support `cognitiveServices` with identity-based authentication
- [ ] Data sources support `identity` property for managed identity configuration
- [ ] Indexers support `identity` property
- [ ] SearchIdentity model correctly handles system and user-assigned identities

### 9.2 Non-Functional Requirements

- [ ] Authentication adds < 10ms latency per request
- [ ] Token validation failures provide helpful error messages
- [ ] Configuration errors are caught at startup
- [ ] All authentication events are properly logged
- [ ] Unit test coverage > 80% for auth components

### 9.3 Compatibility Requirements

- [ ] Azure SDK (Azure.Search.Documents) works with all auth modes
- [ ] Existing HTTP samples work without changes (API key)
- [ ] Docker deployment supports all auth configurations

---

## Appendix A: Sample Token Generation Request

```http
POST /admin/token HTTP/1.1
Content-Type: application/json
api-key: admin-key-12345

{
  "identityType": "ServicePrincipal",
  "appId": "test-app-1",
  "roles": ["Search.Admin"],
  "expiresInMinutes": 60
}
```

Response:

```json
{
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
  "expiresAt": "2026-01-30T11:00:00Z",
  "tokenType": "Bearer"
}
```

## Appendix B: Azure AD App Registration Steps

### For Service-to-Service (Service Principal)

1. Register application in Azure AD
2. Create client credentials (secret or certificate)
3. In Azure AI Search, assign built-in roles to the service principal:
   - **Search Service Contributor** (`7ca78c08-252a-4471-8644-bb5ff32d4ba0`) - manage indexes
   - **Search Index Data Contributor** (`8ebe5a00-799e-43f5-93ac-243d3dce84a7`) - index documents
   - **Search Index Data Reader** (`1407120a-92aa-4202-b7e9-c0e197c71c8f`) - query only
4. Configure the app to request tokens with scope `https://search.azure.com/.default`

### For User Authentication (Delegated)

1. Register application in Azure AD
2. Configure redirect URIs for your application
3. Add API permissions: `Azure AI Search` → `user_impersonation`
4. Grant admin consent (if required)
5. Assign Azure roles to users in the Search service IAM

### Role Assignment via Azure CLI

```bash
# Assign Search Index Data Reader to a service principal
az role assignment create \
    --role "Search Index Data Reader" \
    --assignee <service-principal-object-id> \
    --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Search/searchServices/<service>

# Assign to a specific index only
az role assignment create \
    --role "Search Index Data Reader" \
    --assignee <object-id> \
    --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Search/searchServices/<service>/indexes/<index-name>
```

## Appendix C: Migration Guide

### From API Key Only

1. Update `appsettings.json` with new `Authentication` section
2. Keep `ApiKey` in `EnabledModes` for backward compatibility
3. Deploy and verify existing clients work
4. Add `EntraId` or `Simulated` when ready

### Testing Migration

```bash
# Test API key still works
curl -H "api-key: admin-key-12345" http://localhost:5000/indexes

# Test bearer token
curl -H "Authorization: Bearer <token>" http://localhost:5000/indexes
```

## Appendix D: Resource-Level Identity Examples

### Custom Skill with Entra ID Authentication

```json
PUT /skillsets/my-skillset?api-version=2025-09-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "my-skillset",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
      "name": "secure-enrichment-skill",
      "description": "Calls a protected Azure Function",
      "uri": "https://my-function.azurewebsites.net/api/enrich",
      "httpMethod": "POST",
      "timeout": "PT60S",
      "batchSize": 10,
      "authResourceId": "api://my-function-app-client-id",
      "authIdentity": null,
      "context": "/document",
      "inputs": [
        { "name": "text", "source": "/document/content" }
      ],
      "outputs": [
        { "name": "enrichedText", "targetName": "enrichedContent" }
      ]
    }
  ]
}
```

### Custom Skill with User-Assigned Identity

```json
{
  "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
  "name": "secure-skill-with-uami",
  "uri": "https://my-function.azurewebsites.net/api/process",
  "authResourceId": "api://my-function-app",
  "authIdentity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-search-identity"
  },
  "inputs": [...],
  "outputs": [...]
}
```

### Skillset with Managed Identity for AI Services

```json
PUT /skillsets/ai-enrichment-skillset?api-version=2025-09-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "ai-enrichment-skillset",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Text.EntityRecognitionSkill",
      "name": "entity-recognition",
      "context": "/document",
      "inputs": [{ "name": "text", "source": "/document/content" }],
      "outputs": [{ "name": "entities", "targetName": "entities" }]
    }
  ],
  "cognitiveServices": {
    "@odata.type": "#Microsoft.Azure.Search.AIServicesByIdentity",
    "description": "Using managed identity for keyless AI Services billing",
    "subdomainUrl": "https://my-ai-resource.cognitiveservices.azure.com",
    "identity": null
  }
}
```

### Data Source with Managed Identity

```json
PUT /datasources/blob-datasource?api-version=2025-09-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "blob-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "ResourceId=/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/Microsoft.Storage/storageAccounts/mystorageaccount;"
  },
  "container": {
    "name": "documents",
    "query": "subfolder/"
  },
  "identity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-search-identity"
  }
}
```

### Indexer with Identity

```json
PUT /indexers/my-indexer?api-version=2025-09-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "my-indexer",
  "dataSourceName": "blob-datasource",
  "targetIndexName": "my-index",
  "skillsetName": "my-skillset",
  "schedule": {
    "interval": "PT1H"
  },
  "identity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/my-search-identity"
  }
}
```

## Appendix E: Testing Custom Skill Authentication Locally

When testing custom skills with `authResourceId` in the simulator:

### Option 1: Use Simulated Tokens (No Azure Required)

Configure the simulator to generate tokens for custom skill calls:

```json
{
  "OutboundAuthentication": {
    "SimulateIdentities": true,
    "CustomSkillAuth": {
      "Enabled": true,
      "DefaultAudience": "api://test-function"
    }
  }
}
```

### Option 2: Use Real Azure Credentials

Ensure your local development environment has valid Azure credentials:

```bash
# Login with Azure CLI
az login

# The simulator will use DefaultAzureCredential to get tokens
```

### Option 3: Configure Service Principal for Testing

```json
{
  "OutboundAuthentication": {
    "Mode": "ServicePrincipal",
    "ServicePrincipal": {
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret"
    }
  }
}
```
