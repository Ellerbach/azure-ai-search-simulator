# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Phase 10: CI/CD (In Progress)

- **GitHub Actions Workflow** (`.github/workflows/build.yml`)
  - Automated build and test on push to main and pull requests
  - .NET 10 SDK setup
  - Test results upload as artifacts
  - Docker image build verification
  - Sample projects build validation

- **PR Template** (`.github/PULL_REQUEST_TEMPLATE.md`)
  - Standardized pull request format
  - Checklist for code quality, testing, and documentation
  - Type of change classification
  - Azure AI Search reference links

### Added

#### Phase 9: Python Samples & fixes

- **Python Sample** (`samples/AzureSearchNotebook/`)
  - Jupyter notebook demonstrating Azure AI Search Simulator usage
  - Python client integration examples
  - Interactive search and indexing demonstrations

### Fixed

- Various bug fixes and stability improvements
- Sample code corrections and enhancements

### Changed

- Enhanced sample documentation

---

### Added

#### Phase 8: Azure Data Source Connectors (Completed)

- **Azure Blob Storage Connector** (`AzureBlobStorageConnector.cs`)
  - Full support for Azure Blob Storage as a data source
  - Connection string authentication (Account Key, SAS Token)
  - Managed Identity authentication via DefaultAzureCredential
  - Blob prefix/folder filtering via container query
  - Automatic MIME type detection
  - Metadata extraction (path, name, size, last modified, content type)

- **Azure Data Lake Storage Gen2 Connector** (`AdlsGen2Connector.cs`)
  - Full support for ADLS Gen2 with hierarchical namespace
  - DFS endpoint support (`.dfs.core.windows.net`)
  - Connection string and Managed Identity authentication
  - Recursive directory listing
  - Owner/group metadata extraction (ADLS-specific)

- **Data Sources Project** (`AzureAISearchSimulator.DataSources`)
  - New project for Azure-specific data source connectors
  - `ServiceCollectionExtensions` for easy DI registration
  - Azure.Storage.Blobs and Azure.Storage.Files.DataLake packages

- **Custom Skills Sample** (`samples/CustomSkillSample/`)
  - Sample ASP.NET Core project demonstrating custom skill implementation
  - Text stats skill (character, word, sentence counts)
  - Keyword extraction skill
  - Sentiment analysis skill
  - PII detection skill
  - Summarization skill
  - Integration example with skillset and indexer

- **Sample Enhancements**
  - Added indexer, data source, and skillset examples to Azure SDK sample
  - Added `requests.http` REST client file with comprehensive examples
  - Pull mode test workflow examples

#### Phase 7: Search Enhancements & SDK Compatibility (Completed)

- **Faceted Navigation**
  - Value facets for categorical fields (e.g., `category,count:10`)
  - Interval facets for numeric fields (e.g., `rating,interval:1`)
  - Range facets for numeric/date fields (e.g., `price,values:0|10|50|100`)
  - Facet response in search results with value/count pairs
  - Lucene term enumeration for efficient facet calculation

- **Field Validation**
  - Document field validation against index schema
  - Type checking for all EDM types (strings, numbers, dates, collections, geography)
  - Warning logs for type mismatches (lenient behavior matching Azure AI Search)
  - Support for `JsonElement` type validation

- **Azure SDK Compatibility**
  - Added OData entity syntax routes (`indexes('name')`) for Azure SDK compatibility
  - Created `samples/AzureSdkSample` project demonstrating SDK usage
  - Support for Azure.Search.Documents SDK version 11.6.0

- **Test Coverage**
  - `FacetTests.cs` - Unit tests for facet functionality (8 tests)
  - `VectorSearchTests.cs` - Unit tests for vector search (8 tests)
  - `EmbeddingSkillTests.cs` - Unit tests for Azure OpenAI embedding skill (9 tests)
  - Total: 55 passing tests

#### Phase 6: Polish & Documentation (Completed)

- **Error Handling**
  - `ODataError` response format with error body and inner errors
  - `ExceptionHandlerMiddleware` for global exception handling
  - Proper HTTP status code mapping (400, 404, 409, 500)
  - Error codes: `InvalidArgument`, `ResourceNotFound`, `ResourceAlreadyExists`, `InternalServerError`

- **Docker Support**
  - Multi-stage `Dockerfile` with .NET 10 SDK and ASP.NET runtime
  - `docker-compose.yml` for easy deployment
  - Volume mounts for data persistence
  - Health check configuration

- **Documentation**
  - `CONFIGURATION.md` - Complete configuration guide
  - `API-REFERENCE.md` - Comprehensive API documentation
  - `LIMITATIONS.md` - Known limitations vs Azure AI Search
  - `CONTRIBUTING.md` - Contribution guidelines
  - `LICENSE` - MIT License
  - Environment variable reference
  - Docker deployment instructions
  - Security considerations and performance tuning tips

#### Phase 5: Skillsets (Completed)

- **Skillset Management**
  - `Skillset` and `Skill` models following Azure AI Search schema
  - `SkillsetService` and `LiteDbSkillsetRepository`
  - `SkillsetsController` with CRUD endpoints

- **Skill Executors**
  - `TextSplitSkillExecutor` - Split text into pages or sentences
  - `TextMergeSkillExecutor` - Concatenate text arrays
  - `ShaperSkillExecutor` - Create complex output shapes
  - `ConditionalSkillExecutor` - Conditional logic in pipelines
  - `CustomWebApiSkillExecutor` - Call external web services
  - `AzureOpenAIEmbeddingSkillExecutor` - Generate embeddings via Azure OpenAI

- **Skill Pipeline**
  - `SkillPipeline` for orchestrating skill execution
  - `EnrichedDocument` model for tracking enrichments
  - Input/output context mapping with JSON path support

