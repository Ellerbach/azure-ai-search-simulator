# Azure AI Search Simulator - Development Plan

## Executive Summary

This document outlines the comprehensive plan for building an Azure AI Search Simulator using C# and .NET. The simulator will allow developers to learn, experiment, and test Azure AI Search concepts locally without requiring an actual Azure subscription or incurring any costs.

## Implementation Status

| Phase | Status | Description |
| ----- | ------ | ----------- |
| Phase 1: Foundation | ✅ Complete | API infrastructure, authentication, index management, Lucene setup |
| Phase 2: Document & Search | ✅ Complete | Document CRUD, full-text search, vector search, hybrid search, facets |
| Phase 3: Pull Model | ✅ Complete | Indexers, data sources (file system, Azure Blob, ADLS Gen2) |
| Phase 4: Document Cracking | ✅ Complete | PDF, Word, Excel, HTML, JSON, CSV, plain text extraction |
| Phase 5: Skillsets | ✅ Complete | Text skills, embedding skills, custom WebApiSkill, skill pipeline |
| Phase 6: Polish & Docs | ✅ Complete | Error handling, Docker support, SDK samples, documentation |
| Phase 7: HNSW Vector Search | ✅ Complete | HNSWlib.NET integration, filtered vector search, hybrid ranking |
| Phase 8: API 2025-09-01 | 🔄 In Progress | Index description ✅, debug subscores ✅, normalizers ✅ — remaining: truncated dimensions, rescoring |
| Phase 9: Local Embeddings | ✅ Complete | ONNX Runtime local embedding models via `local://` URI, Microsoft.ML.Tokenizers, download script |

## 1. Project Overview

### 1.1 Goals

- Create a local simulator that mimics Azure AI Search REST APIs
- Support both **Push** and **Pull** indexing models
- Implement core cognitive skills (document cracking, text extraction, basic transformations)
- Provide a compatible API surface for testing and learning
- Run entirely locally without Azure dependencies

### 1.2 Scope

#### Implemented ✅

- Index management (create, update, delete, list)
- Document operations (upload, merge, mergeOrUpload, delete)
- Full-text search with simple and Lucene query syntax
- Basic OData filtering
- Sorting and paging
- Field selection ($select)
- Search highlighting
- Autocomplete and suggestions
- Vector search with cosine similarity
- Hybrid search (text + vector)
- API key authentication (admin and query keys)
- Data sources (file system, Azure Blob Storage, ADLS Gen2)
- Indexers with field mappings
- Indexer execution and status tracking
- Change detection based on file timestamps
- Document cracking (PDF, Word, Excel, HTML, JSON, CSV, plain text)
- Automatic metadata extraction (title, author, page count, word count)
- Skillsets with skill pipeline execution
- Text skills (TextSplitSkill, TextMergeSkill, ShaperSkill, ConditionalSkill)
- Document Extraction Skill (base64/URL file_data, content-type detection, parsingMode support)
- Azure OpenAI Embedding Skill
- Local ONNX Embedding Models (via `local://` URI in AzureOpenAIEmbeddingSkill)
- Custom Web API Skill
- Output field mappings for enriched content
- Index projections (one-to-many fan-out to secondary indexes)
- Facets (count and value facets)
- Azure SDK compatibility (Azure.Search.Documents)
- Docker support with multi-stage build
- Synonym maps (CRUD management, Solr format, query-time expansion)\n- Scheduled indexer runs (BackgroundService-based scheduler)

#### In Progress 🔄

- **API version 2025-09-01 features:**
  - ✅ Index description property
  - ✅ Search debug parameter (query subscores)
  - ✅ Normalizers for case-insensitive filtering
  - Truncated dimensions (MRL)
  - Rescoring options
- ~~Scoring profiles~~ ✅ Implemented

#### Future Phases

- ~~Local embedding models (ONNX Runtime — Phase 9)~~ ✅ Implemented
- OneLake indexer (2025-09-01)
- Document Layout skill (2025-09-01)
- Semantic search/ranking
- Azure-hosted AI skills (OCR, Entity Recognition, etc.)
- Knowledge stores
- ~~Synonym maps~~ ✅ Implemented
- Debug sessions

---

## 2. Architecture Overview

### 2.1 High-Level Architecture

