# Authentication Guide

This guide covers all authentication options available in the Azure AI Search Simulator.

## Overview

The simulator supports three authentication modes that can be used simultaneously:

| Mode | Description | Azure Required |
|------|-------------|----------------|
| **API Key** | Traditional API key authentication | No |
| **Simulated** | Local JWT tokens for testing | No |
| **Entra ID** | Real Azure AD token validation | Yes |

## Quick Start

### API Key (Default)

API key authentication is enabled by default. Use the `api-key` header:

```http
GET https://localhost:7250/indexes?api-version=2024-07-01
api-key: admin-key-12345
```

### Simulated Tokens (Local Development)

Generate test tokens without Azure:

```http
### Get a token with Search Index Data Contributor role
GET https://localhost:7250/admin/token/quick/data-contributor
api-key: admin-key-12345

### Use the token
GET https://localhost:7250/indexes
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Entra ID (Real Azure AD)

Use real Azure AD tokens:

```bash
# Get token using Azure CLI
az account get-access-token --scope https://search.azure.com/.default

# Use in request
curl -H "Authorization: Bearer $TOKEN" https://localhost:7250/indexes
```

---

## Authentication Modes

### API Key Authentication

The traditional Azure AI Search authentication method using API keys.

#### Configuration

```json
{
  "Simulator": {
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890"
  },
  "Authentication": {
    "EnabledModes": ["ApiKey"]
  }
}
```

#### Usage

**Header:**
```http
api-key: admin-key-12345
```

**Query parameter:**
```http
GET /indexes?api-version=2024-07-01&api-key=admin-key-12345
```

#### Key Types

| Key Type | Operations Allowed |
|----------|-------------------|
| Admin Key | All operations (create, update, delete, query) |
| Query Key | Query operations only (search, suggest, autocomplete) |

---

### Simulated Token Authentication

Generate and validate JWT tokens locally without Azure dependencies. Useful for:

- Unit testing
- Local development
- CI/CD pipelines
- Learning and experimentation

#### Configuration

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "Simulated"],
    "Simulated": {
      "Enabled": true,
      "SigningKey": "your-256-bit-secret-key-for-signing-jwt-tokens",
      "Issuer": "https://localhost:7250",
      "Audience": "https://search.azure.com",
      "TokenLifetimeMinutes": 60
    }
  }
}
```

#### Generating Tokens

**Quick Token Generation:**

```http
### Get a token with a specific role
GET https://localhost:7250/admin/token/quick/service-contributor
api-key: admin-key-12345
```

Available shortcuts:
- `owner` - Owner role
- `contributor` - Contributor role
- `reader` - Reader role
- `service-contributor` - Search Service Contributor
- `data-contributor` - Search Index Data Contributor
- `data-reader` - Search Index Data Reader

**Custom Token Generation:**

```http
POST https://localhost:7250/admin/token
Content-Type: application/json
api-key: admin-key-12345

{
  "roles": ["Search Index Data Contributor", "Search Index Data Reader"],
  "subject": "test-app",
  "identityType": "app",
  "expiresInMinutes": 120
}
```

#### Token Validation

```http
POST https://localhost:7250/admin/token/validate
Content-Type: application/json
api-key: admin-key-12345

{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

---

### Entra ID Authentication

Validate real Azure AD tokens for production-like testing.

#### Configuration

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "EntraId"],
    "EntraId": {
      "Enabled": true,
      "TenantId": "your-tenant-id",
      "ClientId": "your-app-client-id",
      "AllowMultipleTenants": false,
      "ValidAudiences": ["https://search.azure.com"],
      "ValidIssuers": [
        "https://login.microsoftonline.com/{tenantId}/v2.0",
        "https://sts.windows.net/{tenantId}/"
      ]
    }
  }
}
```

#### Azure AD App Registration

1. Go to **Azure Portal** → **Microsoft Entra ID** → **App registrations**
2. Click **New registration**
3. Name your app (e.g., "Azure AI Search Simulator")
4. Set supported account types (single tenant recommended)
5. No redirect URI needed for service authentication
6. After creation, note the **Application (client) ID** and **Directory (tenant) ID**

#### Assigning Roles

1. Go to the app registration → **App roles**
2. Create roles matching Azure AI Search:

| Display Name | Value | Description |
|--------------|-------|-------------|
| Search Service Contributor | Search.Service.Contributor | Manage indexes, indexers, skillsets |
| Search Index Data Contributor | Search.Index.Data.Contributor | Upload and manage documents |
| Search Index Data Reader | Search.Index.Data.Reader | Query indexes |

#### Acquiring Tokens

