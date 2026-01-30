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
