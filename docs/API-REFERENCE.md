# Azure AI Search Simulator - API Reference

This document provides a detailed reference for all REST API endpoints supported by the Azure AI Search Simulator.

## Implementation Status

| Endpoint Category | Status | Notes |
| ----------------- | ------ | ----- |
| Index Operations | ✅ Implemented | Full CRUD support, statistics |
| Document Operations | ✅ Implemented | Upload, merge, mergeOrUpload, delete |
| Search | ✅ Implemented | Simple & Lucene syntax, vector search, hybrid search |
| Suggest/Autocomplete | ✅ Implemented | Basic prefix matching |
| Data Sources | ✅ Implemented | File system connector |
| Indexers | ✅ Implemented | Full CRUD, run, reset, status, scheduled execution |
| Document Cracking | ✅ Implemented | PDF, Word, Excel, HTML, JSON, CSV, TXT |
| Skillsets | ✅ Implemented | Text skills, embedding skill, custom Web API skill |

## Base URL

```http
https://localhost:7250
```

> **Note**: HTTPS is recommended for Azure SDK compatibility. HTTP is also available at `http://localhost:5250`.

## Authentication

The simulator supports three authentication methods. See [AUTHENTICATION.md](AUTHENTICATION.md) for details.

### API Key (Default)

```http
api-key: admin-key-12345
```

| Key Type | Default Value | Permissions |
| -------- | ------------ | ----------- |
| Admin Key | `admin-key-12345` | Full read/write access |
| Query Key | `query-key-67890` | Read-only search operations |

### Bearer Token (Simulated or Entra ID)

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

| Role | Permissions |
| ---- | ----------- |
| Search Service Contributor | Manage indexes, indexers, data sources, skillsets |
| Search Index Data Contributor | Upload/merge/delete documents |
| Search Index Data Reader | Search, suggest, autocomplete |

### Quick Token Generation

```http
GET /admin/token/quick/data-contributor
api-key: admin-key-12345
```

Returns a JWT with the specified role for local testing.

> **Note:** If both `api-key` and `Authorization: Bearer` are present, the API key takes precedence (matching Azure AI Search behavior).

## API Version

All requests require the `api-version` query parameter:

```text
?api-version=2025-09-01
```

### Supported API Versions

| Version | Status | Notes |
| ------- | ------ | ----- |
| `2025-09-01` | ✅ Supported | Latest stable - includes index description, debug subscores |
| `2024-07-01` | ✅ Supported | Previous stable - vector search, quantization |
| `2023-11-01` | ⚠️ Partial | Vector search, semantic ranking basics |

---

## Index Operations ✅

### Create Index

Creates a new search index.

```http
POST /indexes?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "hotels",
  "fields": [
    {
      "name": "hotelId",
      "type": "Edm.String",
      "key": true,
      "filterable": true
    },
    {
      "name": "hotelName",
      "type": "Edm.String",
      "searchable": true,
      "filterable": true,
      "sortable": true
    },
    {
      "name": "description",
      "type": "Edm.String",
      "searchable": true
    },
    {
      "name": "category",
      "type": "Edm.String",
      "searchable": true,
      "filterable": true,
      "facetable": true
    },
    {
      "name": "rating",
      "type": "Edm.Double",
      "filterable": true,
      "sortable": true,
      "facetable": true
    },
    {
      "name": "tags",
      "type": "Collection(Edm.String)",
      "searchable": true,
      "filterable": true,
      "facetable": true
    },
    {
      "name": "descriptionVector",
      "type": "Collection(Edm.Single)",
      "searchable": true,
      "dimensions": 1536,
      "vectorSearchProfile": "my-vector-profile"
    },
    {
      "name": "address",
      "type": "Edm.ComplexType",
      "fields": [
        {
          "name": "streetAddress",
          "type": "Edm.String",
          "searchable": true
        },
        {
          "name": "city",
          "type": "Edm.String",
          "searchable": true,
          "filterable": true,
          "facetable": true
        }
      ]
    }
  ],
  "vectorSearch": {
    "algorithms": [
      {
        "name": "my-hnsw",
        "kind": "hnsw",
        "hnswParameters": {
          "metric": "cosine"
        }
      }
    ],
    "profiles": [
      {
        "name": "my-vector-profile",
        "algorithm": "my-hnsw"
      }
    ]
  },
  "scoringProfiles": [],
  "suggesters": [
    {
      "name": "sg",
      "searchMode": "analyzingInfixMatching",
      "sourceFields": ["hotelName", "category"]
    }
  ]
}
```

