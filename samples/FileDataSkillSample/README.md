# File Data Custom Skill Sample

A custom skill for Azure AI Search that reads a file from disk and returns its content as a base64-encoded `file_data` structure, compatible with the **Document Extraction** skill.

## Purpose

This skill acts as a bridge: given a `documentId` and a `contentPath`, it reads the raw file bytes and returns them in the exact format Azure AI Search expects for document cracking:

```json
{
  "file_data": {
    "$type": "file",
    "data": "<base64-encoded-content>"
  }
}
```

This is useful when you want to feed files through a custom skill pipeline before or instead of the built-in blob indexer, or when your files reside on a local/network path rather than in Azure Blob Storage.

## Running

```bash
cd samples/FileDataSkillSample
dotnet run
```

The API starts on `http://localhost:5270` with Swagger UI at the root.

## Configuration

| Setting | Description | Default |
|---|---|---|
| `FileData:BasePath` | Base directory for resolving relative content paths | `""` (empty — paths must be absolute) |

Set via `appsettings.json`, environment variable, or command line:

```bash
dotnet run -- --FileData:BasePath="C:\my-documents"
```

## Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/skills/file-data` | Read file and return base64-encoded `file_data` |
| `GET` | `/api/skills/health` | Health check |

## Skill Contract

### Inputs

| Name | Type | Description |
|---|---|---|
| `documentId` | string | Document identifier (for logging/tracking) |
| `contentPath` | string | Path to the file on disk (absolute, or relative to `BasePath`) |

### Outputs

| Name | Type | Description |
|---|---|---|
| `file_data` | object | `{ "$type": "file", "data": "<base64>" }` |

## Skillset Definition Example

```json
{
  "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
  "name": "file-data-skill",
  "description": "Reads file from disk and returns base64 file_data",
  "uri": "http://localhost:5270/api/skills/file-data",
  "httpMethod": "POST",
  "timeout": "PT60S",
  "batchSize": 1,
  "context": "/document",
  "inputs": [
    {
      "name": "documentId",
      "source": "/document/metadata_storage_name"
    },
    {
      "name": "contentPath",
      "source": "/document/metadata_storage_path"
    }
  ],
  "outputs": [
    {
      "name": "file_data",
      "targetName": "file_data"
    }
  ]
}
```

## Sample Request

```json
{
  "values": [
    {
      "recordId": "1",
      "data": {
        "documentId": "employee_handbook",
        "contentPath": "C:/Projets/AzureAISimulator/samples/data/pdfs/employee_handbook.pdf"
      }
    }
  ]
}
```

## Sample Response

```json
{
  "values": [
    {
      "recordId": "1",
      "data": {
        "file_data": {
          "$type": "file",
          "data": "JVBERi0xLjcKJ..."
        }
      },
      "errors": [],
      "warnings": []
    }
  ]
}
```