```text
┌─────────────────────────────────────────────────────────────────┐
│                        Client Applications                      │
│              (SDK, REST API Clients, Postman, etc.)             │
└─────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ASP.NET Core Web API                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Index APIs     │  │  Document APIs  │  │  Indexer APIs   │  │
│  │  /indexes/*     │  │  /indexes/*/    │  │  /indexers/*    │  │
│  │                 │  │   docs/*        │  │  /datasources/* │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Skillset APIs  │  │  Admin APIs     │  │  Service Stats  │  │
│  │  /skillsets/*   │  │  /servicestats  │  │                 │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Core Services Layer                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Index Service  │  │  Search Engine  │  │ Indexer Service │  │
│  │                 │  │  (Lucene.NET)   │  │                 │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │ Skillset Engine │  │ Data Source Mgr │  │ Security Manager│  │
│  │                 │  │                 │  │                 │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │              Vector Search Engine (HNSWlib.NET)             ││
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  ││
│  │  │ HNSW Index  │  │ Filter Exec │  │ Hybrid Score Fusion │  ││
│  │  │  Manager    │  │   Engine    │  │                     │  ││
│  │  └─────────────┘  └─────────────┘  └─────────────────────┘  ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Storage Layer                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │  Lucene Index   │  │   Metadata DB   │  │  Configuration  │  │
│  │   (File-based)  │  │   (LiteDB/JSON) │  │   (JSON files)  │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Technology Stack

| Component | Technology | Rationale |
| --------- | ---------- | --------- |
| Web Framework | ASP.NET Core 10.0 | Modern, cross-platform, high performance |
| Search Engine | Lucene.NET | Industry-standard full-text search (same as Azure Search) |
| Vector Search | HNSWlib.NET | High-performance HNSW algorithm for ANN search |
| Metadata Storage | LiteDB | Embedded NoSQL database, no setup required |
| PDF Extraction | PdfPig | Free, open-source PDF text extraction |
| Office Docs | OpenXML SDK | Microsoft's free library for Office formats |
| Scheduling | Quartz.NET | Indexer scheduling |
| Logging | Serilog | Structured logging |
| Testing | xUnit + Moq | Standard .NET testing stack |

---

## 3. Core Components

### 3.1 Index Management ✅ COMPLETE

#### Features

- Create, update, delete search indexes
- Support all field types:
  - `Edm.String` - Text/string values
  - `Edm.Int32` - 32-bit integers
  - `Edm.Int64` - 64-bit integers
  - `Edm.Double` - Double-precision floats
  - `Edm.Boolean` - True/false values
  - `Edm.DateTimeOffset` - Date and time values
  - `Edm.GeographyPoint` - Geographic coordinates
  - `Collection(Edm.String)` - String arrays
  - `Collection(Edm.Single)` - Vector embeddings
  - `Edm.ComplexType` - Nested objects
- **Vector field support**: `Collection(Edm.Single)` with `dimensions` and `vectorSearchProfile` properties
- Field attributes: `searchable`, `filterable`, `sortable`, `facetable`, `retrievable`, `key`
- **Vector search configuration**: algorithms (HNSW placeholder) and profiles
- **Suggesters**: Configure autocomplete and suggestions with `analyzingInfixMatching`
- Text analysis endpoint (basic tokenization)
- Azure SDK compatibility (OData entity syntax routes)
- ETag support for optimistic concurrency

#### API Endpoints

```http
POST   /indexes                      - Create index
GET    /indexes                      - List indexes
GET    /indexes/{indexName}          - Get index
GET    /indexes('{indexName}')       - Get index (OData syntax)
PUT    /indexes/{indexName}          - Create or update index
PUT    /indexes('{indexName}')       - Create or update (OData syntax)
DELETE /indexes/{indexName}          - Delete index
DELETE /indexes('{indexName}')       - Delete index (OData syntax)
POST   /indexes/{indexName}/analyze  - Analyze text
```

### 3.2 Document Operations (Push Model)

#### Features

- Upload, merge, mergeOrUpload, delete actions
- Batch operations
- Document key validation
- Field type validation

#### API Endpoints

```
POST   /indexes/{indexName}/docs/index   - Index documents
GET    /indexes/{indexName}/docs/{key}   - Get document by key
GET    /indexes/{indexName}/docs/$count  - Count documents
```

### 3.3 Search & Query

#### Features

- Simple query syntax
- Full Lucene query syntax
- Filtering with OData expressions
- Sorting and paging
- Facets
- Highlighting
- Autocomplete
- Suggestions

#### API Endpoints

```http
POST   /indexes/{indexName}/docs/search     - Search documents
GET    /indexes/{indexName}/docs/search     - Search documents (GET)
POST   /indexes/{indexName}/docs/suggest    - Suggestions
POST   /indexes/{indexName}/docs/autocomplete - Autocomplete
```

#### Vector Search Support

- **Vector queries**: Use `vectorQueries` parameter in POST body
- **Hybrid search**: Combine `search` text query with `vectorQueries`
- **Vector fields**: Type `Collection(Edm.Single)` with `dimensions` property
- **Algorithm**: HNSW (Hierarchical Navigable Small World) via HNSWlib.NET
- **Fallback**: Simple brute-force cosine similarity for small datasets
- **Top-K**: Specify `k` parameter for number of nearest neighbors
- **Filtered Search**: Post-filter pattern with oversampling for accurate results
- **Hybrid Ranking**: Configurable score fusion (RRF or weighted combination)

### 3.4 Indexers (Pull Model)

#### Features

- Scheduled execution (every X minutes)
- On-demand execution
- Change detection (for supported sources)
- Document cracking (PDF, Office docs, JSON, CSV)
- Field mappings
- Output field mappings (for skillsets)

#### API Endpoints

```http
POST   /indexers                    - Create indexer
GET    /indexers                    - List indexers
GET    /indexers/{indexerName}      - Get indexer
PUT    /indexers/{indexerName}      - Create or update indexer
DELETE /indexers/{indexerName}      - Delete indexer
POST   /indexers/{indexerName}/run  - Run indexer
POST   /indexers/{indexerName}/reset - Reset indexer
GET    /indexers/{indexerName}/status - Get indexer status
```

### 3.5 Data Sources

#### Features

- Local file system connector (for development/testing)
- Azure Blob Storage connector (with connection string, SAS, and Managed Identity support)
- Azure Data Lake Storage Gen2 connector (with hierarchical namespace support)
- Container and folder path configuration
- Soft delete detection (metadata-based)

#### API Endpoints

```http
POST   /datasources                    - Create data source
GET    /datasources                    - List data sources
GET    /datasources/{dataSourceName}   - Get data source
PUT    /datasources/{dataSourceName}   - Create or update
DELETE /datasources/{dataSourceName}   - Delete data source
```

### 3.6 Skillsets

#### Features

- Utility skills (Text Merge, Text Split, Conditional, Shaper)
- Document Extraction skill
- Custom Web API skill (call external endpoints)
- **Azure OpenAI Embedding skill** (generate vector embeddings)
- **Azure OpenAI Embedding skill — local mode** (same skill, runs locally via ONNX when `resourceUri` = `local://model-name`)
- Skill input/output mappings

#### Built-in Skills to Implement

| Skill | Description | Implementation |
| ----- | ----------- | -------------- |
| Text Split | Split text into chunks/pages | String operations |
| Text Merge | Merge multiple text fields | String concatenation |
| Conditional | Filter/transform based on conditions | Expression evaluation |
| Shaper | Reshape data structure | JSON transformation |
| Document Extraction | Extract content from files | PdfPig, OpenXML |
| Custom Web API | Call external HTTP endpoints | HttpClient |
| **AzureOpenAIEmbedding** | Generate vector embeddings | Azure.AI.OpenAI SDK |
| **AzureOpenAIEmbedding (local)** | Local vector embeddings | ONNX Runtime + BERT models (triggered by `local://` resourceUri) |

#### API Endpoints

```http
POST   /skillsets                    - Create skillset
GET    /skillsets                    - List skillsets
GET    /skillsets/{skillsetName}     - Get skillset
PUT    /skillsets/{skillsetName}     - Create or update
DELETE /skillsets/{skillsetName}     - Delete skillset
```

