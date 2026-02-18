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
  "SimulatorSettings": {
    "ServiceName": "local-search-simulator",
    "DataDirectory": "./data",
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890",
    "MaxIndexes": 50,
    "MaxDocumentsPerIndex": 100000,
    "MaxFieldsPerIndex": 1000,
    "DefaultPageSize": 50,
    "MaxPageSize": 1000
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `ServiceName` | Name of the simulated search service | `local-search-simulator` |
| `DataDirectory` | Path for LiteDB database files | `./data` |
| `AdminApiKey` | API key for admin operations (create, update, delete) | `admin-key-12345` |
| `QueryApiKey` | API key for query operations (search, suggest) | `query-key-67890` |
| `MaxIndexes` | Maximum number of indexes allowed | `50` |
| `MaxDocumentsPerIndex` | Maximum documents per index | `100000` |
| `MaxFieldsPerIndex` | Maximum fields per index | `1000` |
| `DefaultPageSize` | Default page size for list operations | `50` |
| `MaxPageSize` | Maximum page size for list operations | `1000` |

> **Note:** `MaxIndexes`, `MaxDocumentsPerIndex`, `MaxFieldsPerIndex`, and `MaxPageSize` are defined in configuration but **not yet enforced** by the API. They are reserved for a future update.

**Environment variables:**

```bash
SimulatorSettings__AdminApiKey=your-admin-key
SimulatorSettings__QueryApiKey=your-query-key
SimulatorSettings__DataDirectory=/app/data
```

### Lucene Settings

```json
{
  "LuceneSettings": {
    "IndexPath": "./data/lucene",
    "CommitIntervalSeconds": 5,
    "MaxBufferedDocs": 1000,
    "RamBufferSizeMB": 256.0
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `IndexPath` | Directory for Lucene index files | `./data/lucene` |
| `CommitIntervalSeconds` | Interval between automatic index commits | `5` |
| `MaxBufferedDocs` | Maximum buffered documents before flush | `1000` |
| `RamBufferSizeMB` | RAM buffer size for indexing (MB) | `256.0` |

**Environment variables:**

```bash
LuceneSettings__IndexPath=/app/lucene-indexes
LuceneSettings__RamBufferSizeMB=512.0
```

### Indexer Settings

```json
{
  "IndexerSettings": {
    "MaxConcurrentIndexers": 3,
    "DefaultBatchSize": 1000,
    "EnableScheduler": true,
    "DefaultTimeoutMinutes": 60
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `MaxConcurrentIndexers` | Maximum indexers running concurrently | `3` |
| `DefaultBatchSize` | Default batch size for indexer operations | `1000` |
| `EnableScheduler` | Enable background indexer scheduler | `true` |
| `DefaultTimeoutMinutes` | Default timeout for indexer runs (minutes) | `60` |

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
  "AzureOpenAISettings": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "",
    "DeploymentName": "text-embedding-ada-002",
    "ModelDimensions": 1536,
    "TimeoutSeconds": 30
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Endpoint` | Azure OpenAI resource endpoint URL | *(required)* |
| `ApiKey` | Azure OpenAI API key for embedding skill | *(empty)* |
| `DeploymentName` | Embedding model deployment name | `text-embedding-ada-002` |
| `ModelDimensions` | Embedding model output dimensions | `1536` |
| `TimeoutSeconds` | HTTP timeout for API calls (seconds) | `30` |

**Environment variables:**

```bash
AzureOpenAISettings__Endpoint=https://your-resource.openai.azure.com/
AzureOpenAISettings__ApiKey=your-azure-openai-key
AzureOpenAISettings__DeploymentName=text-embedding-3-small
```

### Local Embedding Settings

Configure local ONNX-based embedding models as an alternative to Azure OpenAI. Use the `local://model-name` URI scheme in skillset definitions to run embeddings locally.

```json
{
  "LocalEmbeddingSettings": {
    "ModelsDirectory": "./data/models",
    "DefaultModel": "all-MiniLM-L6-v2",
    "MaximumTokens": 512,
    "NormalizeEmbeddings": true,
    "PoolingMode": "Mean",
    "AutoDownloadModels": false,
    "CaseSensitive": false
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `ModelsDirectory` | Directory containing ONNX model subdirectories (each with `model.onnx` + `vocab.txt`) | `./data/models` |
| `DefaultModel` | Model used when `local://` URI has no model name | `all-MiniLM-L6-v2` |
| `MaximumTokens` | Maximum token count passed to the BERT tokenizer (truncates longer input) | `512` |
| `NormalizeEmbeddings` | L2-normalize output vectors (recommended for cosine similarity) | `true` |
| `PoolingMode` | Token aggregation strategy: `Mean` or `Max` | `Mean` |
| `AutoDownloadModels` | Auto-download models from HuggingFace when not found locally | `false` |
| `CaseSensitive` | Whether the tokenizer treats input as case-sensitive | `false` |

**Supported models:** `all-MiniLM-L6-v2` (384d), `bge-small-en-v1.5` (384d), `all-mpnet-base-v2` (768d).

**Download models:**

```powershell
.\scripts\Download-EmbeddingModel.ps1                          # default: all-MiniLM-L6-v2
.\scripts\Download-EmbeddingModel.ps1 -ModelName bge-small-en-v1.5
```

**Environment variables:**

```bash
LocalEmbeddingSettings__ModelsDirectory=./data/models
LocalEmbeddingSettings__DefaultModel=all-MiniLM-L6-v2
LocalEmbeddingSettings__AutoDownloadModels=true
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

### Diagnostic Logging Settings

Controls verbose diagnostic logging during indexing and skill execution. Useful for debugging the processing pipeline.

```json
{
  "DiagnosticLogging": {
    "Enabled": false,
    "LogDocumentDetails": true,
    "LogSkillExecution": true,
    "LogSkillInputPayloads": false,
    "LogSkillOutputPayloads": false,
    "LogEnrichedDocumentState": false,
    "LogFieldMappings": true,
    "MaxStringLogLength": 500,
    "IncludeTimings": true
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `Enabled` | Enable the diagnostic logging subsystem (when false, all other settings are ignored) | `false` |
| `LogDocumentDetails` | Log document key, content type, metadata, and processing status | `true` |
| `LogSkillExecution` | Log skill invocations and execution results | `true` |
| `LogSkillInputPayloads` | Log input payloads passed to skills (⚠️ verbose) | `false` |
| `LogSkillOutputPayloads` | Log output payloads produced by skills (⚠️ verbose) | `false` |
| `LogEnrichedDocumentState` | Log complete enriched document state after each skill (⚠️ very verbose) | `false` |
| `LogFieldMappings` | Log field mappings applied during indexing | `true` |
| `MaxStringLogLength` | Maximum string length before truncation (0 = no truncation) | `500` |
| `IncludeTimings` | Include timing information for each operation | `true` |

**Environment variables:**

```bash
DiagnosticLogging__Enabled=true
DiagnosticLogging__LogSkillInputPayloads=true
```

## Docker Configuration

When running in Docker, use environment variables to override settings:

```yaml
# docker-compose.yml
services:
  search-simulator:
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - SimulatorSettings__AdminApiKey=my-secure-admin-key
      - SimulatorSettings__QueryApiKey=my-secure-query-key
      - SimulatorSettings__DataDirectory=/app/data
      - LuceneSettings__IndexPath=/app/lucene-indexes
      - Serilog__WriteTo__1__Args__path=/app/logs/simulator-.log
      - AzureOpenAISettings__ApiKey=${AZURE_OPENAI_API_KEY}
    volumes:
      - ./data:/app/data
      - ./lucene-indexes:/app/lucene-indexes
      - ./logs:/app/logs
      - ./files:/app/files
```

### Docker Volume Mapping

The container declares four volumes for data persistence and file access:

| Container Path | Purpose | Type | Description |
| -------------- | ------- | ---- | ----------- |
| `/app/data` | Database | Named volume or bind mount | LiteDB database files (index metadata, data sources, indexer state) |
| `/app/lucene-indexes` | Search indexes | Named volume or bind mount | Lucene.NET index files for full-text and vector search |
| `/app/logs` | Log files | Bind mount (recommended) | Serilog log files (`simulator-{date}.log`) for debugging and diagnostics |
| `/app/files` | File processing | Bind mount (recommended) | Documents for indexer pull-mode file processing (PDF, Word, JSON, etc.) |

**Named volumes** (managed by Docker) are ideal for `/app/data` and `/app/lucene-indexes` as they persist automatically and are not tied to a host path.

**Bind mounts** are recommended for `/app/logs` and `/app/files` so you can easily access logs and manage documents from the host.

#### Example: File Processing with Docker

To use the indexer pull mode with documents on your host machine, bind mount your documents folder to `/app/files`:

```bash
docker run -p 7250:8443 -p 5250:8080 \
  -v search-data:/app/data \
  -v lucene-indexes:/app/lucene-indexes \
  -v ./logs:/app/logs \
  -v /path/to/your/documents:/app/files \
  azure-ai-search-simulator
```

Then create a filesystem data source pointing to the container path:

```http
PUT https://localhost:7250/datasources/my-docs?api-version=2024-07-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "my-docs",
  "type": "filesystem",
  "credentials": { "connectionString": "/app/files" },
  "container": { "name": "subfolder" }
}
```

#### Example: Accessing Logs

Mount the logs directory to inspect simulator logs from the host:

```bash
docker run -p 7250:8443 \
  -v ./simulator-logs:/app/logs \
  azure-ai-search-simulator

# View today's log
cat ./simulator-logs/simulator-$(date +%Y%m%d).log
```

#### Environment Variable Reference

| Environment Variable | Maps To | Default |
| -------------------- | ------- | ------- |
| `SimulatorSettings__DataDirectory` | `SimulatorSettings.DataDirectory` | `/app/data` |
| `SimulatorSettings__AdminApiKey` | `SimulatorSettings.AdminApiKey` | `admin-key-12345` |
| `SimulatorSettings__QueryApiKey` | `SimulatorSettings.QueryApiKey` | `query-key-67890` |
| `LuceneSettings__IndexPath` | `LuceneSettings.IndexPath` | `/app/lucene-indexes` |
| `Serilog__WriteTo__1__Args__path` | Serilog file sink path | `/app/logs/simulator-.log` |
| `AzureOpenAISettings__ApiKey` | `AzureOpenAISettings.ApiKey` | *(empty)* |

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
   SimulatorSettings__AdminApiKey=$(openssl rand -hex 32)
   SimulatorSettings__QueryApiKey=$(openssl rand -hex 32)
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
  "LuceneSettings": {
    "RamBufferSizeMB": 512.0,
    "MaxBufferedDocs": 5000
  },
  "IndexerSettings": {
    "DefaultBatchSize": 5000
  }
}
```

### For High Query Volume

```json
{
  "VectorSearchSettings": {
    "MaxVectorsPerIndex": 200000
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
  "SimulatorSettings": {
    "ServiceName": "local-search-simulator",
    "DataDirectory": "./data",
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890",
    "MaxIndexes": 50,
    "MaxDocumentsPerIndex": 100000,
    "MaxFieldsPerIndex": 1000,
    "DefaultPageSize": 50,
    "MaxPageSize": 1000
  },
  "LuceneSettings": {
    "IndexPath": "./data/lucene",
    "CommitIntervalSeconds": 5,
    "MaxBufferedDocs": 1000,
    "RamBufferSizeMB": 256.0
  },
  "IndexerSettings": {
    "MaxConcurrentIndexers": 3,
    "DefaultBatchSize": 1000,
    "EnableScheduler": true,
    "DefaultTimeoutMinutes": 60
  },
  "VectorSearchSettings": {
    "DefaultDimensions": 1536,
    "MaxVectorsPerIndex": 50000,
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
  },
  "AzureOpenAISettings": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "",
    "DeploymentName": "text-embedding-ada-002",
    "ModelDimensions": 1536,
    "TimeoutSeconds": 30
  },
  "DiagnosticLogging": {
    "Enabled": false,
    "LogDocumentDetails": true,
    "LogSkillExecution": true,
    "LogSkillInputPayloads": false,
    "LogSkillOutputPayloads": false,
    "LogEnrichedDocumentState": false,
    "LogFieldMappings": true,
    "MaxStringLogLength": 500,
    "IncludeTimings": true
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
