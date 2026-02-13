# Implementation Task Tracker

## Phase 1: Foundation (Week 1-2) ✅ COMPLETED

### Infrastructure Setup

- [x] Configure API project with proper routing
- [x] Add Serilog logging
- [x] Add Scalar/OpenAPI documentation (replaced Swashbuckle due to .NET 10 compatibility)
- [x] Create configuration models (SimulatorSettings, LuceneSettings, IndexerSettings, VectorSearchSettings, AzureOpenAISettings)
- [x] Set up dependency injection

### Authentication

- [x] Create ApiKeyAuthenticationMiddleware
- [x] Implement admin key validation
- [x] Implement query key validation
- [x] Add key configuration from appsettings.json

### Index Management

- [x] Create SearchIndex model
- [x] Create SearchField model with all attributes
- [x] Implement IIndexService interface
- [x] Implement IndexService
- [x] Create IndexesController with all endpoints
- [x] Set up LiteDB storage for index metadata
- [x] Implement index validation
- [x] Write unit tests for index operations

### Lucene Integration

- [x] Set up Lucene.NET index directory management
- [x] Create LuceneIndexManager for CRUD operations
- [x] Implement field type mapping to Lucene fields
- [x] Create analyzer factory for built-in analyzers (AnalyzerFactory.cs)

---

## Phase 2: Document Operations & Search (Week 3-4) ✅ COMPLETED

### Document Operations

- [x] Create document upload endpoint
- [x] Implement upload action
- [x] Implement merge action
- [x] Implement mergeOrUpload action
- [x] Implement delete action
- [x] Add batch operation support
- [x] Implement document retrieval by key
- [x] Implement document count
- [x] Add field validation against index schema

### Search Implementation

- [x] Create search request model
- [x] Create search response model
- [x] Implement simple query parser
- [x] Implement full Lucene query parser
- [x] Add OData filter expression parser (basic)
- [x] Implement sorting
- [x] Implement paging (top/skip)
- [x] Implement field selection ($select)
- [x] Add scoring/relevance

### Advanced Search Features

- [x] Implement facets (count and interval)
- [x] Add highlighting support
- [x] Implement autocomplete
- [x] Implement suggestions
- [x] Write comprehensive search tests (FacetTests.cs, VectorSearchTests.cs)

### Vector Search

- [x] Create in-memory vector storage
- [x] Implement Collection(Edm.Single) field type
- [x] Implement cosine similarity calculation
- [x] Create VectorQuery model
- [x] Implement vector search endpoint
- [x] Implement hybrid search (text + vector)
- [x] Add vector search configuration to index schema (VectorSearchConfiguration with algorithms and profiles)
- [x] Write vector search tests (VectorSearchTests.cs)

---

## Phase 3: Pull Model - Indexers & Data Sources (Week 5-6) ✅ COMPLETED

### Data Sources

- [x] Create DataSource model
- [x] Implement IDataSourceService interface
- [x] Implement DataSourceService
- [x] Create DataSourcesController
- [x] Implement local file system connector
- [x] Add connection string parsing
- [x] Implement Azure Blob Storage connector (with Managed Identity support)
- [x] Implement ADLS Gen2 connector (with hierarchical namespace)

### Indexers

- [x] Create Indexer model
- [x] Create IndexerStatus model
- [x] Implement IIndexerService interface
- [x] Implement IndexerService
- [x] Create IndexersController
- [x] Implement field mappings
- [x] Add output field mappings support

### Indexer Execution

- [x] Create IndexerEngine for execution (in IndexerService)
- [x] Implement on-demand run
- [ ] Add Quartz.NET scheduler (deferred - manual scheduling works)
- [ ] Implement scheduled runs (deferred)
- [x] Add indexer status tracking
- [x] Implement reset functionality
- [x] Add change detection (file timestamps)

---

## Phase 4: Document Cracking (Week 7) ✅ COMPLETED

### Document Crackers

- [x] Create IDocumentCracker interface
- [x] Implement PdfCracker with PdfPig
- [x] Implement WordDocCracker with OpenXML
- [x] Implement ExcelCracker with OpenXML
- [ ] Implement PowerPointCracker (deferred)
- [x] Implement JsonCracker
- [x] Implement CsvCracker
- [x] Implement PlainTextCracker
- [x] Implement HtmlCracker with HtmlAgilityPack
- [x] Create DocumentCrackerFactory