### 3.7 Security

#### Features

- API Key authentication (Admin and Query keys)
- Key generation and rotation
- CORS configuration (optional)

#### Implementation

- Admin Key: Full access to all operations
- Query Key: Read-only access to search operations
- Keys stored in configuration/LiteDB

---

## 4. Project Structure

```text
AzureAISearchSimulator/
├── src/
│   ├── AzureAISearchSimulator.Api/           # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   │   ├── IndexesController.cs
│   │   │   ├── DocumentsController.cs
│   │   │   ├── IndexersController.cs
│   │   │   ├── DataSourcesController.cs
│   │   │   ├── SkillsetsController.cs
│   │   │   └── ServiceStatsController.cs
│   │   ├── Middleware/
│   │   │   ├── ApiKeyAuthenticationMiddleware.cs
│   │   │   └── ExceptionHandlingMiddleware.cs
│   │   ├── Models/
│   │   │   └── ApiModels/                   # Request/Response DTOs
│   │   └── Program.cs
│   │
│   ├── AzureAISearchSimulator.Core/          # Core business logic
│   │   ├── Interfaces/
│   │   │   ├── IIndexService.cs
│   │   │   ├── ISearchService.cs
│   │   │   ├── IIndexerService.cs
│   │   │   ├── IDataSourceService.cs
│   │   │   └── ISkillsetService.cs
│   │   ├── Services/
│   │   │   ├── IndexService.cs
│   │   │   ├── SearchService.cs
│   │   │   ├── IndexerService.cs
│   │   │   ├── DataSourceService.cs
│   │   │   └── SkillsetService.cs
│   │   └── Models/
│   │       ├── SearchIndex.cs
│   │       ├── SearchField.cs
│   │       ├── SearchIndexer.cs
│   │       ├── DataSource.cs
│   │       ├── Skillset.cs
│   │       └── ...
│   │
│   ├── AzureAISearchSimulator.Search/        # Lucene.NET integration
│   │   ├── LuceneIndexManager.cs
│   │   ├── LuceneSearchEngine.cs
│   │   ├── QueryParsers/
│   │   │   ├── SimpleQueryParser.cs
│   │   │   └── FullQueryParser.cs
│   │   ├── Analyzers/
│   │   │   └── AnalyzerFactory.cs
│   │   ├── Filters/
│   │   │   └── ODataFilterParser.cs
│   │   ├── DocumentCracking/
│   │   │   ├── IDocumentCracker.cs
│   │   │   ├── PdfCracker.cs
│   │   │   ├── OfficeDocCracker.cs
│   │   │   ├── JsonCracker.cs
│   │   │   └── PlainTextCracker.cs
│   │   └── Skills/
│   │       ├── ISkillExecutor.cs
│   │       ├── SkillPipeline.cs
│   │       ├── TextSplitSkillExecutor.cs
│   │       ├── TextMergeSkillExecutor.cs
│   │       ├── ShaperSkillExecutor.cs
│   │       ├── ConditionalSkillExecutor.cs
│   │       ├── AzureOpenAIEmbeddingSkillExecutor.cs
│   │       └── CustomWebApiSkillExecutor.cs
│   │
│   ├── AzureAISearchSimulator.DataSources/   # Data source connectors
│   │   ├── IDataSourceConnector.cs
│   │   ├── LocalBlobStorageConnector.cs      # File system as blob storage
│   │   └── ChangeDetection/
│   │       └── FileChangeTracker.cs
│   │
│   └── AzureAISearchSimulator.Storage/       # Persistence layer
│       ├── IMetadataStore.cs
│       ├── LiteDbMetadataStore.cs
│       └── Entities/
│           ├── IndexEntity.cs
│           ├── IndexerEntity.cs
│           └── ...
│
├── tests/
│   ├── AzureAISearchSimulator.Api.Tests/
│   ├── AzureAISearchSimulator.Core.Tests/
│   ├── AzureAISearchSimulator.Search.Tests/
│   └── AzureAISearchSimulator.Integration.Tests/
│
├── samples/
│   ├── data/                                  # Sample documents
│   │   ├── pdfs/
│   │   ├── office/
│   │   └── json/
│   └── scripts/
│       ├── create-index.http
│       ├── upload-documents.http
│       └── run-queries.http
│
├── docs/
│   ├── PLAN.md                               # This document
│   ├── API-REFERENCE.md
│   ├── CONFIGURATION.md
│   └── LIMITATIONS.md
│
├── AzureAISearchSimulator.sln
├── README.md
├── docker-compose.yml                         # Optional Docker support
└── .github/
    └── workflows/
        └── build.yml
```

---

## 5. Implementation Phases

### Phase 1: Foundation (Week 1-2) ✅ COMPLETED

**Goal**: Basic infrastructure and index management

#### Tasks

1. [x] Set up solution structure with all projects
2. [x] Create ASP.NET Core Web API project
3. [x] Implement API key authentication middleware
4. [x] Implement Index CRUD operations
5. [x] Set up LiteDB for metadata storage
6. [x] Create basic Lucene.NET index management
7. [x] Add logging with Serilog
8. [x] Write unit tests for index operations

#### Deliverables

- Working API server
- Index management APIs
- Authentication working

### Phase 2: Document Operations & Search (Week 3-4) ✅ COMPLETED

**Goal**: Push model, search functionality, and vector search

#### Tasks

1. [x] Implement document upload/merge/delete operations
2. [x] Create Lucene document mapping
3. [x] Implement simple query syntax parser
4. [x] Implement full Lucene query syntax
5. [x] Add OData filter expression parser
6. [x] Implement sorting, paging, and facets
7. [x] Add highlighting support
8. [x] Implement autocomplete and suggestions
9. [x] **Implement in-memory vector storage**
10. [x] **Implement cosine similarity search**
11. [x] **Implement hybrid search (text + vector)**
12. [x] Write comprehensive search tests

#### Deliverables

- Full document operations
- Working search with all features
- **Vector search with cosine similarity**
- **Hybrid search capability**

### Phase 3: Pull Model - Indexers & Data Sources (Week 5-6) ✅ COMPLETED

**Goal**: Automated indexing from data sources

