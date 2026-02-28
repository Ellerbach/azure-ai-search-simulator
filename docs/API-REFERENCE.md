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
| Skillsets | ✅ Implemented | Text skills, embedding skill, custom Web API skill, index projections |
| Synonym Maps | ✅ Implemented | Full CRUD, Solr format, query-time expansion |
| Service Statistics | ✅ Implemented | Counters and limits (quotas use S1 defaults) |

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

### [Create Index](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/create)

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
  "scoringProfiles": [
    {
      "name": "boostRating",
      "text": {
        "weights": { "hotelName": 3, "description": 1 }
      },
      "functions": [
        {
          "type": "magnitude",
          "fieldName": "rating",
          "boost": 5,
          "interpolation": "linear",
          "magnitude": {
            "boostingRangeStart": 0,
            "boostingRangeEnd": 5
          }
        }
      ],
      "functionAggregation": "sum"
    }
  ],
  "defaultScoringProfile": "boostRating",
  "similarity": {
    "@odata.type": "#Microsoft.Azure.Search.BM25Similarity",
    "k1": 1.2,
    "b": 0.75
  },
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

### [List Indexes](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/list)

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

### [Get Index](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/get)

```http
GET /indexes/{indexName}?api-version=2024-07-01
api-key: <admin-key>
```

---

### [Delete Index](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/delete)