### Metadata Extraction

- [x] Extract file metadata (name, path, size, dates)
- [x] Extract content metadata where available
- [x] Handle encoding detection

---

## Phase 5: Skillsets (Week 8-9) ✅ COMPLETED

### Skillset Management

- [x] Create Skillset model
- [x] Create Skill base class/interface (ISkillExecutor)
- [x] Implement ISkillsetService
- [x] Implement SkillsetService
- [x] Create SkillsetsController
- [x] Create ISkillsetRepository and LiteDbSkillsetRepository

### Skill Pipeline

- [x] Create skill execution pipeline (ISkillPipeline, SkillPipeline)
- [x] Implement input/output mapping
- [x] Create enriched document model (EnrichedDocument)
- [x] Handle skill dependencies/ordering

### Utility Skills

- [x] Implement TextSplitSkill (pages, sentences)
- [x] Implement TextMergeSkill
- [x] Implement ConditionalSkill
- [x] Implement ShaperSkill
- [ ] Implement DocumentExtractionSkill (deferred - document cracking handles this)

### Custom Skills

- [x] Implement CustomWebApiSkill
- [x] Add HTTP client with timeout
- [x] Handle batch requests
- [x] Add header configuration

### Azure OpenAI Embedding Skill

- [x] Create AzureOpenAIEmbeddingSkill class
- [x] Implement HTTP-based Azure OpenAI client
- [x] Add configuration for endpoint/API key
- [x] Handle embedding response mapping
- [x] Add error handling for API failures
- [x] Write embedding skill tests (EmbeddingSkillTests.cs with 9 tests)

### Integration

- [x] Update IndexerService to execute skillsets
- [x] Integrate skill pipeline with document processing
- [x] Support output field mappings

---

## Phase 6: Polish & Documentation (Week 10) ✅ COMPLETED

### Error Handling

- [x] Create OData error response format
- [x] Add global exception handler (ExceptionHandlerMiddleware)
- [x] Implement proper HTTP status codes
- [x] Add validation error details (ODataError with InnerError)

### Documentation

- [x] Write configuration guide (CONFIGURATION.md)
- [x] Complete API reference (API-REFERENCE.md - comprehensive documentation)
- [x] Add code examples (in README.md and API-REFERENCE.md)
- [x] Create sample HTTP requests (samples/sample-requests.http)
- [x] Document all limitations (in README.md and PLAN.md)

### Testing

- [x] Add unit tests for core models (28 tests passing)
- [x] Add integration tests (basic infrastructure in place)
- [x] Test with Azure SDK (sample project in samples/AzureSdkSample)
- [ ] Performance testing

### Deployment

- [x] Create Dockerfile (multi-stage build)
- [x] Add docker-compose.yml
- [x] Write deployment guide (in CONFIGURATION.md)

---

## Backlog (Future)

### Future Enhancements

- [ ] Synonym maps
- [ ] More language analyzers
- [ ] Azure SQL data source connector
- [ ] Azure Cosmos DB connector
- [ ] Knowledge store (Azure Storage projections)
- [ ] Admin web UI
- [ ] Metrics dashboard
- [ ] Index import/export
- [ ] Local embedding models (ML.NET/ONNX)

---

## Phase 7: HNSW Vector Search (Week 11-12) ✅ COMPLETED

### HNSW.NET Integration

- [x] Add HNSW NuGet package to AzureAISearchSimulator.Search
- [x] Create `IHnswIndexManager` interface
- [x] Implement `HnswIndexManager` class
  - [x] Index lifecycle management (create, open, close, delete)
  - [x] Persist HNSW index to disk alongside Lucene index
  - [x] Support configurable HNSW parameters (M, EfConstruction, EfSearch)
  - [x] Handle multiple vector fields per index
- [x] Create ID mapping system (HNSW internal index ↔ document ID)
- [x] Add HNSW configuration settings (HnswSettings, HybridSearchSettings)

### Vector Search Service

