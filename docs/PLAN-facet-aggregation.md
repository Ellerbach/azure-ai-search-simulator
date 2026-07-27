# Plan: Facet Aggregation (`metric: sum`/`min`/`max`/`avg`)

> **Status: implemented** on branch `feature/facet-aggregation` — `sum`, `min`, `max`, `avg`, and
> `default:` missing-value substitution. `cardinality` is a deliberate scope decision to exclude
> (not a gap): the user asked for sum/min/max/avg only, so it was left out. See section 4.

Goal: support Azure AI Search facet aggregations — the `sum`, `min`, `max`, and `avg` metrics, plus
`default:` value substitution — as described in
[Faceted navigation examples — facet aggregation](https://learn.microsoft.com/en-us/azure/search/search-faceted-navigation-examples#facet-aggregation-example).

## 1. The Azure contract (verified against the docs, 2026-05-01-preview)

Request — a facet expression gains a `metric` parameter (whitespace around `:` is tolerated by Azure):

```json
{
  "search": "*",
  "filter": "HotelId eq '41'",
  "facets": [ "Rooms/SleepsCount, metric: sum" ],
  "count": true
}
```

Response — the field's facet array contains a single bucket whose **only** property is the metric name:

```json
{
  "@search.facets": {
    "Rooms/SleepsCount": [
      { "sum": 40.0 }
    ]
  }
}
```

Rules that matter for `sum`/`min`/`max`/`avg`:

- Apply to **numeric types only** (`Edm.Int32`, `Edm.Int64`, `Edm.Double`); the field must be `facetable`.
- Computed over **all documents matching the query + filter** (same document set the existing
  value/interval facets use).
- The bucket has no `value`, `count`, `from`, or `to` — just the metric name (`sum`/`min`/`max`/`avg`).
  The value is serialized as a number (Azure emits `40.0` even for integer fields).
- Multiple metrics on the same field append multiple buckets to the same array; the same field may
  also legitimately appear as a value/interval facet and an aggregation facet in the same request —
  Azure treats each expression independently.
- `default:V` substitutes a value for documents missing the field. For `sum`/`min`/`max` the missing
  document contributes the default like any other value; for `avg` it's included in both the running
  sum and the denominator. Numeric fields take an unquoted number (`default:5`); string fields (not
  applicable to sum/min/max/avg) take a single-quoted string (`default:'text'`) — see the cardinality
  example in Azure's docs.
- Azure additionally supports `cardinality` (+ `precisionThreshold`) — **excluded by explicit user
  decision**, not a gap (see section 4).
- Feature is preview-only in Azure (`2025-03-01-preview` and later). The simulator does not gate
  features by api-version, so no gating is planned.

## 2. Current state of the simulator (what blocks this today)

| Area | File | Current behavior |
| --- | --- | --- |
| Facet spec parsing | `src/AzureAISearchSimulator.Search/SearchService.cs` → `ParseFacetSpec` (~line 1373) | Only understands `count:` and `interval:`. `metric: sum` is silently ignored, so the spec degrades to a plain value facet. |
| Facet dispatch | `SearchService.cs` → `CalculateFacets` (~line 1280) | Routes to `CalculateValueFacet` or `CalculateIntervalFacet` only. Matching doc set is already collected once (`MatchingDocsCollector`) — reusable for sum. |
| Response model | `src/AzureAISearchSimulator.Core/Models/SearchResponse.cs` → `FacetResult` (~line 118) | Has `value`, `count`, `from`, `to`. `Count` is non-nullable `long`, so a sum bucket would wrongly serialize `"count": 0`. No `sum` property. |
| Numeric storage | `src/AzureAISearchSimulator.Search/LuceneDocumentMapper.cs` | Facetable numeric fields store doc values under `{name}_facet` (`NumericDocValuesField` for Int32/Int64, `DoubleDocValuesField` for Double) and a `StoredField` under the original name when retrievable. `CalculateIntervalFacet` already reads numeric values from stored fields per matching doc — the same extraction works for sum. |
| Complex types | `LuceneDocumentMapper.cs` → `CreateLuceneFields` `default:` case | `Edm.ComplexType` / `Collection(Edm.ComplexType)` sub-fields (e.g. `Rooms/SleepsCount`) and numeric collections (`Collection(Edm.Int32)` etc.) are stored as raw JSON only, never indexed. Sum over sub-field paths is therefore **not implementable** without complex-type indexing — top-level numeric fields only. |
| JSON serialization | `src/AzureAISearchSimulator.Api/Program.cs` (~line 74) | `DefaultIgnoreCondition = WhenWritingNull` globally — making `Count` nullable is enough to keep it out of sum buckets. |

## 3. Implementation plan

### 3.1 Model change — `FacetResult` (`SearchResponse.cs`)

- Add `[JsonPropertyName("sum")] public double? Sum { get; set; }`.
- Change `Count` from `long` to `long?` so sum-only buckets omit it (nulls are globally suppressed).
  - Ripple: `CalculateValueFacet` / `CalculateIntervalFacet` assignments still compile;
    tests asserting `facet.Count` need `.Value` or comparison against `(long?)`.

### 3.2 Parsing — `ParseFacetSpec` (`SearchService.cs`)

- Extend the returned tuple (or introduce a small `FacetSpec` record — preferred, the tuple is at 3
  members already) with `string? Metric`.
- Recognize `metric:<name>` case-insensitively, trimming whitespace around both key and value so
  `"field, metric: sum"` (the docs' own spacing) parses.
- Unknown metric names (min/max/avg/cardinality for now): log a warning and skip that facet expression
  (consistent with the existing "not facetable" handling), rather than throwing.

### 3.3 Computation — `CalculateSumFacet`/`CalculateMinMaxFacet`/`CalculateAvgFacet` (`SearchService.cs`)

- In `CalculateFacets`, when `Metric` is `sum`/`min`/`max`/`avg`:
  - Validate the field type is `Edm.Int32`, `Edm.Int64`, or `Edm.Double`; otherwise warn + skip.
  - Resolve an optional numeric `default:` value via `ResolveNumericDefault` (warns and ignores the
    default if it isn't a valid number for the field).
  - Dispatch to the matching calculation method.
- All three share `GetStoredNumericValue(doc, fieldName, fieldType)`, which reads the stored field
  value (same source `CalculateIntervalFacet` already reads) or returns null if the document has no
  value — at which point the default is substituted, if provided.
  - `sum`: accumulate a running total; missing docs without a default contribute nothing.
  - `min`/`max`: track a running `double?`, seeded by the first contributing value; if no document
    contributes a value, the bucket's min/max is null.
  - `avg`: accumulate both a sum and a count of contributing documents; average is `null` if no
    document contributes.
- Return `new List<FacetResult> { new() { Sum/Min/Max/Avg = value } }` — one bucket per metric expression.
- Result assignment: `CalculateFacets` appends to `facets[fieldName]` when the key already exists
  (rather than overwriting), so a field can carry multiple metric buckets plus a value/interval facet
  in the same response.
- `count`/`interval`/`values` combined with `metric` on one expression are ignored (Azure treats a
  metric expression as aggregation-only).
- `cardinality` was intentionally **not implemented** — the user explicitly requested sum/min/max/avg
  only and asked for cardinality to be excluded. Attempting the metric name still parses but is
  rejected as "not supported" at the `CalculateFacets` dispatch, the same path as any unknown metric.

### 3.4 Tests

- `tests/AzureAISearchSimulator.Core.Tests/FacetTests.cs`
  - `FacetResult` with `Sum`/`Min`/`Max`/`Avg` set serializes to a single-property bucket
    (`{"sum": 40.0}`, `{"min": 1.0}`, etc.) with no `count`/`value`/`from`/`to`.
  - Facet spec parsing theory cases for `metric:min`/`max`/`avg` and `default:`.
- `tests/AzureAISearchSimulator.Integration.Tests/FacetIntegrationTests.cs`
  - Sum over `Edm.Int32`/`Int64`/`Double` fields; filter- and text-search-scoped sum; missing-field
    exclusion; non-numeric field skip; same field as value facet + sum facet.
  - Min/max/avg computed together over the same field in one request.
  - Min/avg with `default:` substituting for a missing value (avg's denominator includes the default).
  - Non-numeric field with a numeric metric → facet omitted (warning path).
  - Unsupported metric name (e.g. `metric:median`) → facet omitted (warning path); this also covers
    `metric:cardinality` since it isn't implemented.

### 3.5 Documentation

- `docs/API-REFERENCE.md` — document `metric:sum`/`min`/`max`/`avg` and `default:` in the facet
  parameter section with request/response examples.
- `docs/LIMITATIONS.md` — Facets row lists aggregation metrics; Facet Limitations section lists
  `cardinality` (and `precisionThreshold`) as unsupported, alongside `sort`/`values`/`timeoffset`,
  facet hierarchies, facet filters, and complex-type sub-field paths.
- `docs/TODO.md` — checked item for the four metrics + `default:`; unchecked item for `cardinality`.
- `CHANGELOG.md` — new entry.
- `samples/sample-requests.http` — sum, min/max/avg, and `default:` example requests.

## 4. Explicitly out of scope

- **`cardinality` metric and `precisionThreshold`** — excluded by explicit user decision (not a
  technical gap; an exact-count implementation over the existing `_facet` doc-values fields was
  drafted and working, then removed at the user's request to keep scope to sum/min/max/avg).
- Facet hierarchies (`>` / `;` operators) and facet filters (regex include/exclude) — separate preview
  features from the same Azure doc page, not requested here.
- Aggregation over complex-type sub-fields (`Rooms/SleepsCount`) or numeric collections — blocked on
  complex-type field indexing in `LuceneDocumentMapper`, which is a separate, larger feature.

## 5. Effort estimate

Small–medium: 1 model change (4 nullable properties), ~150 lines in `SearchService.cs` (parsing,
dispatch, 3 calculation methods, default-resolution helper), tests, and docs. No storage/index format
changes required — existing stored fields already carry the data used by `CalculateIntervalFacet`.