**Response:** `201 Created`

---

### List Indexes

```http
GET /indexes?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "@odata.context": "https://localhost:7001/$metadata#indexes",
  "value": [
    {
      "name": "hotels",
      "fields": [...],
      "@odata.etag": "\"0x12345\""
    }
  ]
}
```

---

### Get Index

```http
GET /indexes/{indexName}?api-version=2024-07-01
api-key: <admin-key>
```

---

### Delete Index

```http
DELETE /indexes/{indexName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

---

### Get Index Statistics

Returns statistics for a search index including document count and storage size.

```http
GET /indexes/{indexName}/stats?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "@odata.context": "https://localhost:7250/$metadata#Microsoft.Azure.Search.V2024_07_01.IndexStatistics",
  "documentCount": 153951,
  "storageSize": 274189410,
  "vectorIndexSize": 0
}
```

| Field | Description |
| ----- | ----------- |
| `documentCount` | Number of documents in the index |
| `storageSize` | Size of the Lucene index storage in bytes |
| `vectorIndexSize` | Size of the HNSW vector index storage in bytes |

---

## Document Operations ✅

### Upload Documents

```http
POST /indexes/{indexName}/docs/index?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "value": [
    {
      "@search.action": "upload",
      "hotelId": "1",
      "hotelName": "Secret Point Motel",
      "description": "A great hotel",
      "category": "Budget",
      "rating": 4.5,
      "tags": ["pool", "wifi"]
    },
    {
      "@search.action": "mergeOrUpload",
      "hotelId": "2",
      "hotelName": "Twin Dome Motel",
      "category": "Budget"
    },
    {
      "@search.action": "delete",
      "hotelId": "3"
    }
  ]
}
```

**Actions:**

- `upload` - Insert new document (fails if exists)
- `merge` - Update existing document fields
- `mergeOrUpload` - Update if exists, otherwise insert
- `delete` - Remove document

**Response:**

```json
{
  "value": [
    {
      "key": "1",
      "status": true,
      "errorMessage": null,
      "statusCode": 201
    },
    {
      "key": "2",
      "status": true,
      "errorMessage": null,
      "statusCode": 200
    }
  ]
}
```

---

### Get Document

```http
GET /indexes/{indexName}/docs/{key}?api-version=2024-07-01
api-key: <query-key>
```

---

### Count Documents

```http
GET /indexes/{indexName}/docs/$count?api-version=2024-07-01
api-key: <query-key>
```

**Response:** `1234` (plain text number)

---

## Search Operations ✅

### Search Documents (POST)

```http
POST /indexes/{indexName}/docs/search?api-version=2024-07-01
Content-Type: application/json
api-key: <query-key>
```

**Request Body:**

```json
{
  "search": "luxury hotel pool",
  "searchMode": "all",
  "queryType": "simple",
  "searchFields": "hotelName,description",
  "select": "hotelId,hotelName,rating,category",
  "filter": "rating ge 4",
  "orderby": "rating desc",
  "top": 10,
  "skip": 0,
  "count": true,
  "facets": ["category,count:5", "rating,interval:1"],
  "highlight": "description",
  "highlightPreTag": "<em>",
  "highlightPostTag": "</em>"
}
```

**Query Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `search` | string | Search text (use `*` for all documents) |
| `searchMode` | string | `any` (default) or `all` |
| `queryType` | string | `simple` (default) or `full` |
| `searchFields` | string | Comma-separated field names |
| `select` | string | Fields to return |
| `filter` | string | OData filter expression |
| `orderby` | string | Sort expression |
| `top` | integer | Number of results (max 1000) |
| `skip` | integer | Results to skip |
| `count` | boolean | Include total count |
| `facets` | array | Facet specifications |
| `highlight` | string | Fields to highlight |
| `vectorQueries` | array | Vector query objects (see below) |

#### Vector Search Parameters

Add `vectorQueries` to perform vector or hybrid search:

```json
{
  "search": "luxury hotels",
  "vectorQueries": [
    {
      "kind": "vector",
      "vector": [0.01, 0.02, ...],
      "fields": "descriptionVector",
      "k": 10
    }
  ]
}
```

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `kind` | string | `"vector"` for raw vector input |
| `vector` | array | Array of floats (embedding) |
| `fields` | string | Vector field name(s) |
| `k` | integer | Number of nearest neighbors |

**Hybrid Search:** Include both `search` (text) and `vectorQueries` (vector) to combine results.

#### HNSW Vector Search Algorithm

The simulator uses HNSW (Hierarchical Navigable Small World) for efficient approximate nearest neighbor search:

- **O(log n) query time** instead of O(n) brute-force
- **High recall** (typically 95-99% accuracy)
- **Configurable parameters** (M, efConstruction, efSearch)
- **Automatic fallback** to brute-force when HNSW is disabled

**Vector Search Architecture:**

The simulator implements a dual-layer vector search system:

```text
┌─────────────────────────────────────────────────────────────┐
│                    IVectorSearchService                     │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────────┐  ┌─────────────────────────────┐   │
│  │   HnswIndexManager  │  │      VectorStore            │   │
│  │   (HNSW algorithm)  │  │   (Brute-force fallback)    │   │
│  └─────────────────────┘  └─────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**Filtered Vector Search:**

