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
  "VectorSearch": {
    "DefaultK": 50,
    "MaxK": 1000,
    "DefaultDimensions": 1536,
    "SimilarityMetric": "cosine"
  }
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `DefaultK` | Default number of nearest neighbors to return | `50` |
| `MaxK` | Maximum allowed K value | `1000` |
| `DefaultDimensions` | Default vector dimensions | `1536` |
| `SimilarityMetric` | Similarity metric (cosine, euclidean, dotProduct) | `cosine` |

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
