# Azure AI Search Simulator - Limitations

This document outlines the differences and limitations between the Azure AI Search Simulator and the actual Azure AI Search service.

## Overview

The simulator is designed for **development, learning, and testing purposes only**. It is not intended to replace Azure AI Search for production workloads.

## API Version Compatibility

| API Version | Support Level | Notes |
| ----------- | ------------- | ----- |
| `2025-09-01` | 🔄 In Progress | Latest stable - partial support |
| `2024-07-01` | ✅ Full | Primary development target |
| `2023-11-01` | ⚠️ Partial | Basic compatibility |

### 2025-09-01 Features Status

| Feature | Status |
| ------- | ------ |
| Index description property | ✅ Implemented |
| Search debug parameter | ✅ Implemented |
| Normalizers | ✅ Implemented (see below for details) |
| Truncated dimensions (MRL) | 🔄 Planned |
| Rescoring options | 🔄 Planned |
| OneLake indexer | ❌ Not planned |
| Document Layout skill | 🔄 Planned |

## Feature Comparison

### ✅ Fully Supported

| Feature | Notes |
| ------- | ----- |
| Index CRUD operations | Full support for create, read, update, delete |
| Field types | All basic types including Complex types and Collections |
| **Vector fields** | `Collection(Edm.Single)` with dimensions property |
| Document operations | upload, merge, mergeOrUpload, delete |
| Simple query syntax | Standard search queries |
| Full Lucene syntax | Wildcards, fuzzy, proximity, boosting |
| **Vector search (HNSW)** | HNSW algorithm for fast ANN search |
| **Hybrid search** | Combined text + vector with RRF/weighted fusion |
| **Filtered vector search** | Post-filter pattern with oversampling |
| OData filters | Comparison, logical, and collection operators |
| Sorting | Single and multi-field sorting |
| Paging | Top and skip parameters |
| Facets | Count, interval, and aggregation (`metric:sum`/`min`/`max`/`avg`) facets |
| Highlighting | Hit highlighting in search results |
| Autocomplete | Term completion suggestions |
| Suggestions | Type-ahead suggestions |
| Indexers | Automated document ingestion with scheduled runs |
| Data sources | Local file system, Azure Blob Storage, ADLS Gen2 |
| Skillsets | Utility skills (see below) |
| **Azure OpenAI Embedding** | Azure OpenAI endpoint or `local://` ONNX mode (no Azure dependency) |
| API key authentication | Admin and query keys |
| **Entra ID authentication** | Real or simulated JWT tokens with role mapping |
| **Role-Based Access Control** | Full RBAC with 6 Azure Search roles |
| **Managed Identity** | System and user-assigned for data sources and skills |
| **Normalizers** | All predefined and custom normalizer configurations |
| **Search debug** | Subscore breakdown for hybrid/vector queries |
| **Synonym maps** | CRUD management and query-time expansion (Solr format) |
| **Scoring profiles** | All 4 function types (freshness, magnitude, distance, tag), text weights, interpolation modes, and aggregation modes. Distance uses Haversine approximation. `scoringStatistics: "global"` is accepted but always uses local statistics. |
| **Similarity configuration** | BM25Similarity with tunable `k1`/`b` parameters, ClassicSimilarity (TF-IDF). Immutability enforced on update; `allowIndexDowntime` supported for BM25 parameter changes. |
| **featuresMode** | Per-field BM25 scoring breakdown via `@search.features` with `uniqueTokenMatches`, `similarityScore`, and `termFrequency` per searchable field. |

### ⚠️ Partially Supported

| Feature | Limitation |
| ------- | ---------- |
| Analyzers | All Lucene language analyzers supported (27 languages); `.microsoft` names accepted and mapped to Lucene equivalents |
| Synonym maps | CRUD management and query-time expansion supported; Solr format only |
| Custom analyzers | Basic tokenizers and filters only |
| CORS | Simplified implementation |
| Service statistics | Quotas and limits use hardcoded S1 tier defaults; not enforced |

### ❌ Not Supported