#### Phase 4: Document Cracking (Completed)

- **Document Crackers**
  - `IDocumentCracker` interface with content type-based selection
  - `PdfCracker` using PdfPig for PDF text extraction
  - `WordDocCracker` using OpenXML SDK for DOCX files
  - `ExcelCracker` for XLSX spreadsheets
  - `HtmlCracker` using HtmlAgilityPack
  - `JsonCracker` and `CsvCracker` for structured data
  - `PlainTextCracker` with encoding detection

- **Metadata Extraction**
  - File metadata (name, path, size, created/modified dates)
  - Document metadata (title, author, page count, word count)

#### Phase 3: Pull Model - Indexers & Data Sources (Completed)

- **Data Sources**
  - `DataSource` model with connection string parsing
  - `DataSourceService` for CRUD operations
  - `DataSourcesController` with REST endpoints
  - Local file system connector for development

- **Indexers**
  - `Indexer` model with field mappings and output field mappings
  - `IndexerService` with execution engine
  - `IndexersController` with run/status/reset endpoints
  - Change detection based on file timestamps
  - Support for scheduled and on-demand runs

#### Phase 2: Document Operations & Search (Completed)

- **Document Operations**
  - `DocumentsController` with full document management:
    - `POST /indexes/{name}/docs/index` - Upload/merge/delete documents
    - `GET /indexes/{name}/docs/{key}` - Get document by key
    - `GET /indexes/{name}/docs/$count` - Get document count
  - Support for all action types: `upload`, `merge`, `mergeOrUpload`, `delete`
  - Batch document operations

- **Lucene.NET Integration**
  - `LuceneIndexManager` for index lifecycle management
  - `LuceneDocumentMapper` for field type mapping:
    - `Edm.String` → TextField/StringField
    - `Edm.Int32` → Int32Field
    - `Edm.Int64` → Int64Field
    - `Edm.Double` → DoubleField
    - `Edm.Boolean` → StringField
    - `Edm.DateTimeOffset` → Int64Field (ticks)
    - `Collection(Edm.String)` → Multiple TextField/StringField
    - `Collection(Edm.Single)` → StoredField (for vectors)
  - Proper handling of `JsonElement` conversion for all types

- **Full-Text Search**
  - `SearchService` with full query support:
    - Simple query syntax (default)
    - Full Lucene query syntax
  - `SearchRequest`/`SearchResponse` models compatible with Azure AI Search
  - Support for:
    - `search` - Text query
    - `filter` - Basic OData expressions (eq, ne, gt, lt, ge, le, search.in)
    - `orderby` - Sorting
    - `top`/`skip` - Paging
    - `select` - Field selection
    - `count` - Include total count
    - `highlight` - Search result highlighting
    - `searchMode` - any/all
    - `queryType` - simple/full

- **Suggestions & Autocomplete**
  - `POST /indexes/{name}/docs/suggest` - Get suggestions
  - `POST /indexes/{name}/docs/autocomplete` - Get autocomplete

- **Vector Search**
  - `VectorStore` for in-memory vector storage
  - Cosine similarity calculation
  - Support for `Collection(Edm.Single)` field type
  - `vectorQueries` parameter support:
    - `kind`: "vector"
    - `vector`: float array
    - `fields`: target vector field
    - `k`: number of neighbors

- **Hybrid Search**
  - Combined text and vector search
  - Score combination (50/50 weighted average)

#### Phase 1: Foundation (Completed)

- **API Infrastructure**
  - ASP.NET Core 10.0 Web API project
  - Serilog structured logging with console and file sinks
  - Scalar/OpenAPI documentation (replaced Swashbuckle due to .NET 10 compatibility)
  - Configuration models: `SimulatorSettings`, `LuceneSettings`, `IndexerSettings`, `VectorSearchSettings`, `AzureOpenAISettings`

- **Authentication**
  - `ApiKeyAuthenticationMiddleware` for API key validation
  - Support for admin keys (full access) and query keys (read-only)
  - Configurable keys via `appsettings.json`

- **Index Management**
  - `SearchIndex` and `SearchField` models with full EDM type support
  - `IndexService` with comprehensive validation
  - `IndexesController` with CRUD endpoints:
    - `POST /indexes` - Create index
    - `GET /indexes` - List all indexes
    - `GET /indexes/{name}` - Get index by name
    - `PUT /indexes/{name}` - Create or update index
    - `DELETE /indexes/{name}` - Delete index
  - `LiteDbIndexRepository` for index metadata persistence

### Technical Details

- **Target Framework**: .NET 10.0
- **Search Engine**: Lucene.NET 4.8.0-beta00016
- **Metadata Storage**: LiteDB 5.0.21
- **API Documentation**: Scalar.AspNetCore 2.0.18
- **Logging**: Serilog 8.0.3
- **PDF Extraction**: PdfPig 0.1.9
- **Office Documents**: DocumentFormat.OpenXml 3.3.0
- **HTML Parsing**: HtmlAgilityPack 1.12.0
- **Azure Storage**: Azure.Storage.Blobs 12.23.0, Azure.Storage.Files.DataLake 12.21.0
- **Azure Identity**: Azure.Identity 1.13.2

### Known Limitations

- OData filter parsing is basic (supports eq, ne, gt, lt, ge, le, and search.in)
- Scoring profiles are partially supported
- Vector search uses brute-force cosine similarity (no HNSW optimization)
- Cognitive skills are simplified implementations (not Azure AI Services quality)
- No synonym maps support
- No semantic ranking/search

---

## [0.1.0] - Initial Development

- Project structure created
- Documentation: PLAN.md, API-REFERENCE.md, TODO.md, README.md