#### Tasks

1. [x] Implement data source management APIs
2. [x] Create local file system connector (blob storage simulator)
3. [x] Implement Azure Blob Storage connector
4. [x] Implement ADLS Gen2 connector
5. [x] Implement indexer management APIs
6. [x] Create indexer execution engine
7. [x] Implement field mappings
8. [x] Add change detection
9. [x] Implement indexer status tracking
10. [x] Write integration tests

#### Deliverables

- Working indexers
- Multiple data source connectors (file system, Azure Blob, ADLS Gen2)
- Change tracking

### Phase 4: Document Cracking (Week 7) ✅ COMPLETED

**Goal**: Extract content from various file formats

#### Tasks

1. [x] Implement PDF text extraction with PdfPig
2. [x] Implement Office document extraction (Word, Excel)
3. [x] Add JSON document parsing
4. [x] Add CSV document parsing
5. [x] Add plain text handling
6. [x] Add HTML parsing with HtmlAgilityPack
7. [x] Create unified document cracking interface
8. [x] Handle metadata extraction
9. [x] Write format-specific tests

#### Deliverables

- Multi-format document support (PDF, Word, Excel, JSON, CSV, HTML, plain text)
- Metadata extraction

### Phase 5: Skillsets (Week 8-9) ✅ COMPLETED

**Goal**: Implement cognitive skills pipeline including embedding generation

#### Tasks

1. [x] Create skillset management APIs
2. [x] Design skill execution pipeline
3. [x] Implement Text Split skill
4. [x] Implement Text Merge skill
5. [x] Implement Conditional skill
6. [x] Implement Shaper skill
7. [x] Implement Custom Web API skill
8. [x] **Implement Azure OpenAI Embedding skill**
9. [x] Implement Document Extraction skill (base64/URL file_data, content-type detection, document cracking)
10. [x] Create output field mapping processor
11. [x] Write skill tests

#### Deliverables

- Working skillsets
- All utility skills implemented
- **Azure OpenAI Embedding skill for vector generation**

### Phase 6: Polish & Documentation (Week 10) ✅ COMPLETED

**Goal**: Production readiness

#### Tasks

1. [x] Add comprehensive error handling
2. [x] Implement proper OData error responses
3. [x] Add request/response validation
4. [x] Create API documentation (API-REFERENCE.md)
5. [x] Write configuration guide (CONFIGURATION.md)
6. [x] Document limitations vs real Azure AI Search (LIMITATIONS.md)
7. [x] Create sample projects (AzureSdkSample, CustomSkillSample)
8. [x] Create Docker support (Dockerfile, docker-compose.yml)

#### Deliverables

- Complete documentation
- Sample applications
- Docker deployment option

### Phase 7: HNSW Vector Search (Week 11-12) ✅ COMPLETED

**Goal**: Replace brute-force vector search with efficient HNSW algorithm using HNSWlib.NET

#### Architecture Overview

The HNSW implementation follows a dual-storage pattern:

- **Lucene.NET**: Stores document metadata (id, content, category, tags, dates, etc.)
- **HNSWlib.NET**: Stores document id → vector mappings for fast ANN search

```text
┌─────────────────────────────────────────────────────────────────────┐
│                     Vector Search Flow                              │
├─────────────────────────────────────────────────────────────────────┤
│  1. Query embedding → HNSWlib.NET → Top-K × 5 candidate IDs         │
│  2. Candidate IDs → Lucene.NET → Fetch metadata                     │
│  3. Apply filters (category, tags, dates, etc.)                     │
│  4. Re-rank filtered results (optional hybrid scoring)              │
│  5. Return top-K final results                                      │
└─────────────────────────────────────────────────────────────────────┘
```

#### Tasks

1. [x] Add HNSWlib.NET NuGet package
2. [x] Create `IHnswIndexManager` interface
3. [x] Implement `HnswIndexManager` class
   - [x] Index lifecycle management (create, open, close, delete)
   - [x] Persist HNSW index to disk alongside Lucene index
   - [x] Support multiple vector fields per index
4. [x] Create `IVectorSearchService` interface
5. [x] Implement `HnswVectorSearchService`
   - [x] Basic KNN search
   - [x] Oversampling for filtered queries (K × multiplier)
   - [x] Distance-to-score conversion (cosine → similarity score)
6. [x] Implement filtered vector search
   - [x] Post-filter pattern: Vector search → Metadata fetch → Filter → Return
   - [x] Configurable oversampling multiplier
   - [x] Support all Lucene-stored metadata fields as filters
7. [x] Implement hybrid search with score fusion
   - [x] Reciprocal Rank Fusion (RRF) algorithm
   - [x] Weighted score combination (configurable weights)
   - [x] Normalize vector distances and Lucene scores
8. [x] Update `DocumentService` to sync HNSW index
   - [x] Add vectors on document upload
   - [x] Update vectors on document merge
   - [x] Remove vectors on document delete
9. [x] Update `IndexerService` for HNSW integration
   - [x] Sync HNSW index during indexer runs
   - [x] Handle embedding generation with skillsets
10. [x] Add HNSW configuration to `VectorSearchSettings`
    - [x] `M` parameter (number of connections)
    - [x] `EfConstruction` (index build quality)
    - [x] `EfSearch` (search quality vs speed)
    - [x] Distance metric (cosine)
11. [x] Implement index persistence
    - [x] Save HNSW index to file on commit
    - [x] Load HNSW index on startup
    - [x] Handle index corruption gracefully
12. [x] Write comprehensive tests
    - [x] Basic HNSW CRUD operations (HnswIndexManagerTests - 22 tests)
    - [x] Filtered vector search accuracy
    - [x] Hybrid search scoring (HybridSearchServiceTests - 25 tests)
    - [ ] Performance benchmarks (deferred)

#### Data Model

```csharp
// Document stored in both systems
class SearchDocument
{
    public string Id { get; set; }
    public string Content { get; set; }
    public string Category { get; set; }
    public string[] Tags { get; set; }
    public DateTime CreatedDate { get; set; }
    public float[] ContentVector { get; set; }  // Stored in HNSW
}

// Lucene stores: id, content, category, tags, createdDate
// HNSWlib stores: internal_id → vector (with id mapping)
```

