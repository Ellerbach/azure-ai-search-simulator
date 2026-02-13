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
| Facets | Count and interval facets |
| Highlighting | Hit highlighting in search results |
| Autocomplete | Term completion suggestions |
| Suggestions | Type-ahead suggestions |
| Indexers | Automated document ingestion |
| Data sources | Local file system as blob storage |
| Skillsets | Utility skills (see below) |
| **Azure OpenAI Embedding** | Requires Azure OpenAI endpoint |
| API key authentication | Admin and query keys |

### ⚠️ Partially Supported

| Feature | Limitation |
| ------- | ---------- |
| Scoring profiles | Basic profiles supported, some functions may differ |
| Analyzers | Built-in Lucene analyzers only, no language-specific Microsoft analyzers |
| Synonym maps | Not yet implemented |
| Custom analyzers | Basic tokenizers and filters only |
| CORS | Simplified implementation |
| Service statistics | Basic stats only |

### ❌ Not Supported

| Feature | Reason |
| ------- | ------ |
| **Semantic search** | Requires Azure AI models |
| **Semantic ranking** | Requires Azure AI models |
| **Pre-filtering for vectors** | HNSW does not support native filtering; uses post-filter |
| **Knowledge stores** | Complex Azure Storage integration |
| **AI enrichment skills** | OCR, Entity Recognition, etc. require Azure AI Services |
| **Managed Identity** | Azure-specific security feature |
| **Private endpoints** | Azure networking feature |
| **Customer-managed keys** | Azure Key Vault integration |
| **Debug sessions (skillset)** | Complex debugging infrastructure |
| **Index aliases** | Not yet implemented |
| **Replica/partition scaling** | Single-instance only |

## API Compatibility

### Supported API Version

- **Target version**: `2024-07-01`
- Most requests compatible with `2023-11-01` through `2024-07-01`

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
| Azure OpenAI Embedding | ✅ Full | Requires Azure OpenAI endpoint config |

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
| `#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill` | Azure OpenAI Embedding | ✅ | ✅ | Requires Azure OpenAI endpoint |
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
| `#Microsoft.Skills.Util.DocumentExtractionSkill` | Document Extraction | ✅ | ⚠️ | PDF, Office, JSON, text only and very basic |

### Summary

- **Implemented**: 6 skills (Text Split, Text Merge, Shaper, Conditional, Web API, Azure OpenAI Embedding)
- **Not Implemented**: 14 skills (require Azure AI Services or are not yet added)

**Workaround**: Use the Custom Web API skill to call your own implementations of missing skills. See the [CustomSkillSample](../samples/CustomSkillSample/) for examples of PII detection, sentiment analysis, keyword extraction, and more.

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
| Scheduled runs | ✅ | Minimum 5 minutes |
| On-demand runs | ✅ | - |
| Field mappings | ✅ | Basic functions |
| Output field mappings | ✅ | - |
| Change detection | ⚠️ | File timestamp only |
| Soft delete | ⚠️ | Metadata-based only |
| Parallel execution | ⚠️ | Limited |
| Incremental enrichment | ❌ | Not supported |
| Enrichment cache | ❌ | Not supported |

## Normalizer Limitations

Normalizers apply text transformations to keyword fields during filtering, sorting, and faceting. The simulator implements most of the Azure AI Search normalizers.

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
| `arabic_normalization` | ✅ | ❌ | Language-specific |
| `german_normalization` | ✅ | ❌ | Language-specific |
| `hindi_normalization` | ✅ | ❌ | Language-specific |
| `indic_normalization` | ✅ | ❌ | Language-specific |
| `persian_normalization` | ✅ | ❌ | Language-specific |
| `scandinavian_normalization` | ✅ | ❌ | Language-specific |
| `scandinavian_folding` | ✅ | ❌ | Language-specific |
| `sorani_normalization` | ✅ | ❌ | Language-specific |
| `cjk_width` | ✅ | ❌ | CJK width normalization |

### Character Filters (for Custom Normalizers)

| Filter | Azure | Simulator | Notes |
| ------ | ----- | --------- | ----- |
| `html_strip` | ✅ | ✅ | Removes HTML tags |
| `mapping` | ✅ | ✅ | Custom character mappings (source=>target) |
| `pattern_replace` | ✅ | ✅ | Regex-based replacements |

### Custom Normalizers

The simulator supports custom normalizers with the following configuration:

- **Token filters**: `lowercase`, `uppercase`, `asciifolding`, `trim`, `elision`
- **Character filters**: `html_strip`, `mapping`, `pattern_replace`
- Custom normalizers can be defined in the index schema and will be validated

**Note**: Language-specific normalizers (Arabic, German, Hindi, etc.) are not implemented. For these languages, consider pre-processing your data before indexing.

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

## Security Limitations

| Feature | Status |
| ------- | ------ |
| API keys | ✅ Supported |
| Key rotation | ⚠️ Manual only |
| RBAC | ❌ Not supported |
| Managed Identity | ❌ Not supported |
| IP restrictions | ❌ Not supported |
| Private endpoints | ❌ Not supported |
| Document-level security | ❌ Not supported |

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

1. **Index definitions**: Should work with minimal changes
2. **Queries**: Should be fully compatible
3. **Skillsets**: Replace Document Extraction with Azure native skills
4. **Data sources**: Update connection strings to Azure resources
5. **Authentication**: Update to Azure API keys or managed identity

---

*Last updated: February 13, 2026*
