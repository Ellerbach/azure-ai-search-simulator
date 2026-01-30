# Configuration Guide

This guide explains all configuration options available for the Azure AI Search Simulator.

## Configuration Sources

The simulator uses the standard ASP.NET Core configuration system. Settings can be provided through:

1. **appsettings.json** - Default configuration file
2. **appsettings.{Environment}.json** - Environment-specific overrides
3. **Environment variables** - For containerized deployments
4. **Command line arguments** - For one-off overrides

## Configuration Sections

### Simulator Settings

```json
{
  "Simulator": {
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890",
    "DataPath": "./data",
    "EnableDetailedErrors": true
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `AdminApiKey` | API key for admin operations (create, update, delete) | `admin-key-12345` |
| `QueryApiKey` | API key for query operations (search, suggest) | `query-key-67890` |
| `DataPath` | Path for LiteDB database files | `./data` |
| `EnableDetailedErrors` | Show detailed error messages in development | `true` |

**Environment variables:**

```bash
Simulator__AdminApiKey=your-admin-key
Simulator__QueryApiKey=your-query-key
Simulator__DataPath=/app/data
```

### Lucene Settings

```json
{
  "Lucene": {
    "IndexPath": "./lucene-indexes",
    "MaxMergeCount": 10,
    "RAMBufferSizeMB": 16.0
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `IndexPath` | Directory for Lucene index files | `./lucene-indexes` |
| `MaxMergeCount` | Maximum concurrent merge operations | `10` |
| `RAMBufferSizeMB` | RAM buffer size for indexing (MB) | `16.0` |

**Environment variables:**

```bash
Lucene__IndexPath=/app/lucene-indexes
Lucene__RAMBufferSizeMB=32.0
```

### Indexer Settings

```json
{
  "Indexer": {
    "DefaultBatchSize": 1000,
    "MaxFailedItems": -1,
    "MaxFailedItemsPerBatch": -1,
    "DefaultScheduleInterval": "PT1H"
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `DefaultBatchSize` | Default batch size for indexer operations | `1000` |
| `MaxFailedItems` | Maximum failed items before stopping (-1 = unlimited) | `-1` |
| `MaxFailedItemsPerBatch` | Maximum failed items per batch (-1 = unlimited) | `-1` |
| `DefaultScheduleInterval` | Default indexer schedule (ISO 8601 duration) | `PT1H` |

### Vector Search Settings

```json
{
  "VectorSearchSettings": {
    "DefaultDimensions": 1536,
    "MaxVectorsPerIndex": 100000,
    "SimilarityMetric": "cosine",
    "UseHnsw": true,
    "HnswSettings": {
      "M": 16,
      "EfConstruction": 200,
      "EfSearch": 100,
      "OversampleMultiplier": 5,
      "RandomSeed": 42
    },
    "HybridSearchSettings": {
      "DefaultFusionMethod": "RRF",
      "DefaultVectorWeight": 0.7,
      "DefaultTextWeight": 0.3,
      "RrfK": 60
    }
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `DefaultDimensions` | Default vector dimensions | `1536` |
| `MaxVectorsPerIndex` | Maximum vectors per index | `100000` |
| `SimilarityMetric` | Similarity metric (cosine, euclidean, dotProduct) | `cosine` |
| `UseHnsw` | Use HNSW algorithm (true) or brute-force (false) | `true` |

#### HNSW Settings

The HNSW (Hierarchical Navigable Small World) algorithm provides fast approximate nearest neighbor search.

| Setting | Description | Default | Recommended Range |
| ------- | ----------- | ------- | ----------------- |
| `M` | Number of bi-directional links per node | `16` | 12-48 |
| `EfConstruction` | Search depth during index build | `200` | 100-500 |
| `EfSearch` | Search depth during query | `100` | 50-500 |
| `OversampleMultiplier` | Multiplier for filtered search | `5` | 3-10 |
| `RandomSeed` | Random seed (-1 for random) | `42` | Any integer |

**Performance tuning:**

- Higher `M` = better recall, more memory, slower build
- Higher `EfConstruction` = better index quality, slower build
- Higher `EfSearch` = better recall, slower queries

**Environment variables:**

```bash
VectorSearchSettings__UseHnsw=true
VectorSearchSettings__HnswSettings__M=16
VectorSearchSettings__HnswSettings__EfSearch=100
```

#### Hybrid Search Settings

Configure how text and vector search results are combined.

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `DefaultFusionMethod` | Fusion method (RRF or Weighted) | `RRF` |
| `DefaultVectorWeight` | Vector score weight (0.0-1.0) | `0.7` |
| `DefaultTextWeight` | Text score weight (0.0-1.0) | `0.3` |
| `RrfK` | RRF constant k in formula 1/(k+rank) | `60` |

### Azure OpenAI Settings

```json
{
  "AzureOpenAI": {
    "ApiKey": "",
    "DefaultModel": "text-embedding-ada-002",
    "DefaultDimensions": 1536,
    "Timeout": "00:01:00"
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `ApiKey` | Azure OpenAI API key for embedding skill | *(empty)* |
| `DefaultModel` | Default embedding model name | `text-embedding-ada-002` |
| `DefaultDimensions` | Default embedding dimensions | `1536` |
| `Timeout` | HTTP timeout for API calls | `00:01:00` |

**Environment variables:**

```bash
AzureOpenAI__ApiKey=your-azure-openai-key
```

### Authentication Settings

The simulator supports multiple authentication modes that can be enabled simultaneously.

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey"],
    "DefaultMode": "ApiKey",
    "ApiKeyTakesPrecedence": true,
    "ApiKey": {
      "AdminApiKey": null,
      "QueryApiKey": null
    },
    "EntraId": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "",
      "ClientId": "",
      "Audience": "https://search.azure.com",
      "ValidIssuers": [],
      "RequireHttpsMetadata": true,
      "AllowMultipleTenants": false
    },
    "Simulated": {
      "Enabled": false,
      "Issuer": "https://simulator.local/",
      "Audience": "https://search.azure.com",
      "SigningKey": "SimulatorSigningKey-Change-This-In-Production-12345678",
      "TokenLifetimeMinutes": 60
    },
    "RoleMapping": {
      "OwnerRoles": ["Owner", "8e3af657-a8ff-443c-a75c-2fe8c4bcb635"],
      "ContributorRoles": ["Contributor", "b24988ac-6180-42a0-ab88-20f7382dd24c"],
      "ReaderRoles": ["Reader", "acdd72a7-3385-48ef-bd42-f606fba81ae7"],
      "ServiceContributorRoles": ["Search Service Contributor", "7ca78c08-252a-4471-8644-bb5ff32d4ba0"],
      "IndexDataContributorRoles": ["Search Index Data Contributor", "8ebe5a00-799e-43f5-93ac-243d3dce84a7"],
      "IndexDataReaderRoles": ["Search Index Data Reader", "1407120a-92aa-4202-b7e9-c0e197c71c8f"]
    }
  }
}
```

#### Authentication Modes

| Mode | Description | Azure Required |
| ---- | ----------- | -------------- |
| `ApiKey` | API key in `api-key` header | No |
| `EntraId` | Real Azure AD tokens | Yes |
| `Simulated` | Mock JWT tokens for local dev | No |

#### API Key Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `AdminApiKey` | Admin key override (falls back to SimulatorSettings) | `null` |
| `QueryApiKey` | Query key override (falls back to SimulatorSettings) | `null` |

#### Entra ID Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Instance` | Azure AD instance URL | `https://login.microsoftonline.com/` |
| `TenantId` | Your Azure AD tenant ID | *(required)* |
| `ClientId` | Application (client) ID | *(optional)* |
| `Audience` | Expected token audience | `https://search.azure.com` |
| `ValidIssuers` | List of valid token issuers | *(auto-derived from TenantId)* |
| `RequireHttpsMetadata` | Require HTTPS for metadata endpoints | `true` |
| `AllowMultipleTenants` | Accept tokens from any tenant | `false` |

**Sovereign Cloud Instances:**

| Cloud | Instance URL |
| ----- | ------------ |
| Azure Public | `https://login.microsoftonline.com/` |
| Azure Government (US) | `https://login.microsoftonline.us/` |
| Azure China | `https://login.chinacloudapi.cn/` |
| Azure Germany | `https://login.microsoftonline.de/` |

**Enabling Real Entra ID:**

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "EntraId"],
    "EntraId": {
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "your-tenant-id-here",
      "ClientId": "your-app-client-id-here",
      "Audience": "https://search.azure.com",
      "RequireHttpsMetadata": true,
      "AllowMultipleTenants": false
    }
  }
}
```

**Getting a Token with Azure CLI:**

```bash
# Get an access token for Azure AI Search
az account get-access-token --resource https://search.azure.com --query accessToken -o tsv
```

#### Simulated Token Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Enabled` | Enable simulated token authentication | `true` |
| `Issuer` | Issuer claim for generated tokens | `https://simulator.local/` |
| `Audience` | Audience claim for generated tokens | `https://search.azure.com` |
| `SigningKey` | HMAC key for signing tokens (min 32 chars) | *(default key)* |
| `TokenLifetimeMinutes` | Default token expiration | `60` |

**Enabling Simulated Tokens:**

```json
{
  "Authentication": {
    "EnabledModes": ["ApiKey", "Simulated"],
    "Simulated": {
      "Enabled": true,
      "SigningKey": "YourSecureSigningKey-AtLeast32Characters!"
    }
  }
}
```

#### Role Mapping

The simulator maps Azure RBAC roles to access levels. Default mappings include the role name and Azure role GUID.

| Role | Role GUID | Permissions |
| ---- | --------- | ----------- |
| Owner | `8e3af657-a8ff-443c-a75c-2fe8c4bcb635` | Full control |
| Contributor | `b24988ac-6180-42a0-ab88-20f7382dd24c` | Full control minus role assignment |
| Reader | `acdd72a7-3385-48ef-bd42-f606fba81ae7` | Read service info |
| Search Service Contributor | `7ca78c08-252a-4471-8644-bb5ff32d4ba0` | Manage indexes, indexers |
| Search Index Data Contributor | `8ebe5a00-799e-43f5-93ac-243d3dce84a7` | Upload/delete documents |
| Search Index Data Reader | `1407120a-92aa-4202-b7e9-c0e197c71c8f` | Query only |

**Environment variables:**

```bash
Authentication__EnabledModes__0=ApiKey
Authentication__ApiKey__AdminApiKey=custom-admin-key
Authentication__ApiKey__QueryApiKey=custom-query-key
```

### Outbound Authentication Settings

Configuration for authenticating to external Azure services (Blob Storage, ADLS Gen2, etc.).

```json
{
  "OutboundAuthentication": {
    "DefaultCredentialType": "DefaultAzureCredential",
    "ServicePrincipal": {
      "TenantId": "",
      "ClientId": "",
      "ClientSecret": null
    },
    "ManagedIdentity": {
      "Enabled": true,
      "ClientId": null,
      "ResourceId": null
    },
    "TokenCache": {
      "Enabled": true,
      "RefreshBeforeExpirationMinutes": 5,
      "MaxCacheSize": 100
    },
    "DefaultCredential": {
      "ExcludeInteractiveBrowserCredential": true
    }
  }
}
```

#### Credential Types

| Type | Description | Use Case |
| ---- | ----------- | -------- |
| `DefaultAzureCredential` | Tries multiple credential sources | Development, Azure deployments |
| `ServicePrincipal` | Client ID + secret/certificate | CI/CD, automation |
| `ManagedIdentity` | Azure-managed identity | Azure-hosted deployments |
| `ConnectionString` | Direct connection string | Legacy, simple setups |

#### Service Principal Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `TenantId` | Azure AD tenant ID | *(required)* |
| `ClientId` | Application (client) ID | *(required)* |
| `ClientSecret` | Client secret for authentication | *(optional)* |
| `CertificatePath` | Path to .pfx certificate file | *(optional)* |
| `CertificatePassword` | Certificate password | *(optional)* |
| `CertificateThumbprint` | Certificate thumbprint (Windows store) | *(optional)* |

#### Managed Identity Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Enabled` | Whether to use managed identity | `true` |
| `ClientId` | User-assigned identity client ID | *(null = system-assigned)* |
| `ResourceId` | User-assigned identity resource ID | *(optional)* |

#### Token Cache Settings

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Enabled` | Enable token caching | `true` |
| `RefreshBeforeExpirationMinutes` | Refresh buffer before expiration | `5` |
| `MaxCacheSize` | Maximum cached tokens | `100` |

**Environment variables:**

```bash
OutboundAuthentication__DefaultCredentialType=ServicePrincipal
OutboundAuthentication__ServicePrincipal__TenantId=your-tenant-id
OutboundAuthentication__ServicePrincipal__ClientId=your-client-id
OutboundAuthentication__ServicePrincipal__ClientSecret=your-secret
```

### Logging Settings (Serilog)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

## Docker Configuration

When running in Docker, use environment variables:

```yaml
# docker-compose.yml
services:
  search-simulator:
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Simulator__AdminApiKey=my-secure-admin-key
      - Simulator__QueryApiKey=my-secure-query-key
      - AzureOpenAI__ApiKey=${AZURE_OPENAI_API_KEY}
    volumes:
      - ./data:/app/data
      - ./lucene-indexes:/app/lucene-indexes
```

## HTTPS Configuration

The Azure SDK for .NET requires HTTPS. For local development with HTTPS:

### Option 1: Use Development Certificate (Recommended for Local Dev)

```bash
# Trust the ASP.NET Core development certificate
dotnet dev-certs https --trust

# Run with HTTPS profile
dotnet run --launch-profile https
```

The simulator listens on:

- `https://localhost:7250` (HTTPS - **recommended for Azure SDK**)
- `http://localhost:5250` (HTTP - for direct REST calls)

### Option 2: Configure in launchSettings.json

```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "applicationUrl": "https://localhost:7250;http://localhost:5250",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### Using Azure SDK with Local HTTPS

When using the Azure.Search.Documents SDK with the local simulator:

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = 
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
var options = new SearchClientOptions
{
    Transport = new Azure.Core.Pipeline.HttpClientTransport(handler)
};

var client = new SearchIndexClient(
    new Uri("https://localhost:7250"), 
    new AzureKeyCredential("admin-key-12345"),
    options);
```

## Security Considerations

### API Keys

1. **Change default keys** in production:

   ```bash
   Simulator__AdminApiKey=$(openssl rand -hex 32)
   Simulator__QueryApiKey=$(openssl rand -hex 32)
   ```

2. **Use separate keys** for admin and query operations
3. **Rotate keys** periodically

### Network Security

1. In production, run behind a reverse proxy (nginx, Traefik)
2. Enable HTTPS termination at the proxy level
3. Restrict access to trusted networks

### Data Protection

1. Persist data volumes in Docker for durability
2. Back up the `data/` and `lucene-indexes/` directories
3. Use encrypted storage for sensitive data

## Performance Tuning

### For Large Indexes

```json
{
  "Lucene": {
    "RAMBufferSizeMB": 64.0,
    "MaxMergeCount": 20
  },
  "Indexer": {
    "DefaultBatchSize": 5000
  }
}
```

### For High Query Volume

```json
{
  "VectorSearch": {
    "MaxK": 100
  }
}
```

### Memory Considerations

- Lucene indexes are memory-mapped; ensure adequate system memory
- Vector search loads vectors into memory; plan for dimensions × document count × 4 bytes
- LiteDB caches data; consider the `DataPath` on SSD storage

## Troubleshooting

### Enable Detailed Logging

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "AzureAISearchSimulator": "Debug"
      }
    }
  }
}
```

### Check Health Endpoint

```bash
# HTTPS (with certificate verification disabled)
curl -k https://localhost:7250/health

# HTTP
curl http://localhost:5250/health
```

### Verify Configuration

The simulator logs configuration values at startup (sensitive values are masked).

## Complete Example Configuration

```json
{
  "Simulator": {
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890",
    "DataPath": "./data",
    "EnableDetailedErrors": true
  },
  "Lucene": {
    "IndexPath": "./lucene-indexes",
    "MaxMergeCount": 10,
    "RAMBufferSizeMB": 16.0
  },
  "Indexer": {
    "DefaultBatchSize": 1000,
    "MaxFailedItems": -1,
    "MaxFailedItemsPerBatch": -1,
    "DefaultScheduleInterval": "PT1H"
  },
  "VectorSearch": {
    "DefaultK": 50,
    "MaxK": 1000,
    "DefaultDimensions": 1536,
    "SimilarityMetric": "cosine"
  },
  "AzureOpenAI": {
    "ApiKey": "",
    "DefaultModel": "text-embedding-ada-002",
    "DefaultDimensions": 1536,
    "Timeout": "00:01:00"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ]
  }
}
```
