# Custom Skills Sample

This sample demonstrates how to create and use custom skills with Azure AI Search (and the simulator).

## What are Custom Skills?

Custom skills allow you to extend Azure AI Search's built-in AI enrichment capabilities by calling your own web API endpoints during the indexing process. They use the `WebApiSkill` type and follow a specific request/response format.

## Skills Included

| Skill | Endpoint | Description |
| ----- | -------- | ----------- |
| **Text Stats** | `POST /api/skills/text-stats` | Counts characters, words, and sentences |
| **Extract Keywords** | `POST /api/skills/extract-keywords` | Extracts keywords using word frequency |
| **Analyze Sentiment** | `POST /api/skills/analyze-sentiment` | Simple keyword-based sentiment analysis |
| **Detect PII** | `POST /api/skills/detect-pii` | Detects and masks emails, phones, SSNs, credit cards |
| **Summarize** | `POST /api/skills/summarize` | Creates extractive summary (first N sentences) |

## Request/Response Format

### Request (sent by Azure AI Search)

```json
{
  "values": [
    {
      "recordId": "unique-id-1",
      "data": {
        "text": "The text to process",
        "optionalParam": "optional value"
      }
    },
    {
      "recordId": "unique-id-2",
      "data": {
        "text": "Another document"
      }
    }
  ]
}
```

### Response (returned by your skill)

```json
{
  "values": [
    {
      "recordId": "unique-id-1",
      "data": {
        "output1": "result",
        "output2": 42
      },
      "errors": [],
      "warnings": []
    },
    {
      "recordId": "unique-id-2",
      "data": {
        "output1": "another result",
        "output2": 100
      },
      "errors": [],
      "warnings": []
    }
  ]
}
```

## Running the Sample

### 1. Start the Custom Skills API

```bash
cd samples/CustomSkillSample
dotnet run
```

The API runs on `http://localhost:5260` with Swagger UI at the root.

### 2. Test Skills Directly

Use the [test-skills.http](test-skills.http) file to test individual skills.

### 3. Integration with Simulator

1. Start the Azure AI Search Simulator:

   ```bash
   cd src/AzureAISearchSimulator.Api
   dotnet run --urls "https://localhost:7250"
   ```

2. Use [integration-example.http](integration-example.http) to:
   - Create an index with enrichment fields
   - Create a skillset with custom WebApiSkills
   - Create a data source and indexer
   - Run the indexer to enrich documents
   - Query the enriched results

## Using in a Skillset Definition

```json
{
  "name": "my-skillset",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
      "name": "my-custom-skill",
      "uri": "http://localhost:5260/api/skills/analyze-sentiment",
      "httpMethod": "POST",
      "timeout": "PT30S",
      "batchSize": 10,
      "context": "/document",
      "inputs": [
        {
          "name": "text",
          "source": "/document/content"
        }
      ],
      "outputs": [
        { "name": "sentiment", "targetName": "sentiment" },
        { "name": "score", "targetName": "sentimentScore" }
      ]
    }
  ]
}
```

## Key Concepts

### Context

The `context` property defines where in the enrichment tree the skill operates:

- `/document` - Runs once per document
- `/document/pages/*` - Runs once per page (for split documents)
- `/document/sentences/*` - Runs once per sentence

### Inputs

Map data from the enrichment tree to your skill's input parameters:

- `/document/content` - The document's text content
- `/document/normalized_images/*` - Extracted images
- `/document/fieldName` - A specific field
- `="literal"` - A literal value

### Outputs

Map your skill's output to the enrichment tree:

- `name` - The output field name from your API response
- `targetName` - Where to store it in the enrichment tree

### Output Field Mappings

In the indexer, map enriched fields to index fields:

```json
{
  "outputFieldMappings": [
    { 
      "sourceFieldName": "/document/sentiment", 
      "targetFieldName": "sentiment" 
    }
  ]
}
```

## Error Handling

Return errors for specific records that failed:

```json
{
  "values": [
    {
      "recordId": "1",
      "data": null,
      "errors": [
        {
          "message": "Text is too short to analyze",
          "statusCode": 400
        }
      ],
      "warnings": []
    }
  ]
}
```

## Best Practices

1. **Batch Processing**: Handle multiple records per request (configurable via `batchSize`)
2. **Timeouts**: Set appropriate timeouts (default is 30 seconds)
3. **Idempotency**: Skills may be called multiple times for the same record
4. **Error Handling**: Return clear error messages for debugging
5. **Performance**: Keep processing fast to avoid indexing delays

## Production Considerations

For production deployments:

1. **Host on Azure Functions** or **Azure App Service**
2. **Add authentication** via API keys or Azure AD
3. **Configure CORS** if needed
4. **Add logging** for troubleshooting
5. **Scale** based on indexing throughput needs

Example with authentication header:

```json
{
  "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
  "uri": "https://my-skills.azurewebsites.net/api/analyze",
  "httpHeaders": {
    "x-functions-key": "your-function-key",
    "x-custom-header": "value"
  }
}
```