#### Filtered Vector Search Algorithm

```csharp
public async Task<List<SearchResult>> FilteredVectorSearchAsync(
    float[] queryVector,
    string filter,           // OData filter expression
    int topK,
    int oversampleMultiplier = 5)
{
    // 1. Vector search with oversampling
    var (labels, distances) = hnswIndex.SearchKnn(queryVector, k: topK * oversampleMultiplier);
    
    // 2. Map HNSW labels to document IDs
    var candidateIds = labels.Select(l => idMapping[l]).ToList();
    
    // 3. Fetch metadata from Lucene
    var metadata = FetchMetadataFromLucene(candidateIds);
    
    // 4. Apply OData filter
    var filtered = ApplyFilter(metadata, filter);
    
    // 5. Sort by vector distance and take top-K
    return filtered
        .OrderBy(m => m.Distance)
        .Take(topK)
        .ToList();
}
```

#### Hybrid Search with Score Fusion

```csharp
public async Task<List<SearchResult>> HybridSearchAsync(
    string textQuery,
    float[] queryVector,
    string filter,
    int topK,
    double vectorWeight = 0.7,
    double textWeight = 0.3)
{
    // 1. Get vector search results
    var vectorResults = await VectorSearchAsync(queryVector, topK * 3);
    
    // 2. Get text search results from Lucene
    var textResults = await TextSearchAsync(textQuery, topK * 3);
    
    // 3. Normalize scores
    var normalizedVector = NormalizeScores(vectorResults);
    var normalizedText = NormalizeScores(textResults);
    
    // 4. Combine with RRF or weighted fusion
    var combined = ReciprocaRankFusion(normalizedVector, normalizedText);
    // OR: WeightedFusion(normalizedVector, normalizedText, vectorWeight, textWeight);
    
    // 5. Apply filter and return top-K
    return ApplyFilter(combined, filter).Take(topK).ToList();
}
```

#### Configuration

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
      "OversampleMultiplier": 5
    },
    "HybridSearchSettings": {
      "DefaultFusionMethod": "RRF",
      "DefaultVectorWeight": 0.7,
      "DefaultTextWeight": 0.3
    }
  }
}
```

#### Deliverables

- HNSW-based vector search with sub-linear query time
- Filtered vector search with post-filter pattern
- Hybrid search with configurable score fusion
- Persistence of HNSW indexes to disk
- Comprehensive test coverage

### Phase 9: Local Embedding Models (Week 13-14) ✅ COMPLETED

**Goal**: Generate vector embeddings locally using ONNX Runtime, with zero external dependencies. The existing `AzureOpenAIEmbeddingSkill` gains a **local mode** — same OData type, same skillset JSON, same everything — triggered by setting `resourceUri` to `local://model-name`.

#### Architecture Overview

```text
┌──────────────────────────────────────────────────────────────────────┐
│              AzureOpenAIEmbeddingSkillExecutor                       │
│  ┌────────────────────────┐   ┌──────────────────────────────────┐   │
│  │  resourceUri starts    │   │  resourceUri starts              │   │
│  │  with https://         │   │  with local://                   │   │
│  │  → HTTP call to Azure  │   │  → Delegate to LocalOnnx-        │   │
│  │    OpenAI API          │   │    EmbeddingService (in-process) │   │
│  └────────────────────────┘   └──────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
                                         │
                                         ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    LocalOnnxEmbeddingService                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌────────────────────┐  │
│  │  BertTokenizer   │  | ONNX Runtime     │  │  Mean Pooling +    │  │
│  │  (ML.Tokenizers) │  │  InferenceSession│  │  L2 Normalization  │  │
│  └──────────────────┘  └──────────────────┘  └────────────────────┘  │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │               Model Cache (data/models/)                        │ │
│  │  all-MiniLM-L6-v2/model.onnx + vocab.txt  (384 dims, ~80 MB)    │ │
│  │  bge-small-en-v1.5/model.onnx + vocab.txt (384 dims, ~130 MB)   │ │
│  │  all-mpnet-base-v2/model.onnx + vocab.txt (768 dims, ~420 MB)   │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

#### Local Mode Trigger

The local mode is activated by using a **`local://` URI scheme** as the skill's `resourceUri`. This means users keep the exact same skillset JSON with the same `@odata.type` — only the `resourceUri` changes:

```json
{
  "@odata.type": "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
  "name": "embedding",
  "description": "Generate embeddings locally",
  "resourceUri": "local://all-MiniLM-L6-v2",
  "deploymentId": "ignored-in-local-mode",
  "modelName": "all-MiniLM-L6-v2",
  "inputs": [
    { "name": "text", "source": "/document/content" }
  ],
  "outputs": [
    { "name": "embedding", "targetName": "contentVector" }
  ]
}
```

Compare with the cloud version — only `resourceUri` and `deploymentId` differ:

```json
{
  "@odata.type": "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
  "name": "embedding",
  "description": "Generate embeddings via Azure OpenAI",
  "resourceUri": "https://my-openai.openai.azure.com",
  "deploymentId": "text-embedding-ada-002",
  "modelName": "text-embedding-ada-002",
  "inputs": [
    { "name": "text", "source": "/document/content" }
  ],
  "outputs": [
    { "name": "embedding", "targetName": "contentVector" }
  ]
}
```

#### Detection Logic in AzureOpenAIEmbeddingSkillExecutor

```csharp
public async Task<SkillExecutionResult> ExecuteAsync(
    Skill skill, EnrichedDocument document, CancellationToken ct)
{
    if (skill.ResourceUri?.StartsWith("local://", StringComparison.OrdinalIgnoreCase) == true)
    {
        // Extract model name from URI: "local://all-MiniLM-L6-v2" → "all-MiniLM-L6-v2"
        var modelName = skill.ResourceUri["local://".Length..];
        return await _localEmbeddingService.GenerateEmbeddingAsync(
            modelName, skill, document, ct);
    }

    // Existing Azure OpenAI HTTP path...
}
```

#### Supported Models