```http
DELETE /indexes/{indexName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

---

### [Create or Update Index](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/create-or-update)

Creates a new index or updates an existing one.

```http
PUT /indexes/{indexName}?api-version=2024-07-01&allowIndexDowntime=true
Content-Type: application/json
api-key: <admin-key>
```

**Query Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `allowIndexDowntime` | bool | `false` | When `true`, allows updates that require the index to be temporarily offline. Required for updating BM25 similarity parameters (`k1`, `b`) on an existing index. |

**Restrictions on update:**

- The similarity algorithm `@odata.type` **cannot be changed** on an existing index (returns `400 Bad Request`).
- BM25 parameters (`k1`, `b`) can be updated only when `allowIndexDowntime=true`.
- Fields can be added but existing fields cannot be removed.

---

### [Get Index Statistics](https://learn.microsoft.com/en-us/rest/api/searchservice/indexes/get-statistics)

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

### [Upload Documents](https://learn.microsoft.com/en-us/rest/api/searchservice/documents)

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

### [Get Document](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/get)

```http
GET /indexes/{indexName}/docs/{key}?api-version=2024-07-01
api-key: <query-key>
```

---

### [Count Documents](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/count)

```http
GET /indexes/{indexName}/docs/$count?api-version=2024-07-01
api-key: <query-key>
```

**Response:** `1234` (plain text number)

---

## Search Operations ✅

### [Search Documents (POST)](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/search-post)

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
| `scoringProfile` | string | Name of a scoring profile to evaluate (overrides `defaultScoringProfile`) |
| `scoringParameters` | array | Values for scoring functions, e.g. `["tagParam-luxury,boutique"]` |
| `scoringStatistics` | string | `"local"` (default) or `"global"`. Accepted for compatibility; simulator always uses local statistics |
| `vectorQueries` | array | Vector query objects (see below) |
| `debug` | string | Debug mode for search diagnostics (see below) |
| `featuresMode` | string | When `"enabled"`, returns per-field BM25 scoring features in `@search.features` (see below) |

#### featuresMode Parameter

The `featuresMode` parameter provides per-field BM25 scoring breakdown for each search result. This is useful for understanding why certain documents rank higher or lower and how different fields contribute to the overall score.

**Supported values:**

| Value | Description |
| ----- | ----------- |
| `"none"` | No feature-level scoring details (default) |
| `"enabled"` | Returns detailed scoring breakdown per searchable field |

When enabled, each result includes an `@search.features` object with entries for each matching searchable field:

```json
{
  "@search.score": 3.0860271,
  "@search.features": {
    "description": {
      "uniqueTokenMatches": 2.0,
      "similarityScore": 3.0860272,
      "termFrequency": 2.0
    },
    "tags": {
      "uniqueTokenMatches": 1.0,
      "similarityScore": 1.1271671,
      "termFrequency": 1.0
    }
  }
}
```

- **`uniqueTokenMatches`**: Number of unique search terms found in the field
- **`similarityScore`**: BM25 similarity score for this field against the query
- **`termFrequency`**: Total number of times the search terms appear in the field

> **Note:** Only fields where at least one search term matches are included. Use `searchFields` to restrict which fields are evaluated.

#### Debug Parameter

The `debug` parameter enables diagnostic information in the search response. It returns detailed information about how results were scored and ranked.

> **Note:** In Azure AI Search, the `debug` parameter was introduced in API version `2025-05-01-preview`. The simulator supports it on all API versions for convenience.

**Supported values:**

| Value | Description |
| ----- | ----------- |
| `disabled` | No debug info (default) |
| `semantic` | Debug info for semantic ranking |
| `vector` | Debug info for vector/hybrid search subscores |
| `queryRewrites` | Debug info for query rewrites |
| `innerHits` | Debug info for inner hits in complex types |
| `all` | All debug info |

Multiple modes can be combined with `|`, e.g. `"semantic|vector"`.

**Example request with debug:**

```json
{
  "search": "luxury hotel",
  "vectorQueries": [
    {
      "kind": "vector",
      "vector": [0.1, 0.2, 0.3],
      "fields": "contentVector",
      "k": 5
    }
  ],
  "debug": "vector"
}
```

When debug is enabled, the response includes:

- **`@search.debug`** (response-level): Query-level debug info including parsed queries, timing, and simulator-specific diagnostics.
- **`@search.documentDebugInfo`** (per-document): Breakdown of subscores per document, including text BM25 scores and vector similarity scores per field.

**Debug response example:**

```json
{
  "@search.debug": {
    "queryRewrites": null,
    "simulator.parsedQuery": "+title:luxury +title:hotel",
    "simulator.parsedFilter": null,
    "simulator.isHybridSearch": true,
    "simulator.textSearchTimeMs": 5.2,
    "simulator.vectorSearchTimeMs": 3.1,
    "simulator.totalTimeMs": 12.5,
    "simulator.textMatchCount": 15,
    "simulator.vectorMatchCount": 10,
    "simulator.scoreFusionMethod": "WeightedAverage",
    "simulator.searchableFields": ["title", "description"]
  },
  "value": [
    {
      "@search.score": 0.85,
      "@search.documentDebugInfo": {
        "vectors": {
          "subscores": {
            "text": { "searchScore": 3.14 },
            "documentBoost": 1.0,
            "vectors": {
              "contentVector": {
                "searchScore": 0.85,
                "vectorSimilarity": 0.92
              }
            }
          }
        }
      },
      "id": "1",
      "title": "Grand Luxury Hotel"
    }
  ]
}
```

> **Note:** Properties prefixed with `simulator.` are specific to this simulator and are not present in the official Azure AI Search API. The `@search.documentDebugInfo` and `@search.debug.queryRewrites` structures match the official API.

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

#### Similarity Configuration

The similarity algorithm controls how text search relevance scores are computed. Configure it on the index definition.

**Supported algorithms:**

| Algorithm | `@odata.type` | Description |
|-----------|---------------|-------------|
| **BM25Similarity** (default) | `#Microsoft.Azure.Search.BM25Similarity` | Okapi BM25 with tunable `k1` and `b` parameters. Scores are unbounded. |
| **ClassicSimilarity** | `#Microsoft.Azure.Search.ClassicSimilarity` | Legacy TF-IDF scoring. Scores are in the 0–1 range. No tunable parameters. |

**BM25 parameters:**

| Parameter | Type | Default | Range | Description |
|-----------|------|---------|-------|-------------|
| `k1` | double | `1.2` | ≥ 0 (no upper bound) | Controls term frequency saturation. `0` = binary match, higher values increase impact of repeated terms. |
| `b` | double | `0.75` | 0.0 – 1.0 | Controls document length normalization. `0` = no normalization, `1` = fully normalized. |