| Feature | Reason |
| ------- | ------ |
| **Semantic search** | Requires Azure AI models |
| **Semantic ranking** | Requires Azure AI models |
| **Pre-filtering for vectors** | HNSW does not support native filtering; uses post-filter |
| **Knowledge stores** | Complex Azure Storage integration |
| **AI enrichment skills** | OCR, Entity Recognition, etc. require Azure AI Services |
| **Private endpoints** | Azure networking feature |
| **Customer-managed keys** | Azure Key Vault integration |
| **Debug sessions (skillset)** | Complex debugging infrastructure |
| **Index aliases** | Not yet implemented |
| **Replica/partition scaling** | Single-instance only |

## API Compatibility

### Supported API Version

- **Target version**: `2024-07-01`
- Most requests compatible with `2023-11-01` through `2024-07-01`

> [!Important] The simulator is **not** checking the version. It fully ignores it.

### Request/Response Differences

1. **@odata.context**: May differ slightly from Azure AI Search responses
2. **Error messages**: Error format is compatible but specific messages may differ
3. **ETags**: Simplified ETag implementation
4. **Throttling**: No real throttling, but rate limiting exists for testing

## Performance Characteristics

| Aspect | Azure AI Search | Simulator |
| ------ | --------------- | --------- |
| Indexing throughput | 10,000+ docs/sec | ~1,000 docs/sec |
| Query latency | <100ms | Variable (depends on data size) |
| Maximum documents | Billions | ~100,000 (configurable) |
| Maximum indexes | 200+ | 50 (configurable) |
| Maximum fields | 3000 | 1000 (configurable) |
| Concurrent queries | Thousands | Limited by machine resources |

## Skillset Limitations

### Supported Skills

| Skill | Status | Notes |
| ----- | ------ | ----- |
| Text Split | ✅ Full | Pages and sentences |
| Text Merge | ✅ Full | - |
| Conditional | ✅ Full | Basic conditions |
| Shaper | ✅ Full | - |
| Document Extraction | ⚠️ Partial | PDF, Office, JSON, text only |
| Custom Web API | ✅ Full | Calls external HTTP endpoints |
| Azure OpenAI Embedding | ✅ Full | Azure OpenAI endpoint or `local://` ONNX mode |

### Complete Skills Reference

The following table lists **all skills available in Azure AI Search** and their support status in the simulator:

#### Foundry/Cognitive Services Skills (Billable in Azure)

| @odata.type | Skill Name | Azure | Simulator | Notes |
| --- | --- | --- | --- | --- |
| `#Microsoft.Skills.Vision.OcrSkill` | OCR | ✅ | ❌ | Requires Azure AI Vision |
| `#Microsoft.Skills.Vision.ImageAnalysisSkill` | Image Analysis | ✅ | ❌ | Requires Azure AI Vision |
| `#Microsoft.Skills.Text.V3.EntityRecognitionSkill` | Entity Recognition | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.EntityLinkingSkill` | Entity Linking | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.KeyPhraseExtractionSkill` | Key Phrase Extraction | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.LanguageDetectionSkill` | Language Detection | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.PIIDetectionSkill` | PII Detection | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.V3.SentimentSkill` | Sentiment Analysis | ✅ | ❌ | Requires Azure AI Language |
| `#Microsoft.Skills.Text.TranslationSkill` | Text Translation | ✅ | ❌ | Requires Azure AI Translator |
| `#Microsoft.Skills.Vision.VectorizeSkill` | Vision Multimodal Embeddings | ✅ | ❌ | Requires Azure AI Vision |

#### Azure-Hosted Model Skills

| @odata.type | Skill Name | Azure | Simulator | Notes |
| --- | --- | --- | --- | --- |
| `#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill` | Azure OpenAI Embedding | ✅ | ✅ | Azure OpenAI or `local://` ONNX mode |
| `#Microsoft.Skills.Custom.AzureContentUnderstandingSkill` | Azure Content Understanding | ✅ | ❌ | Requires Azure AI Document Intelligence |
| `#Microsoft.Skills.Text.GenAIPromptSkill` | GenAI Prompt | ✅ | ❌ | Requires Azure OpenAI |

#### Custom Skills