- [x] Create `IVectorSearchService` interface
- [x] Implement `HnswVectorSearchService`
  - [x] Basic KNN search
  - [x] Oversampling for filtered queries (K × multiplier)
  - [x] Distance-to-score conversion (cosine → similarity score)
  - [x] Fallback to brute-force when HNSW disabled

### Filtered Vector Search

- [x] Implement post-filter pattern
  - [x] Vector search → Filter candidates → Return top-K
  - [x] Configurable oversampling multiplier (default: 5x)
- [x] Support candidate document ID filtering
- [x] Handle edge cases (fallback to brute-force for remaining)

### Hybrid Search Enhancement

- [x] Implement Reciprocal Rank Fusion (RRF) algorithm
- [x] Implement weighted score fusion
  - [x] Normalize vector distances (1 / (1 + distance))
  - [x] Normalize Lucene scores (min-max or sigmoid)
  - [x] Configurable weights (default: 0.7 vector, 0.3 text)
- [x] Support query parameter for fusion method selection

### Document Service Integration

- [x] Update `DocumentService` to sync HNSW index
  - [x] Add vectors on document upload
  - [x] Update vectors on document merge
  - [x] Remove vectors on document delete
- [x] Handle batch operations efficiently

### Indexer Integration

- [x] IndexerService uses DocumentService which now syncs HNSW
- [x] Handle embedding generation with skillsets
- [x] Sync HNSW index after skillset execution (via DocumentService)

### Configuration

- [x] Add `HnswSettings` to `VectorSearchSettings`
  - [x] M parameter (number of connections, default: 16)
  - [x] EfConstruction (index build quality, default: 200)
  - [x] EfSearch (search quality vs speed, default: 100)
  - [x] OversampleMultiplier (for filtered search, default: 5)
- [x] Add `HybridSearchSettings`
  - [x] DefaultFusionMethod (RRF or Weighted)
  - [x] DefaultVectorWeight (0.7)
  - [x] DefaultTextWeight (0.3)
- [x] Add `UseHnsw` toggle for fallback to brute-force

### Persistence

- [x] Save HNSW index to file on commit
- [x] Load HNSW index on startup
- [x] Handle index corruption gracefully
- [x] Implement index rebuild capability

### Testing

- [x] Write unit tests for HnswIndexManager (22 tests)
- [x] Write unit tests for VectorSearchSettings (14 tests)
- [x] Write unit tests for HnswVectorSearchService (14 tests)
- [x] Write unit tests for BruteForceVectorSearchService (3 tests)
- [x] Write unit tests for HybridSearchService (25 tests)
- [x] Write integration tests for DocumentService + HNSW (10 tests)
- [ ] Write filtered vector search accuracy tests
- [ ] Add performance benchmarks (10K, 50K, 100K vectors)

---

*Track progress by checking off completed items*

---

## Phase 8: API Version 2025-09-01 Support

### Index Schema Enhancements

- [x] Add `description` property to SearchIndex model
- [x] Update IndexesController to handle description
- [x] Add `normalizer` property to SearchField model
- [x] Implement built-in normalizers (lowercase, uppercase, standard)
- [x] Add normalizer support to Lucene field mapping
- [x] Create NormalizerFactory for applying normalizations
- [x] Support custom normalizers with token filters

### Vector Search Enhancements

- [ ] Add `truncationDimension` property to vector fields
- [ ] Implement dimension truncation for MRL models (text-embedding-3-*)
- [ ] Add `rescoringOptions` to vector search configuration
- [ ] Implement rescoring with original vectors after compression

### Search Debug Features

- [x] Add `debug` query parameter to search API
- [x] Implement subscore breakdown for hybrid queries
- [x] Return RRF component scores in response
- [x] Return individual vector/text match scores

### Data Source Connectors

- [ ] Create OneLake indexer data source
- [ ] Implement OneLake lakehouse connector
- [ ] Add OneLake authentication (service principal, managed identity)

### Skills

- [ ] Implement DocumentLayoutSkill
- [ ] Integrate with document structure analysis
- [ ] Support structure-aware chunking

### Testing

- [x] Write tests for index description property
- [x] Write tests for normalizers
- [x] Write tests for search debug output (SearchDebugTests.cs - 69 tests)
- [ ] Write tests for truncated dimensions
