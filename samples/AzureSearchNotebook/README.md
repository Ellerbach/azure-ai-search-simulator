# Azure AI Search Notebook Sample

This Jupyter notebook demonstrates how to use the **Azure AI Search Python SDK** with the local **Azure AI Search Simulator**.

## Prerequisites

### 1. Start the Azure AI Search Simulator

```bash
cd src/AzureAISearchSimulator.Api
dotnet run --urls "https://localhost:7250"
```

> **Note**: HTTPS is required for Azure SDK compatibility. The notebook disables SSL verification for local development.

### 2. Start the Custom Skills API (Optional)

For skillset functionality, start the CustomSkillSample project:

```bash
cd samples/CustomSkillSample
dotnet run
```

### 3. Install Python Dependencies

```bash
pip install -r requirements.txt
```

Or install manually:

```bash
pip install azure-search-documents requests pandas jupyter
```

## Running the Notebook

1. Open the notebook in VS Code or Jupyter:

   ```bash
   jupyter notebook azure_search_demo.ipynb
   ```

2. Run cells in order from top to bottom

3. The notebook will:
   - Download sample PDF files from Azure samples repository
   - Create a search index with enrichment fields
   - Configure a data source for local file system
   - Set up a skillset with custom Web API skills
   - Create an indexer to process documents
   - Upload sample documents (push model)
   - Execute various search queries

## What's Covered

| Section | Description |
| ------- | ----------- |
| **Setup** | Import libraries, configure connection |
| **Index Creation** | Define schema with searchable, filterable, facetable fields |
| **Data Source** | Configure local file system as data source |
| **Skillset** | Custom WebApiSkill for text stats, keywords, sentiment, summary |
| **Indexer** | Automated document processing with field mappings |
| **Push Model** | Direct document upload alternative |
| **Search Queries** | Simple, filtered, faceted, sorted, highlighted searches |

## Custom Skills Used

The notebook integrates with the [CustomSkillSample](../CustomSkillSample/) project:

| Skill | Endpoint | Output |
| ----- | -------- | ------ |
| Text Stats | `/api/skills/text-stats` | wordCount, sentenceCount |
| Keywords | `/api/skills/extract-keywords` | keywords array |
| Sentiment | `/api/skills/analyze-sentiment` | sentiment, score |
| Summarize | `/api/skills/summarize` | summary text |

## Troubleshooting

### Connection Refused

Make sure the simulator is running:

```bash
curl -k https://localhost:7250/indexes?api-version=2024-07-01 -H "api-key: admin-key-12345"
```

> The `-k` flag skips SSL certificate verification for the self-signed dev certificate.

### Custom Skills Not Working

Ensure CustomSkillSample is running:

```bash
curl http://localhost:5260/api/skills/health
```

### SSL Certificate Errors

The notebook disables SSL verification for local development. This is expected behavior.

## Related Resources

- [Azure AI Search Simulator](../../README.md)
- [API Reference](../../docs/API-REFERENCE.md)
- [Limitations](../../docs/LIMITATIONS.md)
- [Azure SDK Sample (C#)](../AzureSdkSample/)