When filters are applied, the simulator uses post-filtering with oversampling:

1. Retrieve `k × oversampleMultiplier` candidates from HNSW
2. Filter candidates to those matching the OData filter
3. Return top-k filtered results
4. Fall back to brute-force if insufficient results

This ensures good recall even with selective filters.

**Score Calculation:**

Vector search results include both distance and score:

- **Distance**: Raw distance from HNSW (lower = closer)
- **Score**: Similarity score computed as `1 / (1 + distance)` (0-1 range, higher = more similar)

**Performance Considerations:**

| Use Case | Recommended Settings |
| -------- | -------------------- |
| Development | M=16, efConstruction=100, efSearch=50 |
| Balanced | M=16, efConstruction=200, efSearch=100 |
| High Recall | M=32, efConstruction=400, efSearch=200 |

**Disabling HNSW:**

To use brute-force cosine similarity instead of HNSW:

```json
{
  "VectorSearchSettings": {
    "UseHnsw": false
  }
}
```

#### Hybrid Search Score Fusion

When combining text and vector search results, the simulator supports two fusion methods:

**Reciprocal Rank Fusion (RRF)** (default)

RRF combines results based on their ranks rather than scores:

```text
RRF_score(d) = Σ 1 / (k + rank(d))
```

Where:

- `k` is a constant (default: 60) that controls rank-score distribution
- `rank(d)` is the document's position in each result list (1-indexed)
- Documents appearing in both text and vector results get higher scores

**Benefits of RRF:**

- No score normalization needed
- Works well when score distributions differ
- Documents in both result sets are boosted
- Simple and robust

**Weighted Score Fusion**

Alternatively, combine normalized scores with configurable weights:

```text
final_score = (text_weight × norm_text_score) + (vector_weight × norm_vector_score)
```

Text scores are normalized using min-max normalization. Vector scores are already in 0-1 range.

**Default weights:**

- `vectorWeight`: 0.7 (semantic similarity prioritized)
- `textWeight`: 0.3 (keyword matches)

**Configuration:**

```json
{
  "VectorSearchSettings": {
    "HybridSearchSettings": {
      "DefaultFusionMethod": "RRF",
      "RrfK": 60,
      "DefaultVectorWeight": 0.7,
      "DefaultTextWeight": 0.3
    }
  }
}
```

**Fusion Method Selection:**

| Method | Best For | Notes |
| ------ | -------- | ----- |
| RRF | General use | Robust, no tuning needed |
| Weighted | Score transparency | Requires weight tuning |

**Response:**
```json
{
  "@odata.context": "...",
  "@odata.count": 42,
  "@search.facets": {
    "category": [
      { "value": "Luxury", "count": 15 },
      { "value": "Budget", "count": 27 }
    ],
    "rating": [
      { "from": 4, "to": 5, "count": 30 }
    ]
  },
  "value": [
    {
      "@search.score": 1.234,
      "@search.highlights": {
        "description": ["A <em>luxury</em> <em>hotel</em> with <em>pool</em>"]
      },
      "hotelId": "1",
      "hotelName": "Grand Hotel",
      "rating": 4.8,
      "category": "Luxury"
    }
  ]
}
```

