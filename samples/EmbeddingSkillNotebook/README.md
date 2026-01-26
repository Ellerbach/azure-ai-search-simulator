# Embedding Skill Demo Notebook

A Jupyter notebook sample demonstrating how to use the **Azure OpenAI Embedding Skill** with the Azure AI Search Simulator for vector search.

## Overview

This sample shows how to:

1. Create a search index with vector fields for embeddings
2. Configure HNSW algorithm for efficient vector search
3. Set up a skillset with Azure OpenAI Embedding skill
4. Run an indexer to generate embeddings automatically
5. Perform vector search (semantic search)
6. Perform hybrid search (keyword + vector with RRF fusion)

## Sample Data

The notebook uses the same sample data as the IndexerTestNotebook:

- **JSON metadata files**: Document metadata (id, title, author, category)
- **TXT content files**: Document text content (used for embedding generation)

The data is located at `../IndexerTestNotebook/data/`

## Prerequisites

### 1. Start the Azure AI Search Simulator

```bash
cd src/AzureAISearchSimulator.Api
dotnet run --urls "https://localhost:7250"
```

> ⚠️ **Important**: The Azure SDK requires HTTPS. Make sure to use the `--urls` parameter.

### 2. Configure Azure OpenAI

Update `src/AzureAISearchSimulator.Api/appsettings.json` with your Azure OpenAI credentials:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "ApiKey": "your-api-key",
    "EmbeddingDeployment": "text-embedding-ada-002"
  }
}
```

### 3. Install Python Dependencies

```bash
pip install -r requirements.txt
```

Or install manually:

```bash
pip install azure-search-documents requests pandas numpy jupyter ipykernel
```

## Running the Notebook

1. Start Jupyter:

   ```bash
   jupyter notebook
   ```

2. Open `embedding_skill_demo.ipynb`

3. Update the Azure OpenAI configuration in cell 2:

   ```python
   AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com"
   AZURE_OPENAI_DEPLOYMENT = "text-embedding-ada-002"
   AZURE_OPENAI_API_KEY = "YOUR-AZURE-OPENAI-API-KEY-HERE"
   ```

4. Run all cells sequentially

## What You'll Learn

### Vector Search Concepts

- **Embeddings**: Dense vector representations of text (1536 dimensions for ada-002)
- **HNSW Algorithm**: Hierarchical Navigable Small World for fast ANN search
- **Cosine Similarity**: Distance metric for comparing vectors

### Azure AI Search Features

- **Vector Fields**: `Collection(Edm.Single)` with dimensions
- **Vector Search Profiles**: Configure algorithm and parameters
- **Output Field Mappings**: Map skill outputs to index fields
- **Hybrid Search**: Combine BM25 keyword search with vector search
- **RRF Fusion**: Reciprocal Rank Fusion for result combination

## Troubleshooting

### "No documents with vectors found"

- Check that Azure OpenAI credentials are configured correctly
- Verify the embedding deployment exists and is accessible
- Check the simulator logs for embedding API errors

### "SSL certificate verify failed"

- The notebook disables SSL verification for local development
- Make sure the simulator is running on HTTPS (port 7250)

### "Index not found"

- Run the cells in order (create index before running indexer)
- Check the simulator is running

## Related Samples

- [AzureSearchNotebook](../AzureSearchNotebook/) - Basic Azure SDK demo
- [IndexerTestNotebook](../IndexerTestNotebook/) - Indexer functionality demo
- [AzureSdkSample](../AzureSdkSample/) - C# SDK sample