| Model | Dimensions | Size | Speed (CPU) | Quality | Default |
| ----- | ---------- | ---- | ----------- | ------- | ------- |
| `all-MiniLM-L6-v2` | 384 | ~80 MB | ~5 ms/embed | Good | ✅ |
| `bge-small-en-v1.5` | 384 | ~130 MB | ~8 ms/embed | Better | |
| `all-mpnet-base-v2` | 768 | ~420 MB | ~15 ms/embed | Best | |

All models are BERT-based sentence-transformers exported to ONNX format from HuggingFace.

#### Tasks

1. [x] Add NuGet packages: `Microsoft.ML.OnnxRuntime` 1.22.0, `Microsoft.ML.Tokenizers` 1.0.2 (replaced `FastBertTokenizer` which had no stable release)
2. [x] Create `LocalEmbeddingSettings` configuration class
   - [x] `ModelsDirectory` (default: `./data/models`)
   - [x] `DefaultModel` (default: `all-MiniLM-L6-v2`)
   - [x] `MaximumTokens` (default: `512`)
   - [x] `NormalizeEmbeddings` (default: `true`)
   - [x] `PoolingMode` (default: `Mean`)
3. [x] Create `ILocalEmbeddingService` interface

   ```csharp
   public interface ILocalEmbeddingService
   {
       Task<SkillExecutionResult> GenerateEmbeddingAsync(
           string modelName,
           Skill skill,
           EnrichedDocument document,
           CancellationToken ct);
       float[] GenerateEmbedding(string modelName, string text);
       bool IsModelAvailable(string modelName);
       int GetModelDimensions(string modelName);
   }
   ```

4. [x] Implement `LocalOnnxEmbeddingService`
   - [x] Load ONNX model + vocab from `data/models/{modelName}/`
   - [x] Lazy-load and cache `InferenceSession` per model (thread-safe via `ConcurrentDictionary`)
   - [x] Tokenize input text with `Microsoft.ML.Tokenizers.BertTokenizer` (replaced `FastBertTokenizer`)
   - [x] Run ONNX inference (input_ids, attention_mask, token_type_ids)
   - [x] Apply mean pooling over token embeddings
   - [x] L2-normalize the output vector via `TensorPrimitives`
   - [x] Return `float[]` embeddings
5. [x] Modify `AzureOpenAIEmbeddingSkillExecutor`
   - [x] Inject optional `ILocalEmbeddingService?`
   - [x] Detect `local://` scheme in `resourceUri`
   - [x] Delegate to `ILocalEmbeddingService` when local mode is detected
   - [x] Keep existing Azure OpenAI HTTP path unchanged
6. [x] Implement model management
   - [x] Auto-detect available models in `data/models/` directory
   - [x] Provide download instructions in logs when model not found
   - [x] Ship a PowerShell script to download models from HuggingFace (`scripts/Download-EmbeddingModel.ps1`)
   - [x] Optionally auto-download from HuggingFace on first use (behind config flag `AutoDownloadModels`)
7. [x] Add configuration to `appsettings.json`
8. [x] Register services in DI (`Program.cs`)
9. [x] Write unit tests (39 new tests, 682 total passing)
   - [x] Local embedding generation produces correct dimension vectors
   - [x] `local://` URI detection and delegation (case-insensitive)
   - [x] Cloud URI still routes to Azure OpenAI
   - [x] Model not found returns helpful error
   - [x] Thread safety with concurrent embedding requests
10. [x] Create sample `.http` file demonstrating local embedding skillset
11. [x] Update documentation (CONFIGURATION.md, LIMITATIONS.md, README.md)

#### ONNX Inference Pipeline

```csharp
public float[] GenerateEmbedding(string modelName, string text)
{
    var (session, tokenizer, dimensions) = GetOrLoadModel(modelName);

    // 1. Tokenize with Microsoft.ML.Tokenizers.BertTokenizer
    int maxTokens = _options.MaximumTokens;
    IReadOnlyList<int> encoded = tokenizer.EncodeToIds(text, maxTokens);
    int tokenCount = encoded.Count;
    long[] inputIds = encoded.Select(id => (long)id).ToArray();
    long[] attentionMask = Enumerable.Repeat(1L, tokenCount).ToArray();

    // 2. Build tensors
    var shape = new long[] { 1, tokenCount };
    using var inputIdsOrt = OrtValue.CreateTensorValueFromMemory(
        inputIds.AsMemory(), shape);
    using var attMaskOrt = OrtValue.CreateTensorValueFromMemory(
        attentionMask.AsMemory(), shape);
    using var typeIdsOrt = OrtValue.CreateTensorValueFromMemory(
        new long[tokenCount].AsMemory(), shape); // all zeros

    // 3. Run inference
    var inputs = new[] { inputIdsOrt, attMaskOrt, typeIdsOrt };
    using var outputs = session.Run(
        s_runOptions,
        new[] { "input_ids", "attention_mask", "token_type_ids" },
        inputs,
        session.OutputNames);

    // 4. Mean pooling
    ReadOnlySpan<float> rawOutput = outputs[0].GetTensorDataAsSpan<float>();
    float[] result = MeanPool(rawOutput, tokenCount, dimensions);

    // 5. L2 normalize
    float norm = TensorPrimitives.Norm(result);
    TensorPrimitives.Divide(result, norm, result);

    return result;
}
```

#### Configuration

```json
{
  "LocalEmbeddingSettings": {
    "ModelsDirectory": "./data/models",
    "DefaultModel": "all-MiniLM-L6-v2",
    "MaximumTokens": 512,
    "NormalizeEmbeddings": true,
    "PoolingMode": "Mean",
    "AutoDownloadModels": false
  }
}
```

#### Model Directory Structure

```text
data/
  models/
    all-MiniLM-L6-v2/
      model.onnx          # ONNX model file (~80 MB)
      vocab.txt           # BERT vocabulary file
    bge-small-en-v1.5/
      model.onnx
      vocab.txt
```

#### Model Download Script