**Index definition example:**

```json
{
  "similarity": {
    "@odata.type": "#Microsoft.Azure.Search.BM25Similarity",
    "k1": 1.5,
    "b": 0.5
  }
}
```

**ClassicSimilarity example:**

```json
{
  "similarity": {
    "@odata.type": "#Microsoft.Azure.Search.ClassicSimilarity"
  }
}
```

**Key rules:**

- If `similarity` is null or omitted, BM25 with default parameters is used.
- The `@odata.type` is **immutable after index creation** — attempting to change it returns `400 Bad Request`.
- BM25 parameters (`k1`, `b`) can be updated via Create-or-Update with `allowIndexDowntime=true`.
- ClassicSimilarity does not accept `k1` or `b` parameters.
- Invalid parameter ranges (negative `k1`, `b` outside 0–1) return `400 Bad Request`.

#### Scoring Profiles

Scoring profiles boost document relevance based on field values. Define profiles in the index, then activate them via `defaultScoringProfile` or the `scoringProfile` search parameter.

**Index definition example:**

```json
{
  "scoringProfiles": [
    {
      "name": "boostByRating",
      "text": {
        "weights": {
          "hotelName": 3,
          "description": 1
        }
      },
      "functions": [
        {
          "type": "magnitude",
          "fieldName": "rating",
          "boost": 5,
          "interpolation": "linear",
          "magnitude": {
            "boostingRangeStart": 0,
            "boostingRangeEnd": 5,
            "constantBoostBeyondRange": true
          }
        }
      ],
      "functionAggregation": "sum"
    }
  ],
  "defaultScoringProfile": "boostByRating"
}
```

**Supported scoring function types:**

| Function | Field Type | Description |
| -------- | ---------- | ----------- |
| `freshness` | `Edm.DateTimeOffset` | Boost based on recency; decays over `boostingDuration` (ISO 8601) |
| `magnitude` | `Edm.Double`, `Edm.Int32`, `Edm.Int64` | Boost within a numeric range |
| `distance` | `Edm.GeographyPoint` | Boost by proximity to a reference point (Haversine) |
| `tag` | `Collection(Edm.String)`, `Edm.String` | Boost when field values match scoring parameter tags |

**Interpolation modes:** `linear` (default), `constant`, `quadratic`, `logarithmic`

> **Note:** Tag functions only support `linear` and `constant` interpolation.

**Aggregation modes:** `sum` (default), `average`, `minimum`, `maximum`, `firstMatching`

**Search request with scoring profile:**

```json
{
  "search": "luxury hotel",
  "scoringProfile": "boostByRating",
  "scoringParameters": [
    "tagParam-luxury,boutique"
  ]
}
```

The `scoringParameters` array provides values for `tag` and `distance` functions. Format for tag: `paramName-value1,value2`. Format for distance: `paramName--longitude,latitude` (note the double dash separator).

When `debug` is enabled, the `@search.documentDebugInfo` includes a `documentBoost` value reflecting the combined scoring profile boost applied to each document.

**Validation:**

- Profiles with invalid field references, unsupported field types for functions, or non-filterable fields are rejected at index creation time.
- Function `boost` must be non-zero and not equal to `1.0`. Negative values are allowed (to demote documents).
- Tag functions only accept `linear` or `constant` interpolation.
- Maximum 100 scoring profiles per index.
- Requesting a non-existent `scoringProfile` in a search returns `400 Bad Request`.

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

### [Search Documents (GET)](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/search-get)

```http
GET /indexes/{indexName}/docs?api-version=2024-07-01&search={text}&$filter={filter}&$select={fields}&$orderby={sort}&$top={n}&$skip={n}&$count={bool}&highlight={fields}&searchMode={mode}&queryType={type}&scoringProfile={name}&scoringParameter={param}&scoringStatistics={scope}&debug={mode}
api-key: <query-key>
```

