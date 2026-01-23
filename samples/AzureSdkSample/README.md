# Azure SDK Sample

This sample demonstrates that the Azure AI Search Simulator is compatible with the official **Azure.Search.Documents** SDK.

## Prerequisites

1. .NET 10.0 SDK installed
2. Azure AI Search Simulator running with HTTPS on `https://localhost:7250`
3. ASP.NET Core developer certificate trusted (see below)

## Trust the Developer Certificate

The Azure SDK requires HTTPS. To run this sample, first trust the ASP.NET Core developer certificate:

```bash
dotnet dev-certs https --trust
```

## Running the Simulator

Start the simulator with HTTPS enabled in a separate terminal:

```bash
# From the repository root
cd src/AzureAISearchSimulator.Api
dotnet run --launch-profile https
```

Or with Docker (ensure HTTPS is configured):

```bash
docker-compose up
```

## Running the Sample

In a separate terminal:

```bash
cd samples/AzureSdkSample
dotnet run
```

## What This Sample Tests

The sample performs the following operations using the official Azure SDK:

1. **Create Index** - Creates a hotel index with various field types
2. **Upload Documents** - Uploads 5 hotel documents
3. **Simple Search** - Full-text search for "luxury hotel"
4. **Filtered Search** - Search with OData filter (rating >= 4.5)
5. **Faceted Search** - Get category and tag facets
6. **Get Document** - Retrieve a document by key
7. **Document Count** - Get total document count
8. **Merge Document** - Update a document using merge
9. **Delete Document** - Delete a document by key
10. **Delete Index** - Clean up by deleting the index

## Expected Output

```text
=== Azure AI Search Simulator - Azure SDK Compatibility Test ===

1. Creating index...
   Index 'hotels-sdk-test' created successfully.

2. Uploading documents...
   Uploaded 5 documents.

3. Simple search for 'luxury hotel'...
   Found 5 results:
   - Grand Hotel Seattle (Score: X.XX)
   ...

4. Search with filter (rating >= 4.5)...
   High-rated hotels:
   - Mountain Resort Aspen (Rating: 4.9)
   ...

5. Faceted search...
   Categories:
   - Resort: 2
   - Luxury: 1
   ...

=== All Azure SDK operations completed successfully! ===
The simulator is compatible with Azure.Search.Documents SDK.
```

## SDK Version

This sample uses `Azure.Search.Documents` version 11.6.0, which is the latest stable version as of this writing.

## Differences from Real Azure AI Search

While the simulator is compatible with the Azure SDK for most operations, there are some differences:

- **Vector Search**: Uses brute-force cosine similarity instead of HNSW
- **Semantic Search**: Not supported (requires Azure AI models)
- **Some Analyzers**: Only Lucene analyzers are available
- **Geo-spatial**: Limited support for geography types

See [LIMITATIONS.md](../../docs/LIMITATIONS.md) for a full list of differences.