---

### Suggestions

```http
POST /indexes/{indexName}/docs/suggest?api-version=2024-07-01
Content-Type: application/json
api-key: <query-key>
```

**Request Body:**

```json
{
  "search": "sea",
  "suggesterName": "sg",
  "select": "hotelId,hotelName",
  "top": 5,
  "fuzzy": true
}
```

**Response:**

```json
{
  "value": [
    {
      "@search.text": "Seaside Resort",
      "hotelId": "5",
      "hotelName": "Seaside Resort"
    }
  ]
}
```

---

### Autocomplete

```http
POST /indexes/{indexName}/docs/autocomplete?api-version=2024-07-01
Content-Type: application/json
api-key: <query-key>
```

**Request Body:**

```json
{
  "search": "sea",
  "suggesterName": "sg",
  "autocompleteMode": "twoTerms",
  "fuzzy": true
}
```

---

## Indexer Operations ✅

### Create Indexer

```http
POST /indexers?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "hotel-indexer",
  "dataSourceName": "hotel-datasource",
  "targetIndexName": "hotels",
  "skillsetName": "hotel-skillset",
  "schedule": {
    "interval": "PT1H",
    "startTime": "2024-01-01T00:00:00Z"
  },
  "parameters": {
    "configuration": {
      "parsingMode": "default",
      "dataToExtract": "contentAndMetadata"
    }
  },
  "fieldMappings": [
    {
      "sourceFieldName": "metadata_storage_path",
      "targetFieldName": "hotelId",
      "mappingFunction": {
        "name": "base64Encode"
      }
    }
  ],
  "outputFieldMappings": [
    {
      "sourceFieldName": "/document/content",
      "targetFieldName": "description"
    }
  ]
}
```

---

### Run Indexer