All search parameters can be passed as query string parameters. Use `scoringProfile` for the profile name and `scoringParameter` (repeated) for each scoring parameter value. The `debug` parameter accepts the same values as in the POST body.

**Example:**

```http
GET /indexes/hotels/docs?api-version=2024-07-01&search=luxury&debug=all
api-key: <query-key>
```

---

### [Suggestions](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/suggest-post)

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

### [Autocomplete](https://learn.microsoft.com/en-us/rest/api/searchservice/documents/autocomplete-post)

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

### [Create Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/create)

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

### [Run Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/run)

```http
POST /indexers/{indexerName}/run?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `202 Accepted`

---

### [Get Indexer Status](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/get-status)

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

### [Reset Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/reset)

Resets the change tracking state, causing a full re-index on next run.

```http
POST /indexers/{indexerName}/reset?api-version=2024-07-01
api-key: <admin-key>
```

---

## Data Source Operations ✅

### [Create Data Source](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/create)

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

### [Create Skillset](https://learn.microsoft.com/en-us/rest/api/searchservice/skillsets/create)

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

### [Get Skillset](https://learn.microsoft.com/en-us/rest/api/searchservice/skillsets/get)

```http
GET /skillsets/{skillsetName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `200 OK` with skillset definition

### [List Skillsets](https://learn.microsoft.com/en-us/rest/api/searchservice/skillsets/list)

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

### [Create or Update Skillset](https://learn.microsoft.com/en-us/rest/api/searchservice/skillsets/create-or-update)

```http
PUT /skillsets/{skillsetName}?api-version=2024-07-01
Content-Type: application/json
api-key: <admin-key>
```

### [Delete Skillset](https://learn.microsoft.com/en-us/rest/api/searchservice/skillsets/delete)

```http
DELETE /skillsets/{skillsetName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### Supported Skills

| Skill Type | Description | Status |
| ---------- | ----------- | ------ |
| [`#Microsoft.Skills.Text.SplitSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-skill-textsplit) | Split text into pages or sentences | ✅ |
| [`#Microsoft.Skills.Text.MergeSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-skill-textmerger) | Merge text fragments | ✅ |
| [`#Microsoft.Skills.Util.ShaperSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-skill-shaper) | Restructure data | ✅ |
| [`#Microsoft.Skills.Util.ConditionalSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-skill-conditional) | Conditional output | ✅ |
| [`#Microsoft.Skills.Custom.WebApiSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-custom-skill-web-api) | Call external REST API | ✅ |
| [`#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill`](https://learn.microsoft.com/en-us/azure/search/cognitive-search-skill-azure-openai-embedding) | Generate embeddings | ✅ |

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

### [Index Projections (One-to-Many Indexing)](https://learn.microsoft.com/en-us/azure/search/search-how-to-define-index-projections)

Skillsets support an `indexProjections` property that enables one-to-many indexing — fanning out enriched child elements (e.g., chunks) into separate search documents in a secondary index.

This is useful when a skill such as `TextSplitSkill` produces an array of chunks and each chunk should become its own searchable document.

```json
{
  "name": "chunking-skillset",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Text.SplitSkill",
      "name": "split-into-chunks",
      "context": "/document",
      "textSplitMode": "pages",
      "maximumPageLength": 500,
      "inputs": [
        { "name": "text", "source": "/document/content" }
      ],
      "outputs": [
        { "name": "textItems", "targetName": "chunks" }
      ]
    }
  ],
  "indexProjections": {
    "selectors": [
      {
        "targetIndexName": "chunks-index",
        "parentKeyFieldName": "parent_id",
        "sourceContext": "/document/chunks/*",
        "mappings": [
          { "name": "chunk_content", "source": "/document/chunks/*" },
          { "name": "title", "source": "/document/metadata_storage_name" }
        ]
      }
    ],
    "parameters": {
      "projectionMode": "skipIndexingParentDocuments"
    }
  }
}
```

**Key properties:**

| Property | Description |
| -------- | ----------- |
| `selectors[].targetIndexName` | The secondary index to receive projected documents |
| `selectors[].parentKeyFieldName` | Field in the child document that stores the parent document key |
| `selectors[].sourceContext` | Enrichment path with wildcard (e.g., `/document/chunks/*`) that determines fan-out |
| `selectors[].mappings[]` | Field mappings using `name` (target field) and `source` (enrichment path) |
| `parameters.projectionMode` | `"skipIndexingParentDocuments"` or `"includeIndexingParentDocuments"` (default) |

**Projection modes:**

- `skipIndexingParentDocuments` — Only projected child documents are indexed. The parent document is not sent to any index.
- `includeIndexingParentDocuments` (default) — Both the parent document (to the indexer's `targetIndexName`) and the child documents (to each selector's `targetIndexName`) are indexed.

**Projected key format:** Each child document receives a key in the format `{parentKey}_{contextSegment}_{index}` (e.g., `doc1_chunks_0`, `doc1_chunks_2`).

See [index-projection-sample.http](../samples/index-projection-sample.http) for a complete walkthrough.

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

## Synonym Map Operations ✅

Synonym maps define synonym rules that expand search queries at query time. Fields can reference synonym maps via the `synonymMaps` property. Only the Apache Solr synonym format is supported.

### [Create Synonym Map](https://learn.microsoft.com/en-us/rest/api/searchservice/synonym-maps/create)

```http
POST /synonymmaps?api-version=2024-07-01
Content-Type: application/json
api-key: your-admin-key

{
  "name": "my-synonym-map",
  "format": "solr",
  "synonyms": "usa, united states, america\nautomobile => car, vehicle"
}
```

**Response:** `201 Created` with the synonym map definition including `@odata.etag`.

### [Get Synonym Map](https://learn.microsoft.com/en-us/rest/api/searchservice/synonym-maps/get)

```http
GET /synonymmaps/{synonymMapName}?api-version=2024-07-01
api-key: your-admin-key
```

**Response:** `200 OK` with the synonym map definition.

### [List Synonym Maps](https://learn.microsoft.com/en-us/rest/api/searchservice/synonym-maps/list)

```http
GET /synonymmaps?api-version=2024-07-01
api-key: your-admin-key
```

**Response:** `200 OK` with `{ "value": [...] }`.

### [Create or Update Synonym Map](https://learn.microsoft.com/en-us/rest/api/searchservice/synonym-maps/create-or-update)

```http
PUT /synonymmaps/{synonymMapName}?api-version=2024-07-01
Content-Type: application/json
api-key: your-admin-key

{
  "name": "my-synonym-map",
  "format": "solr",
  "synonyms": "usa, united states, america\nautomobile => car, vehicle"
}
```

**Response:** `200 OK` (updated) or `201 Created` (new).

### [Delete Synonym Map](https://learn.microsoft.com/en-us/rest/api/searchservice/synonym-maps/delete)

```http
DELETE /synonymmaps/{synonymMapName}?api-version=2024-07-01
api-key: your-admin-key
```

**Response:** `204 No Content`.

### Synonym Rule Format (Solr)

| Format | Example | Behavior |
| ------ | ------- | -------- |
| Equivalent | `usa, united states, america` | Bidirectional: searching any term finds documents with any of the others |
| Explicit mapping | `automobile => car, vehicle` | Unidirectional: searching "automobile" also finds "car" and "vehicle", but not vice versa |

Lines starting with `#` are treated as comments. Each rule is on a separate line.

### Using Synonym Maps with Index Fields

To enable synonym expansion on a field, reference the synonym map in the field's `synonymMaps` property when creating or updating an index:

```json
{
  "name": "my-index",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    {
      "name": "description",
      "type": "Edm.String",
      "searchable": true,
      "synonymMaps": ["my-synonym-map"]
    }
  ]
}
```

When a search query matches a term in the synonym map on a field with `synonymMaps` configured, the query is automatically expanded with the synonym terms.

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

### [Create Data Source](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/create)

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

### [Create or Update Data Source](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/create-or-update)

```http
PUT /datasources/{dataSourceName}?api-version=2024-07-01
```

**Response:** `200 OK` (update) or `201 Created` (create).

### [Get Data Source](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/get)

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

### [List Data Sources](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/list)

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

### [Delete Data Source](https://learn.microsoft.com/en-us/rest/api/searchservice/data-sources/delete)

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

### [Create Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/create)

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

### [Create or Update Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/create-or-update)

```http
PUT /indexers/{indexerName}?api-version=2024-07-01
```

### [Get Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/get)

```http
GET /indexers/{indexerName}?api-version=2024-07-01
api-key: <admin-key>
```

### [List Indexers](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/list)

```http
GET /indexers?api-version=2024-07-01
api-key: <admin-key>
```

### [Delete Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/delete)

```http
DELETE /indexers/{indexerName}?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### [Run Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/run)

Triggers an immediate indexer run.

```http
POST /indexers/{indexerName}/run?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `202 Accepted`

### [Reset Indexer](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/reset)

Resets the indexer tracking state, causing a full reindex on next run.

```http
POST /indexers/{indexerName}/reset?api-version=2024-07-01
api-key: <admin-key>
```

**Response:** `204 No Content`

### [Get Indexer Status](https://learn.microsoft.com/en-us/rest/api/searchservice/indexers/get-status)

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

## Service Statistics

Returns service-level resource counters and limits.

### [Get Service Statistics](https://learn.microsoft.com/en-us/rest/api/searchservice/get-service-statistics)

```http
GET /servicestats?api-version=2024-07-01
api-key: <admin-key>
```

**Response:**

```json
{
  "@odata.context": "https://localhost:7250/$metadata#Microsoft.Azure.Search.V2024_07_01.ServiceStatistics",
  "counters": {
    "documentCount": { "usage": 153956, "quota": null },
    "indexesCount": { "usage": 2, "quota": 15 },
    "indexersCount": { "usage": 1, "quota": 15 },
    "dataSourcesCount": { "usage": 1, "quota": 15 },
    "storageSize": { "usage": 274215358, "quota": 16106127360 },
    "synonymMaps": { "usage": 0, "quota": 3 },
    "skillsetCount": { "usage": 0, "quota": 15 },
    "vectorIndexSize": { "usage": 0, "quota": 5368709120 }
  },
  "limits": {
    "maxStoragePerIndex": 16106127360,
    "maxFieldsPerIndex": 1000,
    "maxFieldNestingDepthPerIndex": 10,
    "maxComplexCollectionFieldsPerIndex": 40,
    "maxComplexObjectsInCollectionsPerDocument": 3000
  }
}
```

**Counter Details:**

| Counter | Usage | Quota |
| ------- | ----- | ----- |
| `documentCount` | Actual total across all indexes | `null` (unlimited, same as Azure) |
| `indexesCount` | Actual count | Hardcoded S1 default (15) |
| `indexersCount` | Actual count | Hardcoded S1 default (15) |
| `dataSourcesCount` | Actual count | Hardcoded S1 default (15) |
| `storageSize` | Actual Lucene index storage in bytes | Hardcoded S1 default (~15 GB) |
| `synonymMaps` | Actual count | Hardcoded S1 default (3) |
| `skillsetCount` | Actual count | Hardcoded S1 default (15) |
| `vectorIndexSize` | Actual HNSW index size in bytes | Hardcoded S1 default (5 GB) |

> **Note**: The simulator does not enforce quotas. All `quota` values and `limits` are hardcoded to Azure AI Search **Standard (S1) tier** defaults. The `usage` values for `documentCount`, `indexesCount`, `indexersCount`, `dataSourcesCount`, `storageSize`, `skillsetCount`, `synonymMaps`, and `vectorIndexSize` reflect actual simulator state.

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