**Azure CLI:**
```bash
az account get-access-token --scope https://search.azure.com/.default
```

**PowerShell:**
```powershell
Get-AzAccessToken -ResourceUrl https://search.azure.com
```

**C# with Azure SDK:**
```csharp
var credential = new DefaultAzureCredential();
var searchClient = new SearchClient(
    new Uri("https://localhost:7250"),
    "my-index",
    credential
);
```

---

## Entra ID Configuration Reference

This section provides detailed configuration guidance for Entra ID authentication with well-known tools and services.

### Known Client IDs

When acquiring tokens from Azure CLI, Azure PowerShell, or other Microsoft tools, use these well-known client IDs:

| Tool/Application | Client ID | Description |
|-----------------|-----------|-------------|
| **Azure CLI** | `04b07795-8ddb-461a-bbee-02f9e1bf7b46` | `az account get-access-token` commands |
| **Azure PowerShell** | `1950a258-227b-4e31-a9cf-717495945fc2` | `Get-AzAccessToken` cmdlet |
| **Visual Studio** | `872cd9fa-d31f-45e0-9eab-6e460a02d1f1` | Visual Studio authentication |
| **VS Code Azure Extension** | `aebc6443-996d-45c2-90f0-388ff96faa56` | Azure extension in VS Code |
| **Azure Portal** | `c44b4083-3bb0-49c1-b47d-974e53cbdf3c` | Portal browser authentication |
| **Microsoft Graph PowerShell** | `14d82eec-204b-4c2f-b7e8-296a70dab67e` | Microsoft Graph module |

### Valid Issuers by Token Version

Azure AD issues tokens in two formats (v1.0 and v2.0). Configure both issuers for maximum compatibility:

| Token Version | Issuer Format | Example |
|--------------|---------------|---------|
| **v1.0** | `https://sts.windows.net/{tenantId}/` | `https://sts.windows.net/5af26bcf-47c7-45a7-9cdc-f40a5a82fc23/` |
| **v2.0** | `https://login.microsoftonline.com/{tenantId}/v2.0` | `https://login.microsoftonline.com/5af26bcf-47c7-45a7-9cdc-f40a5a82fc23/v2.0` |

> **Note:** Azure CLI typically issues v1.0 tokens, while Azure PowerShell and the Azure SDKs may issue v2.0 tokens.

### Configuring the EntraId Section

Complete configuration example with explanations:

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "Simulated", "EntraId"],
    "EntraId": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "<YOUR-TENANT-ID>",
      "ClientId": "04b07795-8ddb-461a-bbee-02f9e1bf7b46",
      "Audience": "https://search.azure.com",
      "ValidIssuers": [
        "https://sts.windows.net/<YOUR-TENANT-ID>/",
        "https://login.microsoftonline.com/<YOUR-TENANT-ID>/v2.0"
      ],
      "RequireHttpsMetadata": true,
      "AllowMultipleTenants": false,
      "DefaultRoles": [
        "Search Index Data Reader",
        "Search Index Data Contributor",
        "Search Service Contributor"
      ]
    }
  }
}
```

#### Configuration Fields

| Field | Required | Description |
|-------|----------|-------------|
| `TenantId` | **Yes** | Your Azure AD tenant ID (GUID). Find it in Azure Portal → Microsoft Entra ID → Overview. |
| `ClientId` | **Yes** | The application client ID. Use Azure CLI's client ID (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) for CLI tokens. |
| `Audience` | **Yes** | Must be `https://search.azure.com` for Azure AI Search tokens. |
| `ValidIssuers` | **Yes** | Array of valid token issuers. Include both v1.0 and v2.0 formats with your tenant ID. |
| `Instance` | No | Azure AD instance URL. Default: `https://login.microsoftonline.com/` |
| `RequireHttpsMetadata` | No | Whether to require HTTPS for metadata endpoint. Default: `true` |
| `AllowMultipleTenants` | No | Allow tokens from any tenant. Default: `false` |
| `DefaultRoles` | No | Roles to assign when token has no explicit roles. Useful for personal accounts without RBAC assignments. |

### Finding Your Tenant ID

#### Option 1: Azure Portal

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Microsoft Entra ID** → **Overview**
3. Copy the **Tenant ID** (Directory ID)

#### Option 2: Azure CLI

```bash
az account show --query tenantId -o tsv
```

#### Option 3: PowerShell

```powershell
(Get-AzContext).Tenant.Id
```

### Getting Tokens with Azure CLI

#### Prerequisites

Install Azure CLI if not already installed:

```bash
# Windows (winget)
winget install Microsoft.AzureCLI

# macOS (Homebrew)
brew install azure-cli

# Linux (Ubuntu/Debian)
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

#### Authentication and Token Acquisition

- **Step 1: Login to Azure**

```bash
# Interactive login (opens browser)
az login

# Login with specific tenant
az login --tenant <YOUR-TENANT-ID>

# Login with device code (for remote/headless systems)
az login --use-device-code
```

- **Step 2: Get a token for Azure AI Search**

```bash
# Get token as JSON (includes expiration info)
az account get-access-token --scope https://search.azure.com/.default

# Get just the token value
az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv
```

**Example output:**

```json
{
  "accessToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6Ik...",
  "expiresOn": "2026-01-31 15:30:00.000000",
  "subscription": "your-subscription-id",
  "tenant": "your-tenant-id",
  "tokenType": "Bearer"
}
```

- **Step 3: Use the token with the simulator**

```bash
# Store token in a variable (Bash)
TOKEN=$(az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv)

# Use with curl
curl -H "Authorization: Bearer $TOKEN" \
     "https://localhost:7250/indexes?api-version=2024-07-01"
```

```powershell
# Store token in a variable (PowerShell)
$token = az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv

# Use with Invoke-RestMethod
$headers = @{ "Authorization" = "Bearer $token" }
Invoke-RestMethod -Uri "https://localhost:7250/indexes?api-version=2024-07-01" -Headers $headers
```

### Getting Tokens with Azure PowerShell

```powershell
# Login
Connect-AzAccount

# Login with specific tenant
Connect-AzAccount -TenantId "<YOUR-TENANT-ID>"

# Get token
$token = (Get-AzAccessToken -ResourceUrl "https://search.azure.com").Token

# Use with Invoke-RestMethod
$headers = @{ "Authorization" = "Bearer $token" }
Invoke-RestMethod -Uri "https://localhost:7250/indexes?api-version=2024-07-01" -Headers $headers
```

### HTTP Client Examples (.http files)

For VS Code REST Client or JetBrains HTTP Client:

```http
### Variables - Update these for your environment
@baseUrl = https://localhost:7250
@apiVersion = 2024-07-01

### Get token using Azure CLI (run in terminal first)
# az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv
@bearerToken = <paste-your-token-here>

### List indexes with Entra ID token
GET {{baseUrl}}/indexes?api-version={{apiVersion}}
Authorization: Bearer {{bearerToken}}

### Search with Entra ID token
POST {{baseUrl}}/indexes/my-index/docs/search?api-version={{apiVersion}}
Authorization: Bearer {{bearerToken}}
Content-Type: application/json

{
  "search": "*",
  "count": true
}
```

### Troubleshooting Entra ID Authentication

#### "InvalidIssuer" Error

**Problem:** Token issuer doesn't match configured valid issuers.

**Solution:** Ensure `ValidIssuers` includes both v1.0 and v2.0 formats:

```json
"ValidIssuers": [
  "https://sts.windows.net/<YOUR-TENANT-ID>/",
  "https://login.microsoftonline.com/<YOUR-TENANT-ID>/v2.0"
]
```

#### "InvalidAudience" Error

**Problem:** Token was requested for wrong resource.

**Solution:** Ensure you request tokens with scope `https://search.azure.com/.default`

#### 403 Forbidden After Successful Authentication

**Problem:** User authenticated but has no roles assigned.

**Solution:** Either:

1. Assign Azure AI Search RBAC roles in Azure Portal
2. Or configure `DefaultRoles` in the simulator for development

#### Token Expired

**Problem:** Azure CLI tokens expire after ~1 hour.

**Solution:** Get a fresh token:

```bash
az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv
```

---

## Role-Based Access Control (RBAC)

The simulator enforces the same RBAC permissions as real Azure AI Search.

### Permission Matrix

| Operation | Data Reader | Data Contributor | Service Contributor | Owner/Contributor |
|-----------|:-----------:|:----------------:|:-------------------:|:-----------------:|
| Search/Suggest/Autocomplete | ✅ | ✅ | ❌ | ❌ |
| Get document by key | ✅ | ✅ | ❌ | ❌ |
| Upload/Merge/Delete documents | ❌ | ✅ | ❌ | ❌ |
| Create/Update/Delete indexes | ❌ | ❌ | ✅ | ✅ |
| Create/Update/Delete indexers | ❌ | ❌ | ✅ | ✅ |
| Create/Update/Delete data sources | ❌ | ❌ | ✅ | ✅ |
| Create/Update/Delete skillsets | ❌ | ❌ | ✅ | ✅ |
| Run/Reset indexers | ❌ | ❌ | ✅ | ✅ |
| Get service statistics | ❌ | ❌ | ✅ | ✅ |