| @odata.type | Skill Name | Azure | Simulator | Notes |
| --- | --- | --- | --- | --- |
| `#Microsoft.Skills.Custom.WebApiSkill` | Web API | ✅ | ✅ | Full support for external REST APIs |
| `#Microsoft.Skills.Custom.AmlSkill` | Azure Machine Learning | ✅ | ❌ | Requires Azure ML workspace |
| `#Microsoft.Skills.Text.CustomEntityLookupSkill` | Custom Entity Lookup | ✅ | ❌ | Not yet implemented |

#### Utility Skills (Free - No external dependencies)

| @odata.type | Skill Name | Azure | Simulator | Notes |
| --- | --- | --- | --- | --- |
| `#Microsoft.Skills.Text.SplitSkill` | Text Split | ✅ | ✅ | Pages and sentences |
| `#Microsoft.Skills.Text.MergeSkill` | Text Merge | ✅ | ✅ | Full support |
| `#Microsoft.Skills.Util.ShaperSkill` | Shaper | ✅ | ✅ | Restructure data |
| `#Microsoft.Skills.Util.ConditionalSkill` | Conditional | ✅ | ✅ | Filter and merge data |
| `#Microsoft.Skills.Util.DocumentExtractionSkill` | Document Extraction | ✅ | ✅ | Base64 and URL file_data input, PDF/Office/JSON/CSV/HTML/text cracking, parsingMode (default/text/json), dataToExtract. Image extraction from PDF/Word/Excel when `imageAction` is set (JPEG normalization, resize, EXIF rotation). |

### Summary

- **Implemented**: 7 skills (Text Split, Text Merge, Shaper, Conditional, Document Extraction, Web API, Azure OpenAI Embedding)
- **Local mode**: Azure OpenAI Embedding skill also supports `local://` ONNX mode — same skill, no Azure dependency
- **Not Implemented**: 13 skills (require Azure AI Services or are not yet added)

**Workaround**: Use the Custom Web API skill to call your own implementations of missing skills. See the [CustomSkillSample](../samples/CustomSkillSample/) for examples of PII detection, sentiment analysis, keyword extraction, and more.

## Local Embedding Limitations

The simulator supports local ONNX-based embedding generation via the `local://` URI scheme in `AzureOpenAIEmbeddingSkill`.

### Supported Models

| Model | Dimensions | Size | Notes |
| ----- | ---------- | ---- | ----- |
| `all-MiniLM-L6-v2` | 384 | ~80 MB | Default model |
| `bge-small-en-v1.5` | 384 | ~130 MB | - |
| `all-mpnet-base-v2` | 768 | ~420 MB | Higher quality |

### Limitations

- **English only** — all shipped models are English sentence-transformers
- **CPU inference** — no GPU acceleration (ONNX Runtime CPU provider only)
- **Model download required** — models are not bundled; use `scripts/Download-EmbeddingModel.ps1`
- **Custom models** — any ONNX-exported BERT model with `model.onnx` + `vocab.txt` can be used, but only tested with the three models above
- **Max 512 tokens** — input text exceeding `MaximumTokens` is truncated (configurable)
- **Dimensions fixed per model** — cannot change output dimensions (unlike Azure OpenAI text-embedding-3-* with MRL)

## Document Cracking Limitations

### Supported Formats

| Format | Support Level | Notes |
| ------ | ------------- | ----- |
| PDF | Good | Text extraction only, no OCR for images |
| Word (.docx) | Good | Text and basic structure |
| Excel (.xlsx) | Partial | Cell values only |
| JSON | Full | - |
| CSV | Full | - |
| Plain text | Full | - |
| HTML | Partial | Basic tag stripping |

### Unsupported Formats

- PowerPoint (.pptx) - not yet implemented
- Scanned PDFs (require OCR)
- Images (JPG, PNG, etc.)
- Encrypted/password-protected files
- Legacy Office formats (.doc, .xls, .ppt)

## Data Source Limitations

### Supported Data Sources

| Source | Type | Implementation |
| ------ | ---- | -------------- |
| Local File System | `filesystem` | Direct file access (simulator-only) |
| Azure Blob Storage | `azureblob` | Full support with connection string or Managed Identity |
| Azure Data Lake Gen2 | `adlsgen2` | Full support with hierarchical namespace |