```powershell
# scripts/Download-EmbeddingModel.ps1
param(
    [string]$ModelName = "all-MiniLM-L6-v2",
    [string]$OutputDir = "./data/models"
)

$models = @{
    "all-MiniLM-L6-v2"  = "sentence-transformers/all-MiniLM-L6-v2"
    "bge-small-en-v1.5" = "BAAI/bge-small-en-v1.5"
    "all-mpnet-base-v2" = "sentence-transformers/all-mpnet-base-v2"
}

$repo = $models[$ModelName]
$dir = Join-Path $OutputDir $ModelName
New-Item -ItemType Directory -Force -Path $dir | Out-Null

Write-Host "Downloading $ModelName from HuggingFace ($repo)..."
Invoke-WebRequest "https://huggingface.co/$repo/resolve/main/onnx/model.onnx" -OutFile "$dir/model.onnx"
Invoke-WebRequest "https://huggingface.co/$repo/resolve/main/vocab.txt" -OutFile "$dir/vocab.txt"
Write-Host "Done. Model saved to $dir"
```

#### Deliverables

- ✅ Local ONNX-based embedding generation (zero external dependencies)
- ✅ Transparent local mode for the existing `AzureOpenAIEmbeddingSkill` via `local://` URI
- ✅ Support for multiple BERT sentence-transformer models
- ✅ Model download script for HuggingFace models (`scripts/Download-EmbeddingModel.ps1`)
- ✅ Unit tests for local embedding pipeline (39 new tests, 682 total)
- ✅ Sample `.http` file (`samples/local-embedding-sample.http`)
- ✅ Updated documentation (CONFIGURATION.md, LIMITATIONS.md, README.md)
- Updated documentation

---

## 6. API Compatibility

### 6.1 API Version

The simulator will target API version **2024-07-01** as the baseline, with compatibility notes for newer versions.

### 6.2 Request/Response Format

- All requests/responses use JSON
- Proper `api-version` query parameter validation
- OData-style response format for collections
- Proper `@odata.context` annotations

### 6.3 Known Limitations

| Feature | Azure AI Search | Simulator | Notes |
| ------- | --------------- | --------- | ----- |
| Vector Search (HNSW) | ✅ | ✅ | HNSWlib.NET for fast ANN search |
| Filtered Vector Search | ✅ | ✅ | Post-filter pattern with oversampling |
| Azure OpenAI Embedding | ✅ | ✅ | Azure OpenAI endpoint or `local://` ONNX mode |
| Local Embedding (ONNX) | N/A | ✅ | Simulator-only: in-process BERT models via `local://` resourceUri |
| Hybrid Search | ✅ | ✅ | Text + vector with score fusion |
| Facets | ✅ | ✅ | Count and value facets |
| Azure Blob Storage | ✅ | ✅ | Full support with connection string, SAS, Managed Identity |
| ADLS Gen2 | ✅ | ✅ | Full support with hierarchical namespace |
| Custom WebApiSkill | ✅ | ✅ | Full support for external HTTP endpoints |
| Azure SDK Compatibility | ✅ | ✅ | Azure.Search.Documents SDK works |
| Semantic Ranking | ✅ | ❌ | Requires complex ML models |
| Knowledge Store | ✅ | ❌ | Future phase |
| Azure AI Skills (OCR, etc.) | ✅ | ❌ | Requires Azure AI Services |
| Scoring Profiles | ✅ | ✅ | Text weights, freshness, magnitude, distance, tag; all interpolation & aggregation modes |
| Synonym Maps | ✅ | ✅ | CRUD management, Solr format, query-time expansion |
| SLA/Availability | 99.9%+ | N/A | Local dev tool |
| Scale | Millions of docs | Limited | Dev/test only |

---

## 7. Configuration