### Role GUIDs

| Role | GUID |
|------|------|
| Owner | `8e3af657-a8ff-443c-a75c-2fe8c4bcb635` |
| Contributor | `b24988ac-6180-42a0-ab88-20f7382dd24c` |
| Reader | `acdd72a7-3385-48ef-bd42-f606fba81ae7` |
| Search Service Contributor | `7ca78c08-252a-4471-8644-bb5ff32d4ba0` |
| Search Index Data Contributor | `8ebe5a00-799e-43f5-93ac-243d3dce84a7` |
| Search Index Data Reader | `1407120a-92aa-4202-b7e9-c0e197c71c8f` |

---

## Outbound Authentication

The simulator can authenticate to external Azure services when using:
- Azure Blob Storage data sources
- ADLS Gen2 data sources
- Custom Web API skills
- Azure OpenAI embedding skills

### Configuration

```json
{
  "OutboundAuthentication": {
    "DefaultCredentialType": "DefaultAzureCredential",
    "ServicePrincipal": {
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret"
    },
    "ManagedIdentity": {
      "ClientId": "user-assigned-identity-client-id"
    },
    "TokenCache": {
      "ExpirationBufferMinutes": 5,
      "MaxCachedTokens": 100
    }
  }
}
```

### Credential Types

| Type | Description | Use Case |
|------|-------------|----------|
| `DefaultAzureCredential` | Azure SDK default chain | Development, Azure-hosted apps |
| `ServicePrincipal` | App with client secret/certificate | CI/CD, automated processes |
| `ManagedIdentity` | Azure Managed Identity | Azure-hosted apps |

### Resource-Level Identity

Data sources, indexers, and skills can specify their own identity:

```json
{
  "name": "my-blob-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "ResourceId=/subscriptions/.../storageAccounts/mystorageaccount"
  },
  "identity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/.../userAssignedIdentities/my-identity"
  }
}
```

### Custom Skill Authentication

Custom Web API skills can authenticate using managed identity:

```json
{
  "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
  "name": "myCustomSkill",
  "uri": "https://my-function.azurewebsites.net/api/process",
  "authResourceId": "https://my-function.azurewebsites.net",
  "authIdentity": {
    "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
    "userAssignedIdentity": "/subscriptions/.../userAssignedIdentities/my-identity"
  }
}
```

---

## Diagnostics

### View Authentication Configuration

```http
GET https://localhost:7250/admin/diagnostics/auth
api-key: admin-key-12345
```

### Test Outbound Credentials

```http
GET https://localhost:7250/admin/diagnostics/credentials/test
api-key: admin-key-12345
```

### Acquire Token for Scope

```http
POST https://localhost:7250/admin/diagnostics/credentials/token
Content-Type: application/json
api-key: admin-key-12345

{
  "scope": "https://storage.azure.com/.default"
}
```

---

## Best Practices

### Development

1. Use **API keys** for simple local testing
2. Use **Simulated tokens** for RBAC testing without Azure
3. Enable **detailed errors** in development

### Testing

1. Use **Simulated tokens** in CI/CD pipelines
2. Create tokens with different roles to test RBAC
3. Test both success and failure scenarios

### Production-Like Testing

1. Use **Entra ID** mode with real Azure AD tokens
2. Configure proper tenant and audience
3. Test with actual Azure RBAC assignments

### Security

1. Never commit API keys to source control
2. Use environment variables for sensitive settings
3. Rotate keys regularly
4. Use short token lifetimes in development

---

## Troubleshooting

### 401 Unauthorized

**Causes:**
- Missing or invalid API key
- Expired or malformed JWT token
- Token audience/issuer mismatch

**Solutions:**
1. Check the `api-key` header spelling
2. Verify token hasn't expired
3. Check authentication mode is enabled

### 403 Forbidden

**Causes:**
- Insufficient role permissions
- Operation not allowed for role

**Solutions:**
1. Check required role for operation
2. Generate token with appropriate role
3. Use admin key for full access

### Token Validation Failed

**Causes:**
- Wrong signing key (simulated mode)
- Invalid issuer configuration (Entra ID)
- Clock skew issues

**Solutions:**
1. Verify signing key matches
2. Check issuer in configuration
3. Allow 5-minute clock skew tolerance

---

## API Key vs Bearer Token Precedence

> ⚠️ **Important:** If both `api-key` header and `Authorization: Bearer` are provided, the **API key takes precedence**. This matches Azure AI Search behavior.

Use one authentication method per request, not both.