### Authentication Methods

| Method | Blob Storage | ADLS Gen2 |
| ------ | ------------ | --------- |
| Connection String | ✅ | ✅ |
| Account Key | ✅ | ✅ |
| SAS Token | ✅ | ✅ |
| Managed Identity | ✅ | ✅ |

### Unsupported Data Sources

- Azure SQL Database
- Azure Cosmos DB
- Azure Table Storage
- SharePoint Online
- MySQL
- SQL Server

**Workaround**: Use the push API to manually index data from any source.

## Indexer Limitations

| Feature | Status | Notes |
| ------- | ------ | ----- |
| Scheduled runs | ✅ | Minimum 5 minutes, ISO 8601 intervals |
| On-demand runs | ✅ | - |
| Field mappings | ✅ | base64Encode, base64Decode, urlEncode, urlDecode, extractTokenAtPosition |
| Output field mappings | ✅ | - |
| Index projections | ✅ | One-to-many indexing, fan-out chunks to secondary index |
| Change detection | ✅ | High Water Mark policy (metadata_storage_last_modified or custom column) |
| Parsing modes | ⚠️ | `default`, `json`, `jsonArray` supported; `jsonLines` and `delimitedText` not implemented |
| Soft delete | ❌ | Model accepted but not processed during indexing |
| Parallel execution | ⚠️ | Semaphore-bounded parallelism within batches |
| Incremental enrichment | ❌ | Not supported |
| Enrichment cache | ❌ | Not supported |
| Knowledge store projections | ❌ | Not supported (index projections are supported) |

## Normalizer Limitations

Normalizers apply text transformations to keyword fields during filtering, sorting, and faceting. The simulator implements all Azure AI Search normalizers, including all 14 token filters and all 3 character filter types for custom normalizers.

### Predefined Normalizers