### 7.1 Application Settings

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
    "CommitIntervalSeconds": 5
  },
  "IndexerSettings": {
    "MaxConcurrentIndexers": 3,
    "DefaultBatchSize": 1000,
    "EnableScheduler": true
  },
  "VectorSearchSettings": {
    "DefaultDimensions": 1536,
    "MaxVectorsPerIndex": 100000,
    "SimilarityMetric": "cosine",
    "UseHnsw": true,
    "HnswSettings": {
      "M": 16,
      "EfConstruction": 200,
      "EfSearch": 100,
      "OversampleMultiplier": 5
    },
    "HybridSearchSettings": {
      "DefaultFusionMethod": "RRF",
      "DefaultVectorWeight": 0.7,
      "DefaultTextWeight": 0.3
    }
  },
  "AzureOpenAISettings": {
    "Endpoint": "",
    "ApiKey": "",
    "DeploymentName": "text-embedding-ada-002",
    "ModelDimensions": 1536
  },
  "LocalEmbeddingSettings": {
    "ModelsDirectory": "./data/models",
    "DefaultModel": "all-MiniLM-L6-v2",
    "MaximumTokens": 512,
    "NormalizeEmbeddings": true,
    "PoolingMode": "Mean",
    "AutoDownloadModels": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

---

## 8. Dependencies (NuGet Packages)

Package versions are managed per project and kept up-to-date by Dependabot. See each `.csproj` for the current pinned versions.

### [AzureAISearchSimulator.Api](../src/AzureAISearchSimulator.Api/AzureAISearchSimulator.Api.csproj)

| Package | Purpose |
| ------- | ------- |
| Microsoft.AspNetCore.Authentication.JwtBearer | JWT / Entra ID authentication |
| Microsoft.AspNetCore.OpenApi | OpenAPI metadata |
| Microsoft.Identity.Web | Entra ID integration |
| Scalar.AspNetCore | API documentation UI (replaced Swashbuckle) |
| Serilog.AspNetCore, Serilog.Sinks.Console/File | Structured logging |
| System.IdentityModel.Tokens.Jwt | Token validation |

### [AzureAISearchSimulator.Core](../src/AzureAISearchSimulator.Core/AzureAISearchSimulator.Core.csproj)

| Package | Purpose |
| ------- | ------- |
| Azure.Core | Azure credential types |
| LiteDB | Embedded NoSQL metadata storage |

### [AzureAISearchSimulator.Search](../src/AzureAISearchSimulator.Search/AzureAISearchSimulator.Search.csproj)

| Package | Purpose |
| ------- | ------- |
| Lucene.Net, Lucene.Net.Analysis.Common | Full-text indexing and analysis |
| Lucene.Net.QueryParser | Simple + full Lucene query syntax |
| Lucene.Net.Facet | Faceted navigation |
| Lucene.Net.Highlighter | Hit highlighting |
| Lucene.Net.Suggest | Autocomplete and suggestions |
| HNSW | Approximate nearest-neighbour vector search |
| Microsoft.ML.OnnxRuntime | Local ONNX model inference for embeddings |
| Microsoft.ML.Tokenizers | BERT tokenization for local embedding models (replaced FastBertTokenizer) |
| PdfPig | PDF text extraction |
| DocumentFormat.OpenXml | Word / Excel document cracking |
| HtmlAgilityPack | HTML tag stripping |

### [AzureAISearchSimulator.Storage](../src/AzureAISearchSimulator.Storage/AzureAISearchSimulator.Storage.csproj)

| Package | Purpose |
| ------- | ------- |
| LiteDB | Persistent repositories (indexes, indexers, data sources, skillsets, synonym maps) |

### [AzureAISearchSimulator.DataSources](../src/AzureAISearchSimulator.DataSources/AzureAISearchSimulator.DataSources.csproj)

| Package | Purpose |
| ------- | ------- |
| Azure.Storage.Blobs | Azure Blob Storage connector |
| Azure.Storage.Files.DataLake | ADLS Gen2 connector |
| Azure.Identity | Managed Identity / credential chain |

### Test projects

| Project | Key packages |
| ------- | ------------ |
| [Api.Tests](../tests/AzureAISearchSimulator.Api.Tests/AzureAISearchSimulator.Api.Tests.csproj) | xUnit, Moq, Microsoft.AspNetCore.Mvc.Testing |
| [Core.Tests](../tests/AzureAISearchSimulator.Core.Tests/AzureAISearchSimulator.Core.Tests.csproj) | xUnit, Moq |
| [Integration.Tests](../tests/AzureAISearchSimulator.Integration.Tests/AzureAISearchSimulator.Integration.Tests.csproj) | xUnit, Moq |

---

## 9. Getting Started

See the main [README.md](../README.md) for prerequisites, quick start, Docker usage, and Azure SDK examples.

---

## 10. Success Criteria

1. **API Compatibility**: Azure Search SDK can connect and perform basic operations
2. **Search Quality**: Full-text search returns relevant results
3. **Vector Search Performance**: <50ms response time for HNSW vector search on 50K vectors
4. **Indexer Reliability**: Scheduled indexers run without errors
5. **Document Support**: PDF, Word, Excel files can be indexed
6. **Performance**: <100ms response time for typical searches on 10K documents
7. **Documentation**: Clear setup and usage instructions
8. **Test Coverage**: >80% unit test coverage

---

## 11. Risks and Mitigations

| Risk | Impact | Mitigation |
| ---- | ------ | ---------- |
| Lucene.NET version compatibility | High | Use stable beta version, comprehensive testing |
| HNSWlib.NET memory usage | Medium | Configure max vectors per index, document limits |
| Filtered vector search accuracy | Medium | Configurable oversampling multiplier |
| OData filter complexity | Medium | Implement subset, document limitations |
| PDF extraction quality | Medium | PdfPig handles most cases, document limitations |
| SDK compatibility issues | High | Test with official Azure SDK regularly |
| ONNX model download size | Medium | Provide download script, document sizes, support multiple model tiers |
| ONNX model compatibility | Low | Pin to well-tested HuggingFace ONNX exports, test on CI |
| Performance at scale | Low | Document as dev/test tool only |

---

## 12. Future Enhancements (Phase 3+)

1. ~~**Synonym Maps** - Word mappings for search expansion~~ ✅ Done — full CRUD, Solr format, query-time expansion
2. ~~**More Analyzers** - Language-specific analyzers~~ ✅ Done — 27 Lucene-backed language analyzers + 22 Microsoft-only fallbacks
3. **More Data Sources** - SQL database connector
4. **Knowledge Store** - Projection to external storage
5. **Admin UI** - Web-based management interface
6. **Metrics Dashboard** - Search analytics
7. **Import/Export** - Backup and restore indexes
~~8. **Local Embedding Models** - ML.NET or ONNX for offline embedding generation~~ ✅ Done — Phase 9: ONNX Runtime + Microsoft.ML.Tokenizers, `local://` URI trigger in existing AzureOpenAIEmbeddingSkill
9. **Pre-filtering for Vector Search** - Build filtered HNSW sub-indexes for common filter values
10. **Multiple Vector Fields** - Support for multiple vector fields per document
11. **Enforce SimulatorSettings Limits** - Wire up `MaxIndexes`, `MaxDocumentsPerIndex`, `MaxFieldsPerIndex`, `DefaultPageSize`, and `MaxPageSize` so the API rejects requests that exceed configured limits (currently defined but not enforced)

---

## Appendix A: API Endpoint Reference

See [API-REFERENCE.md](API-REFERENCE.md) for complete endpoint documentation.

## Appendix B: Sample Requests

See `samples/scripts/` directory for HTTP request examples.

## Appendix C: HNSW Algorithm Overview

### What is HNSW?

Hierarchical Navigable Small World (HNSW) is a graph-based algorithm for approximate nearest neighbor (ANN) search. It provides:

- **Sub-linear query time**: O(log n) vs O(n) for brute-force
- **High recall**: Typically 95-99% accuracy
- **Efficient updates**: Supports incremental insertions

### Key Parameters

| Parameter | Description | Trade-off |
| --------- | ----------- | --------- |
| M | Number of connections per node | Higher = better recall, more memory |
| EfConstruction | Search depth during index build | Higher = better quality, slower build |
| EfSearch | Search depth during query | Higher = better recall, slower query |

### Recommended Settings

| Use Case | M | EfConstruction | EfSearch |
| -------- | - | -------------- | -------- |
| Development | 16 | 100 | 50 |
| Production (balanced) | 16 | 200 | 100 |
| Production (high recall) | 32 | 400 | 200 |

### Why Post-filtering?

HNSWlib.NET does not support metadata filtering natively. The post-filter pattern:

1. **Oversample**: Retrieve K × multiplier candidates from HNSW
2. **Fetch metadata**: Get document metadata from Lucene
3. **Apply filter**: Filter based on category, tags, dates, etc.
4. **Return top-K**: Return the filtered results

This is the same pattern used by production vector databases like Elasticsearch.

---

*Document Version: 2.2*  
*Last Updated: February 18, 2026*