```http
POST /indexers/{indexerName}/run?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `202 Accepted`

---

### Get Indexer Status

```http
GET /indexers/{indexerName}/status?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "name": "hotel-indexer",
  "status": "running",
  "lastResult": {
    "status": "success",
    "itemsProcessed": 100,
    "itemsFailed": 0,
    "startTime": "2024-01-15T10:00:00Z",
    "endTime": "2024-01-15T10:05:00Z"
  },
  "executionHistory": [...]
}
```

---

### Reset Indexer

Resets the change tracking state, causing a full re-index on next run.

```http
POST /indexers/{indexerName}/reset?api-version=2024-07-01
api-key: <admin-key>
```

---

## Data Source Operations ✅

### Create Data Source

```http
POST /datasources?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "hotel-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "DefaultEndpointsProtocol=file;LocalPath=./data/hotels"
  },
  "container": {
    "name": "documents",
    "query": "pdfs/"
  },
  "dataDeletionDetectionPolicy": {
    "@odata.type": "#Microsoft.Azure.Search.SoftDeleteColumnDeletionDetectionPolicy",
    "softDeleteColumnName": "IsDeleted",
    "softDeleteMarkerValue": "true"
  }
}
```

**Note:** The simulator uses a special connection string format for local files:

- `DefaultEndpointsProtocol=file;LocalPath=<path>` - Maps to local file system

---

## Document Cracking ✅

The simulator includes built-in document cracking capabilities to extract text and metadata from various file formats. This functionality is automatically invoked by indexers when processing documents from data sources.

### Supported File Formats

| Format | Extension(s) | Library | Features |
| ------ | ------------ | ------- | -------- |
| Plain Text | `.txt`, `.md` | Built-in | UTF-8/UTF-16 encoding detection |
| JSON | `.json` | Built-in | Extracts all string values, metadata fields |
| CSV/TSV | `.csv`, `.tsv` | Built-in | Auto delimiter detection, row/column extraction |
| HTML | `.html`, `.htm` | HtmlAgilityPack | Tag stripping, meta tag extraction |
| PDF | `.pdf` | PdfPig | Page-by-page extraction, document properties |
| Word | `.docx` | OpenXML | Paragraphs, tables, document properties |
| Excel | `.xlsx` | OpenXML | All sheets, shared strings |

### Extracted Metadata

When cracking documents, the following metadata is automatically extracted when available:

```json
{
  "content": "The extracted text content...",
  "metadata_title": "Document Title",
  "metadata_author": "Author Name",
  "metadata_creation_date": "2024-01-15T10:30:00Z",
  "metadata_last_modified": "2024-01-20T14:45:00Z",
  "metadata_page_count": 5,
  "metadata_word_count": 1250,
  "metadata_character_count": 7500,
  "metadata_language": "en"
}
```

### Content Type Detection

The cracker is selected based on:

1. **Content-Type header** (when available from the data source)
2. **File extension** (fallback)

| Content Type | Cracker Used |
| ------------ | ------------ |
| `text/plain` | PlainTextCracker |
| `text/markdown` | PlainTextCracker |
| `application/json` | JsonCracker |
| `text/csv` | CsvCracker |
| `text/tab-separated-values` | CsvCracker |
| `text/html` | HtmlCracker |
| `application/pdf` | PdfCracker |
| `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | WordDocCracker |
| `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | ExcelCracker |

### Limitations

- **PDF**: Text extraction quality depends on PDF structure; scanned PDFs without OCR layer won't extract text
- **Word/Excel**: Only `.docx`/`.xlsx` (Open XML) formats supported, not legacy `.doc`/`.xls`
- **Encoding**: Plain text files default to UTF-8 if BOM not present
- **Large files**: No streaming; entire file is loaded into memory

---

## Skillset Operations ✅

Skillsets define a sequence of skills that transform and enrich documents during indexing.

### Create Skillset

```http
POST /skillsets?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "hotel-skillset",
  "description": "Extract content and split into chunks",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Text.SplitSkill",
      "name": "split-skill",
      "description": "Split text into pages",
      "context": "/document",
      "inputs": [
        {
          "name": "text",
          "source": "/document/content"
        }
      ],
      "outputs": [
        {
          "name": "textItems",
          "targetName": "pages"
        }
      ],
      "textSplitMode": "pages",
      "maximumPageLength": 2000
    },
    {
      "@odata.type": "#Microsoft.Skills.Text.MergeSkill",
      "name": "merge-skill",
      "context": "/document",
      "inputs": [
        {
          "name": "text",
          "source": "/document/content"
        },
        {
          "name": "itemsToInsert",
          "source": "/document/metadata_title"
        }
      ],
      "outputs": [
        {
          "name": "mergedText",
          "targetName": "fullContent"
        }
      ],
      "insertPreTag": " ",
      "insertPostTag": " "
    },
    {
      "@odata.type": "#Microsoft.Skills.Util.ShaperSkill",
      "name": "shaper-skill",
      "context": "/document",
      "inputs": [
        {
          "name": "title",
          "source": "/document/metadata_title"
        },
        {
          "name": "content",
          "source": "/document/content"
        }
      ],
      "outputs": [
        {
          "name": "output",
          "targetName": "documentInfo"
        }
      ]
    },
    {
      "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
      "name": "custom-skill",
      "context": "/document",
      "uri": "https://my-function.azurewebsites.net/api/Translate",
      "httpMethod": "POST",
      "timeout": "PT30S",
      "batchSize": 10,
      "inputs": [
        {
          "name": "text",
          "source": "/document/content"
        }
      ],
      "outputs": [
        {
          "name": "translatedText",
          "targetName": "translatedContent"
        }
      ],
      "httpHeaders": {
        "x-functions-key": "your-function-key"
      }
    },
    {
      "@odata.type": "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill",
      "name": "embedding-skill",
      "context": "/document",
      "resourceUri": "https://your-openai.openai.azure.com",
      "deploymentId": "text-embedding-ada-002",
      "modelName": "text-embedding-ada-002",
      "inputs": [
        {
          "name": "text",
          "source": "/document/content"
        }
      ],
      "outputs": [
        {
          "name": "embedding",
          "targetName": "contentVector"
        }
      ]
    }
  ]
}
```

**Response:** `201 Created`

### Get Skillset

```http
GET /skillsets/{skillsetName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `200 OK` with skillset definition

### List Skillsets

```http
GET /skillsets?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "value": [
    { "name": "skillset-1", ... },
    { "name": "skillset-2", ... }
  ]
}
```

### Create or Update Skillset

```http
PUT /skillsets/{skillsetName}?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

### Delete Skillset

```http
DELETE /skillsets/{skillsetName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### Supported Skills

| Skill Type | Description | Status |
| ---------- | ----------- | ------ |
| `#Microsoft.Skills.Text.SplitSkill` | Split text into pages or sentences | ✅ |
| `#Microsoft.Skills.Text.MergeSkill` | Merge text fragments | ✅ |
| `#Microsoft.Skills.Util.ShaperSkill` | Restructure data | ✅ |
| `#Microsoft.Skills.Util.ConditionalSkill` | Conditional output | ✅ |
| `#Microsoft.Skills.Custom.WebApiSkill` | Call external REST API | ✅ |
| `#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill` | Generate embeddings | ✅ |

### Using Skillsets with Indexers

To use a skillset with an indexer, specify the `skillsetName` and `outputFieldMappings`:

```json
{
  "name": "my-indexer",
  "dataSourceName": "my-datasource",
  "targetIndexName": "my-index",
  "skillsetName": "my-skillset",
  "outputFieldMappings": [
    {
      "sourceFieldName": "/document/contentVector",
      "targetFieldName": "embedding"
    },
    {
      "sourceFieldName": "/document/pages",
      "targetFieldName": "chunks"
    }
  ]
}
```

### Azure OpenAI Configuration

To use the Azure OpenAI Embedding Skill, configure the API key in `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "ApiKey": "your-azure-openai-api-key"
  }
}
```

The skill's `resourceUri` and `deploymentId` are specified in the skill definition itself.

---

## Supported Field Types

| Type | Description | Example |
| ---- | ----------- | ------- |
| `Edm.String` | Text/string | `"hello world"` |
| `Edm.Int32` | 32-bit integer | `42` |
| `Edm.Int64` | 64-bit integer | `9223372036854775807` |
| `Edm.Double` | Double-precision float | `3.14159` |
| `Edm.Boolean` | True/false | `true` |
| `Edm.DateTimeOffset` | Date and time | `"2024-01-15T10:30:00Z"` |
| `Edm.GeographyPoint` | Lat/long coordinates | `{"type":"Point","coordinates":[-122.131577,47.678581]}` |
| `Edm.ComplexType` | Nested object | `{"street":"123 Main St","city":"Seattle"}` |
| `Collection(Edm.String)` | Array of strings | `["tag1","tag2"]` |
| `Collection(Edm.Single)` | Vector embeddings | `[0.01, 0.02, ..., 0.99]` |
| `Collection(Edm.*)` | Array of any type | `[1, 2, 3]` |

---

## OData Filter Syntax

### Comparison Operators

| Operator | Description | Example |
| -------- | ----------- | ------- |
| `eq` | Equal | `rating eq 5` |
| `ne` | Not equal | `category ne 'Budget'` |
| `gt` | Greater than | `rating gt 4` |
| `ge` | Greater than or equal | `rating ge 4` |
| `lt` | Less than | `rating lt 3` |
| `le` | Less than or equal | `rating le 3` |

### Logical Operators

| Operator | Example |
| -------- | ------- |
| `and` | `rating ge 4 and category eq 'Luxury'` |
| `or` | `category eq 'Budget' or category eq 'Economy'` |
| `not` | `not (rating lt 4)` |

### Functions

| Function | Example |
| -------- | ------- |
| `search.ismatch()` | `search.ismatch('pool', 'description')` |
| `search.in()` | `search.in(category, 'Budget,Economy')` |
| `geo.distance()` | `geo.distance(location, geography'POINT(-122.13 47.67)') le 10` |

### Collection Functions

| Function | Example |
| -------- | ------- |
| `any()` | `tags/any(t: t eq 'wifi')` |
| `all()` | `tags/all(t: t ne 'casino')` |

---

## Data Source Operations ✅

### Create Data Source

Creates a new data source.

```http
POST /datasources?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "local-files",
  "type": "filesystem",
  "credentials": {
    "connectionString": "c:\\data\\documents"
  },
  "container": {
    "name": "subfolder",
    "query": "*.txt"
  }
}
```

**Response:** `201 Created` with the created data source.

### Create or Update Data Source

```http
PUT /datasources/{dataSourceName}?api-version=2024-07-01
```

**Response:** `200 OK` (update) or `201 Created` (create).

### Get Data Source

```http
GET /datasources/{dataSourceName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "name": "local-files",
  "type": "filesystem",
  "credentials": {
    "connectionString": "c:\\data\\documents"
  },
  "container": {
    "name": "subfolder"
  },
  "@odata.etag": "\"abc123\""
}
```

### List Data Sources

```http
GET /datasources?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**
```json
{
  "value": [
    {
      "name": "local-files",
      "type": "filesystem",
      "container": {
        "name": "documents"
      }
    }
  ]
}
```

### Delete Data Source

```http
DELETE /datasources/{dataSourceName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### Supported Data Source Types

| Type | Description | Authentication |
| ---- | ----------- | -------------- |
| `filesystem` | Local file system (simulator-only) | Local path |
| `azureblob` | Azure Blob Storage | Connection string, Account Key, SAS, Managed Identity |
| `adlsgen2` | Azure Data Lake Storage Gen2 | Connection string, Account Key, SAS, Managed Identity |

### Data Source Examples

**Local File System:**

```json
{
  "name": "local-files",
  "type": "filesystem",
  "credentials": {
    "connectionString": "C:/data"
  },
  "container": {
    "name": "documents",
    "query": "*.pdf"
  }
}
```

**Azure Blob Storage (Connection String):**

```json
{
  "name": "blob-datasource",
  "type": "azureblob",
  "credentials": {
    "connectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "container": {
    "name": "documents",
    "query": "folder1/"
  }
}
```

**Azure Blob Storage (Managed Identity):**

```json
{
  "name": "blob-managed-identity",
  "type": "azureblob",
  "credentials": {
    "connectionString": "https://mystorageaccount.blob.core.windows.net"
  },
  "container": {
    "name": "documents"
  }
}
```

**ADLS Gen2 (Connection String):**

```json
{
  "name": "adls-datasource",
  "type": "adlsgen2",
  "credentials": {
    "connectionString": "DefaultEndpointsProtocol=https;AccountName=mydatalake;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "container": {
    "name": "filesystem1",
    "query": "data/raw/"
  }
}
```

**ADLS Gen2 (Managed Identity with DFS endpoint):**

```json
{
  "name": "adls-managed-identity",
  "type": "adlsgen2",
  "credentials": {
    "connectionString": "https://mydatalake.dfs.core.windows.net"
  },
  "container": {
    "name": "filesystem1"
  }
}
```

---

## Indexer Operations ✅

### Create Indexer

Creates a new indexer.

```http
POST /indexers?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "name": "my-indexer",
  "dataSourceName": "local-files",
  "targetIndexName": "documents-index",
  "schedule": {
    "interval": "PT1H"
  },
  "fieldMappings": [
    {
      "sourceFieldName": "metadata_storage_path",
      "targetFieldName": "id",
      "mappingFunction": {
        "name": "base64Encode"
      }
    }
  ],
  "parameters": {
    "batchSize": 100,
    "maxFailedItems": 10,
    "configuration": {
      "parsingMode": "default",
      "dataToExtract": "contentAndMetadata"
    }
  }
}
```

**Response:** `201 Created` with the created indexer.

### Create or Update Indexer

```http
PUT /indexers/{indexerName}?api-version=2024-07-01
```

### Get Indexer

```http
GET /indexers/{indexerName}?api-version=2024-07-01
api-key: <admin-key>
```

### List Indexers

```http
GET /indexers?api-version=2024-07-01
api-key: <admin-key>
```

### Delete Indexer

```http
DELETE /indexers/{indexerName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### Run Indexer

Triggers an immediate indexer run.

```http
POST /indexers/{indexerName}/run?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `202 Accepted`

### Reset Indexer

Resets the indexer tracking state, causing a full reindex on next run.

```http
POST /indexers/{indexerName}/reset?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### Get Indexer Status

Gets the current status and execution history.

```http
GET /indexers/{indexerName}/status?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "status": "unknown",
  "lastResult": {
    "status": "success",
    "startTime": "2024-01-15T10:00:00Z",
    "endTime": "2024-01-15T10:01:30Z",
    "itemsProcessed": 150,
    "itemsFailed": 2,
    "errors": [],
    "warnings": []
  },
  "executionHistory": [
    {
      "status": "success",
      "startTime": "2024-01-15T10:00:00Z",
      "endTime": "2024-01-15T10:01:30Z",
      "itemsProcessed": 150,
      "itemsFailed": 2
    }
  ],
  "limits": {
    "maxRunTime": "PT2H",
    "maxDocumentExtractionSize": 16777216,
    "maxDocumentContentCharactersToExtract": 64000
  }
}
```

### Field Mapping Functions

| Function | Description |
| -------- | ----------- |
| `base64Encode` | Encodes string to URL-safe Base64 |
| `base64Decode` | Decodes Base64 to string |
| `urlEncode` | URL-encodes string |
| `urlDecode` | URL-decodes string |
| `extractTokenAtPosition` | Extracts token at position (params: delimiter, position) |

### Indexer Parameters

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `batchSize` | int | Documents per batch (default: 1000) |
| `maxFailedItems` | int | Max failures before stopping (-1 = unlimited) |
| `maxFailedItemsPerBatch` | int | Max failures per batch |
| `configuration.parsingMode` | string | `default`, `json`, `jsonLines`, `jsonArray`, `delimitedText` |
| `configuration.dataToExtract` | string | `contentAndMetadata`, `storageMetadata` |
| `configuration.indexedFileNameExtensions` | string | Comma-separated extensions to include |
| `configuration.excludedFileNameExtensions` | string | Comma-separated extensions to exclude |

---

## Admin Endpoints

Administrative endpoints for token management and diagnostics.

### Generate Token

Generates a simulated JWT token for local testing.

```http
POST /admin/token?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "roles": ["Search Index Data Contributor", "Search Index Data Reader"],
  "subject": "test-app",
  "identityType": "app",
  "expiresInMinutes": 60
}
```

**Response:**

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2024-01-15T11:00:00Z",
  "tokenType": "Bearer"
}
```

### Quick Token Generation

Generates a token with a predefined role using shortcuts.

```http
GET /admin/token/quick/{role}?api-version=2024-07-01
api-key: <admin-key>
```

**Available Role Shortcuts:**

| Shortcut | Role |
| -------- | ---- |
| `owner` | Owner |
| `contributor` | Contributor |
| `reader` | Reader |
| `service-contributor` | Search Service Contributor |
| `data-contributor` | Search Index Data Contributor |
| `data-reader` | Search Index Data Reader |

### Validate Token

Validates and inspects a JWT token.

```http
POST /admin/token/validate?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response:**

```json
{
  "isValid": true,
  "claims": {
    "sub": "test-app",
    "roles": ["Search Index Data Contributor"],
    "exp": 1705316400,
    "iss": "https://simulator.local/"
  },
  "accessLevel": "IndexDataContributor"
}
```

### Get Auth Configuration Info

Returns current authentication configuration (non-sensitive).

```http
GET /admin/token/info?api-version=2024-07-01
api-key: <admin-key>
```

### Test Outbound Credentials

Tests the configured outbound credential settings.

```http
GET /admin/diagnostics/credentials/test?api-version=2024-07-01
api-key: <admin-key>
```

### Get Auth Diagnostics

Returns authentication configuration status.

```http
GET /admin/diagnostics/auth?api-version=2024-07-01
api-key: <admin-key>
```

### Acquire Token for Scope

Acquires a token for an external Azure resource.

```http
POST /admin/diagnostics/credentials/token?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

**Request Body:**

```json
{
  "scope": "https://storage.azure.com/.default"
}
```

---

## Error Responses

All errors follow the OData error format:

```json
{
  "error": {
    "code": "InvalidRequest",
    "message": "The request is invalid.",
    "details": [
      {
        "code": "FieldNotFound",
        "message": "Field 'unknownField' is not defined in the index schema."
      }
    ]
  }
}
```

### Common Error Codes

| Code | HTTP Status | Description |
| ---- | ----------- | ----------- |
| `InvalidApiKey` | 401 | Missing or invalid API key |
| `Forbidden` | 403 | Key doesn't have permission |
| `IndexNotFound` | 404 | Index doesn't exist |
| `DocumentNotFound` | 404 | Document not found |
| `InvalidRequest` | 400 | Malformed request |
| `ValidationError` | 400 | Schema validation failed |
| `IndexerExecutionError` | 500 | Indexer run failed |

---

## Rate Limits

The simulator implements soft rate limits for testing purposes:

| Limit | Value |
| ----- | ----- |
| Max requests/second | 100 |
| Max batch size | 1000 documents |
| Max query results | 1000 |
| Max facet values | 100 |

---

*API Reference Version: 1.0*