| Normalizer | Azure | Simulator | Notes |
| ---------- | ----- | --------- | ----- |
| `standard` | ✅ | ✅ | Lowercase + ASCII folding |
| `lowercase` | ✅ | ✅ | Converts to lowercase |
| `uppercase` | ✅ | ✅ | Converts to uppercase |
| `asciifolding` | ✅ | ✅ | Removes diacritics (keeps case) |
| `elision` | ✅ | ✅ | English contraction removal ('s, 't, 'll, etc.) |

### Token Filters (for Custom Normalizers)

| Filter | Azure | Simulator | Notes |
| ------ | ----- | --------- | ----- |
| `lowercase` | ✅ | ✅ | - |
| `uppercase` | ✅ | ✅ | - |
| `asciifolding` | ✅ | ✅ | Removes diacritics |
| `trim` | ✅ | ✅ | Removes leading/trailing whitespace |
| `elision` | ✅ | ✅ | English contraction removal |
| `arabic_normalization` | ✅ | ✅ | Normalizes Arabic orthography (alef variants, tatweel, diacritics) |
| `german_normalization` | ✅ | ✅ | Normalizes umlauts (ä→a, ö→o, ü→u) and ß→ss |
| `hindi_normalization` | ✅ | ✅ | Normalizes Devanagari nukta composites |
| `indic_normalization` | ✅ | ✅ | Removes zero-width joiners/non-joiners |
| `persian_normalization` | ✅ | ✅ | Normalizes Arabic keh/yeh to Persian equivalents |
| `scandinavian_normalization` | ✅ | ✅ | Normalizes interchangeable Scandinavian chars (æ→ä, ø→ö) |
| `scandinavian_folding` | ✅ | ✅ | Folds Scandinavian chars to ASCII (å→a, ä/æ→a, ö/ø→o) |
| `sorani_normalization` | ✅ | ✅ | Normalizes Sorani Kurdish text |
| `cjk_width` | ✅ | ✅ | Fullwidth→halfwidth ASCII, halfwidth→fullwidth Katakana |

### Character Filters (for Custom Normalizers)

| Filter | Azure | Simulator | Notes |
| ------ | ----- | --------- | ----- |
| `html_strip` | ✅ | ✅ | Removes HTML tags |
| `mapping` | ✅ | ✅ | Custom character mappings (source=>target) |
| `pattern_replace` | ✅ | ✅ | Regex-based replacements |

### Custom Normalizers

The simulator supports custom normalizers with the following configuration:

- **Token filters**: `lowercase`, `uppercase`, `asciifolding`, `trim`, `elision`, `arabic_normalization`, `cjk_width`, `german_normalization`, `hindi_normalization`, `indic_normalization`, `persian_normalization`, `scandinavian_folding`, `scandinavian_normalization`, `sorani_normalization`
- **Character filters**: `html_strip`, `mapping`, `pattern_replace`
- Custom normalizers can be defined in the index schema and will be validated

## Search Query Limitations

### OData Filter Limitations

Supported functions:

- `search.ismatch()` - Basic support
- `search.in()` - Full support
- `geo.distance()` - Basic support
- `any()` / `all()` - Full support

Not supported:

- `search.ismatchscoring()`
- Complex geo-spatial functions

### Query Limitations

- Maximum query length: 8,000 characters
- Maximum terms in a query: 100
- Regular expressions have limited complexity

### Facet Limitations

Supported:

- Value facets with `count:N`
- Interval facets with `interval:N` (numeric fields)
- Aggregation metrics `sum`, `min`, `max`, `avg` (`Edm.Int32`, `Edm.Int64`, `Edm.Double` fields, including sub-fields of a complex type)
- `default:` missing-value substitution for the aggregation metrics above (numeric default only)
- Facets on `Edm.ComplexType` sub-fields (e.g. `Tiles/PendingPaymentCount`) — sub-fields are indexed field-by-field under a path-qualified name

Not supported:

- The `cardinality` aggregation metric (and its `precisionThreshold` parameter)
- `sort`, `values`, and `timeoffset` facet parameters
- Facet hierarchies (`>` and `;` operators) and facet filters (`includeTermFilter` / `excludeTermFilter`)
- Facets on `Collection(Edm.ComplexType)` sub-fields (e.g. `Rooms/BaseRate` where `Rooms` is a collection) — only non-collection complex types are indexed field-by-field
- `$filter`/`$orderby` on complex-type sub-fields — only facets resolve field paths today

## Security Limitations

| Feature | Status |
| ------- | ------ |
| API keys | ✅ Supported |
| Entra ID authentication | ✅ Supported |
| Managed Identity (data sources) | ✅ Supported (system & user-assigned) |
| RBAC | ✅ Supported (6 Azure Search roles) |
| Key rotation | ⚠️ Manual only |
| IP restrictions | ❌ Not supported |
| Private endpoints | ❌ Not supported |
| Document-level security | ❌ Not supported |

## Service Statistics Limitations

The `GET /servicestats` endpoint returns resource counters and service limits. Because the simulator has no real quota system:

- **Usage values** (`documentCount`, `indexesCount`, `indexersCount`, `dataSourcesCount`, `storageSize`, `skillsetCount`, `synonymMaps`, `vectorIndexSize`) are computed from actual simulator state.
- **Quota values** and **limits** are hardcoded to Azure AI Search **Standard (S1) tier** defaults and are **not enforced**.
- `documentCount.quota` is `null` (unlimited), matching Azure's Standard tier behavior.

## Recommendations

### When to Use the Simulator

✅ **Good for:**

- Learning Azure AI Search concepts
- Prototyping search solutions
- Testing index schemas
- Developing without Azure costs
- CI/CD integration testing
- Offline development

### When NOT to Use the Simulator

❌ **Not suitable for:**

- Production workloads
- Performance benchmarking
- Testing AI-powered features
- Large-scale testing (>100K documents)
- Security testing
- Compliance testing

## Migration to Azure

When moving from the simulator to Azure AI Search:

1. **Index definitions**: Should work with no changes
2. **Queries**: Should be fully compatible
3. **Skillsets**: Should work with no changes, but Document Extraction will work much better with Azure native skills
4. **Local embeddings**: Replace `local://model-name` resource URIs with your Azure OpenAI endpoint
5. **Data sources**: Update connection strings to Azure resources if needed
6. **Authentication**: Update to Azure API keys or managed identity

---

*Last updated: February 18, 2026*
