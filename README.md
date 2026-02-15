# Azure AI Search Simulator

A local simulator for Azure AI Search that allows developers to learn, experiment, and test Azure AI Search concepts without requiring an actual Azure subscription.

![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![License](https://img.shields.io/badge/license-MIT-blue)

## Overview

The Azure AI Search Simulator provides a local implementation of the Azure AI Search REST API, enabling you to:

- 🔍 **Learn** Azure AI Search concepts in a safe, cost-free environment
- 🧪 **Test** your search configurations before deploying to Azure
- 🚀 **Develop** search-powered applications without Azure dependencies
- 📚 **Experiment** with indexing pipelines and skillsets

## Features

### ✅ Implemented

- **Index Management**: Create, update, delete, and list search indexes
- **Document Operations**: Upload, merge, mergeOrUpload, and delete documents (Push model)
- **Full-Text Search**: Simple and Lucene query syntax
- **Filtering**: Basic OData filter expressions (eq, ne, gt, lt, ge, le, search.in)
- **Sorting & Paging**: OrderBy, top, skip support
- **Field Selection**: $select parameter support
- **Highlighting**: Search result highlighting
- **Faceted Navigation**: Value facets and interval/range facets
- **Autocomplete**: Term-based autocomplete
- **Suggestions**: Prefix-based suggestions
- **Vector Search**: Cosine similarity with `Collection(Edm.Single)` fields
- **Hybrid Search**: Combined text and vector search scoring
- **Authentication**: API keys, simulated JWT tokens, and Entra ID (Azure AD) support
- **Role-Based Access Control**: Full RBAC with 6 Azure Search roles
- **Managed Identity**: Resource-level identity for data sources, indexers, and skills
- **Storage**: LiteDB for index metadata, Lucene.NET for document indexing
- **Data Sources**: Azure Blob Storage, ADLS Gen2, and file system connectors
- **Indexers**: Automated document ingestion with field mappings and status tracking (Pull Mode)
- **Document Cracking**: Extract text/metadata from PDF, Word, Excel, HTML, JSON, CSV, TXT
- **Skillsets**: Skill pipeline with text transformation and embedding skills
- **Azure OpenAI Embedding Skill**: Generate vector embeddings via Azure OpenAI API
- **Custom Web API Skill**: Call external REST APIs for custom processing
- **Error Handling**: OData-compliant error responses
- **Docker Support**: Containerized deployment with docker-compose
- **Azure SDK Compatibility**: Works with official Azure.Search.Documents SDK
- **Search Debug**: Query diagnostics with subscore breakdown for hybrid/vector searches (`debug` parameter)

### 🔜 Planned (Future Phases)

- Scoring profiles
- Local embedding models
- Synonym maps
- Azure SQL / Cosmos DB connectors
- Admin UI dashboard

## Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 / VS Code / JetBrains Rider

### Installation

```bash
# Clone the repository
git clone https://github.com/your-org/azure-ai-search-simulator.git
cd azure-ai-search-simulator

# Build the solution
dotnet build

# Run the simulator (HTTPS - recommended for Azure SDK compatibility)
dotnet run --project src/AzureAISearchSimulator.Api --urls "https://localhost:7250"

# API available at https://localhost:7250
```

### Running with Docker

```bash
# Build and run with docker-compose
docker-compose up -d

# Or build the image manually
docker build -t azure-ai-search-simulator .
docker run -p 7250:8443 -p 5250:8080 -v search-data:/app/data azure-ai-search-simulator

# API available at https://localhost:7250 (HTTPS) or http://localhost:5250 (HTTP)
```

> **Note**: The Docker image generates a self-signed certificate for HTTPS. You'll need to skip certificate validation in your client (see Azure SDK example below).

### Using with Azure SDK

The simulator is compatible with the official **Azure.Search.Documents** SDK. Note that the SDK requires HTTPS.

```csharp
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

// Point to local simulator (HTTPS required)
var endpoint = new Uri("https://localhost:7250");
var credential = new AzureKeyCredential("admin-key-12345");

// Skip certificate validation for local development
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
var options = new SearchClientOptions
{
    Transport = new Azure.Core.Pipeline.HttpClientTransport(handler)
};

// Create clients
var indexClient = new SearchIndexClient(endpoint, credential, options);
var searchClient = new SearchClient(endpoint, "my-index", credential, options);

// Create an index
var index = new SearchIndex("my-index")
{
    Fields = new[]
    {
        new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
        new SearchableField("title") { IsFilterable = true },
        new SearchableField("content"),
        new SimpleField("rating", SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true }
    }
};

await indexClient.CreateIndexAsync(index);

// Upload documents
var documents = new[]
{
    new { id = "1", title = "Document One", content = "This is the first document", rating = 4.5 },
    new { id = "2", title = "Document Two", content = "This is the second document", rating = 3.8 }
};

await searchClient.UploadDocumentsAsync(documents);

// Search
var results = await searchClient.SearchAsync<SearchDocument>("first");
await foreach (var result in results.Value.GetResultsAsync())
{
    Console.WriteLine($"Found: {result.Document["title"]} (Score: {result.Score})");
}
```

### Using REST API

```http
### Create an index
POST https://localhost:7250/indexes?api-version=2024-07-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "hotels",
  "fields": [
    { "name": "hotelId", "type": "Edm.String", "key": true },
    { "name": "hotelName", "type": "Edm.String", "searchable": true },
    { "name": "description", "type": "Edm.String", "searchable": true },
    { "name": "rating", "type": "Edm.Double", "filterable": true, "sortable": true }
  ]
}

### Upload documents
POST https://localhost:7250/indexes/hotels/docs/index?api-version=2024-07-01
Content-Type: application/json
api-key: admin-key-12345

{
  "value": [
    {
      "@search.action": "upload",
      "hotelId": "1",
      "hotelName": "Fancy Hotel",
      "description": "A luxury hotel with great amenities",
      "rating": 4.8
    }
  ]
}

### Search
POST https://localhost:7250/indexes/hotels/docs/search?api-version=2024-07-01
Content-Type: application/json
api-key: query-key-67890

{
  "search": "luxury",
  "filter": "rating ge 4",
  "orderby": "rating desc",
  "top": 10
}

### Vector Search
POST https://localhost:7250/indexes/hotels/docs/search?api-version=2024-07-01
Content-Type: application/json
api-key: query-key-67890

{
  "vectorQueries": [
    {
      "kind": "vector",
      "vector": [0.01, 0.02, ...],
      "fields": "descriptionVector",
      "k": 10
    }
  ]
}

### Hybrid Search (Text + Vector)
POST https://localhost:7250/indexes/hotels/docs/search?api-version=2024-07-01
Content-Type: application/json
api-key: query-key-67890

{
  "search": "luxury hotel",
  "vectorQueries": [
    {
      "kind": "vector",
      "vector": [0.01, 0.02, ...],
      "fields": "descriptionVector",
      "k": 10
    }
  ]
}

### Create Data Source (Pull Model)
PUT https://localhost:7250/datasources/my-files?api-version=2024-07-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "my-files",
  "type": "filesystem",
  "credentials": {
    "connectionString": "c:\\data\\documents"
  },
  "container": {
    "name": "pdfs"
  }
}

### Create Indexer
PUT https://localhost:7250/indexers/my-indexer?api-version=2024-07-01
Content-Type: application/json
api-key: admin-key-12345

{
  "name": "my-indexer",
  "dataSourceName": "my-files",
  "targetIndexName": "documents",
  "fieldMappings": [
    {
      "sourceFieldName": "metadata_storage_path",
      "targetFieldName": "id"
    }
  ]
}

### Run Indexer
POST https://localhost:7250/indexers/my-indexer/run?api-version=2024-07-01
api-key: admin-key-12345
```

## Authentication

The simulator supports three authentication modes that can be enabled simultaneously:

### API Key (Default)

```http
api-key: admin-key-12345
```

### Simulated Tokens (Local Development)

Generate JWT tokens locally for testing RBAC without Azure:

```http
### Get a token with Search Index Data Contributor role
GET https://localhost:7250/admin/token/quick/data-contributor
api-key: admin-key-12345

### Use the token
GET https://localhost:7250/indexes
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Entra ID (Real Azure AD)

Validate real Azure AD tokens for production-like testing:

```csharp
var credential = new DefaultAzureCredential();
var searchClient = new SearchClient(endpoint, "my-index", credential);
```

### Role-Based Access Control

The simulator enforces Azure AI Search RBAC:

| Role | Permissions |
| ---- | ----------- |
| Search Service Contributor | Manage indexes, indexers, data sources, skillsets |
| Search Index Data Contributor | Upload, merge, delete documents |
| Search Index Data Reader | Search, suggest, autocomplete |

> 📚 See [docs/AUTHENTICATION.md](docs/AUTHENTICATION.md) for the complete authentication guide.

## Configuration

Edit `appsettings.json` to customize the simulator:

```json
{
  "SimulatorSettings": {
    "ServiceName": "local-search-simulator",
    "DataDirectory": "./data",
    "AdminApiKey": "admin-key-12345",
    "QueryApiKey": "query-key-67890",
    "MaxIndexes": 50,
    "MaxDocumentsPerIndex": 100000
  }
}
```

### Diagnostic Logging

Enable verbose logging to debug indexer and skill pipeline execution:

```json
{
  "DiagnosticLogging": {
    "Enabled": true,
    "LogDocumentDetails": true,
    "LogSkillExecution": true,
    "LogSkillInputPayloads": true,
    "LogSkillOutputPayloads": true,
    "LogEnrichedDocumentState": false,
    "LogFieldMappings": true,
    "MaxStringLogLength": 500
  }
}
```

Logs are written to `logs/simulator-{date}.log` and console. Look for `[DIAGNOSTIC]` prefixed entries.

## Documentation

- [Development Plan](docs/PLAN.md) - Full project plan and architecture
- [API Reference](docs/API-REFERENCE.md) - Complete REST API documentation
- [Configuration Guide](docs/CONFIGURATION.md) - Detailed configuration options
- [Authentication Guide](docs/AUTHENTICATION.md) - API keys, JWT tokens, Entra ID, and RBAC
- [Limitations](docs/LIMITATIONS.md) - Differences from Azure AI Search

## Samples

All `.http` sample files use environment variables via `$dotenv`. To get started:

1. Copy `.env.example` to `.env` in the workspace root
2. Fill in your values (API keys, storage credentials, etc.)
3. The `.env` file is gitignored and will not be committed

| Sample | Description |
| ------ | ----------- |
| [AzureSdkSample](samples/AzureSdkSample/) | C# console app demonstrating Azure.Search.Documents SDK compatibility |
| [AzureSearchNotebook](samples/AzureSearchNotebook/) | Python Jupyter notebook with comprehensive search demos and skillset integration |
| [IndexerTestNotebook](samples/IndexerTestNotebook/) | Python notebook for testing indexers with JSON metadata files |
| [EmbeddingSkillNotebook](samples/EmbeddingSkillNotebook/) | Python notebook demonstrating Azure OpenAI Embedding skill, vector search, and hybrid search with RRF fusion |
| [CustomSkillSample](samples/CustomSkillSample/) | ASP.NET Core API implementing custom Web API skills (text stats, keywords, sentiment, summarization) |
| [sample-requests.http](samples/sample-requests.http) | REST Client file with comprehensive API examples |
| [pull-mode-test.http](samples/pull-mode-test.http) | REST Client file for testing indexer pull mode workflow |

## Project Structure

```text
AzureAISearchSimulator/
├── src/
│   ├── AzureAISearchSimulator.Api/        # REST API layer
│   ├── AzureAISearchSimulator.Core/       # Business logic
│   ├── AzureAISearchSimulator.Search/     # Lucene.NET search engine & skills
│   ├── AzureAISearchSimulator.DataSources/# Data source connectors
│   └── AzureAISearchSimulator.Storage/    # Persistence layer
├── tests/
├── samples/
└── docs/
```

## Supported vs Azure AI Search

| Feature | Azure AI Search | Simulator |
| ------- | -------------- | --------- |
| Full-text search | ✅ | ✅ |
| Filtering & facets | ✅ | ✅ |
| Vector search | ✅ | ✅ (cosine similarity) |
| Hybrid search | ✅ | ✅ |
| Highlighting | ✅ | ✅ |
| Autocomplete | ✅ | ✅ |
| Suggestions | ✅ | ✅ |
| Indexers | ✅ | ✅ (Blob, ADLS, filesystem) |
| Skillsets (utility) | ✅ | ✅ |
| Custom Web API Skill | ✅ | ✅ |
| Azure OpenAI Embedding | ✅ | ✅ |
| Document Cracking | ✅ | ✅ |
| Semantic search | ✅ | ❌ |
| AI skills (OCR, etc.) | ✅ | ❌ |
| Managed Identity | ✅ | ✅ (simulated) |
| Entra ID Authentication | ✅ | ✅ |
| Scale (millions of docs) | ✅ | Limited |

### Skills Support

| Skill Category | Azure AI Search | Simulator |
| --- | --- | --- |
| **Utility Skills** (Split, Merge, Shaper, Conditional) | ✅ | ✅ |
| **Custom Web API Skill** | ✅ | ✅ |
| **Azure OpenAI Embedding Skill** | ✅ | ✅ |
| **AI Vision Skills** (OCR, Image Analysis) | ✅ | ❌ |
| **AI Language Skills** (Entity Recognition, Sentiment, PII, etc.) | ✅ | ❌ |
| **Translation Skill** | ✅ | ❌ |
| **GenAI Prompt Skill** | ✅ | ❌ |

> **Tip**: Use the Custom Web API Skill to implement your own versions of missing AI skills. See [samples/CustomSkillSample](samples/CustomSkillSample/) for examples.

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.md) file for details.

## Acknowledgments

- Built with [Lucene.NET](https://lucenenet.apache.org/)
- Inspired by [Azure AI Search](https://azure.microsoft.com/services/search/)
