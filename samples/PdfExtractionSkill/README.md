# PDF Extraction Custom Skill (Python + PDFBox)

A Python custom skill for Azure AI Search that:

1. Receives a **base64-encoded PDF** via the `file_data` structure (output of the [FileDataSkillSample](../FileDataSkillSample/))
2. Extracts **text and metadata** using Apache PDFBox (via [JPype](https://jpype.readthedocs.io/))
3. **Chunks** the text using the same algorithm as the simulator's `TextSplitSkill`
4. Returns fields ready for **Azure AI Search index injection**

## Prerequisites

| Requirement | Details |
|---|---|
| **Python 3.10+** | With pip |
| **JDK 11+** (17+ recommended) | JPype needs a JVM. Install [Eclipse Temurin](https://adoptium.net/) or `winget install Microsoft.OpenJDK.21` |
| **PDFBox JAR** | Downloaded automatically on first run |

## Quick Start

```bash
cd samples/PdfExtractionSkill
pip install -r requirements.txt
python app.py
```

The API starts on `http://localhost:5280`.

## Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/skills/pdf-extraction` | Extract text, metadata, and chunks from base64 PDF |
| `GET` | `/api/skills/health` | Health check |

## Configuration (Environment Variables)

| Variable | Default | Description |
|---|---|---|
| `PDFBOX_VERSION` | `3.0.4` | PDFBox JAR version |
| `CHUNK_ENABLED` | `true` | Enable text chunking |
| `CHUNK_MAX_LENGTH` | `2000` | Max characters per chunk |
| `CHUNK_OVERLAP` | `0` | Overlap characters between chunks |
| `PORT` | `5280` | Server port |

## Skill Contract

### Inputs

| Name | Type | Description |
|---|---|---|
| `file_data` | `{"$type": "file", "data": "<base64>"}` | Base64-encoded file content (from FileDataSkill or `/document/file_data`) |
| `documentId` | string (optional) | Document identifier for logging |

### Outputs

| Name | Type | Description |
|---|---|---|
| `content` | string | Full extracted text |
| `page_count` | int | Number of pages |
| `word_count` | int | Total word count |
| `character_count` | int | Total character count |
| `metadata_title` | string | PDF title |
| `metadata_author` | string | PDF author |
| `metadata_created_date` | string | Creation date (ISO-8601) |
| `metadata_modified_date` | string | Modification date (ISO-8601) |
| `metadata` | object | Additional metadata (subject, keywords, creator, producer, pdfVersion) |
| `pages` | array | Per-page text with char/word counts |
| `chunks` | array | Text chunks (when chunking is enabled) |
| `chunk_count` | int | Number of chunks |
| `extraction_time_ms` | float | PDFBox extraction time |

## Pipeline Setup — Chaining FileDataSkill → PdfExtractionSkill

### Skillset Definition

```json
{
  "name": "pdf-extraction-pipeline",
  "description": "Read file → extract PDF text with PDFBox → chunk",
  "skills": [
    {
      "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
      "name": "file-data-skill",
      "description": "Read file from disk and return base64",
      "uri": "http://localhost:5270/api/skills/file-data",
      "httpMethod": "POST",
      "timeout": "PT60S",
      "batchSize": 1,
      "context": "/document",
      "inputs": [
        { "name": "documentId", "source": "/document/metadata_storage_name" },
        { "name": "contentPath", "source": "/document/metadata_storage_path" }
      ],
      "outputs": [
        { "name": "file_data", "targetName": "file_data" }
      ]
    },
    {
      "@odata.type": "#Microsoft.Skills.Custom.WebApiSkill",
      "name": "pdf-extraction-skill",
      "description": "Extract text and metadata from PDF using PDFBox",
      "uri": "http://localhost:5280/api/skills/pdf-extraction",
      "httpMethod": "POST",
      "timeout": "PT120S",
      "batchSize": 1,
      "context": "/document",
      "inputs": [
        { "name": "file_data", "source": "/document/file_data" },
        { "name": "documentId", "source": "/document/metadata_storage_name" }
      ],
      "outputs": [
        { "name": "content", "targetName": "pdfbox_content" },
        { "name": "page_count", "targetName": "pdfbox_page_count" },
        { "name": "word_count", "targetName": "pdfbox_word_count" },
        { "name": "character_count", "targetName": "pdfbox_character_count" },
        { "name": "metadata_title", "targetName": "pdfbox_title" },
        { "name": "metadata_author", "targetName": "pdfbox_author" },
        { "name": "chunks", "targetName": "pdfbox_chunks" },
        { "name": "chunk_count", "targetName": "pdfbox_chunk_count" }
      ]
    }
  ]
}
```

### Index Fields  

```json
{
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "pdfbox_content", "type": "Edm.String", "searchable": true },
    { "name": "pdfbox_page_count", "type": "Edm.Int32", "filterable": true },
    { "name": "pdfbox_word_count", "type": "Edm.Int32", "filterable": true },
    { "name": "pdfbox_character_count", "type": "Edm.Int32", "filterable": true },
    { "name": "pdfbox_title", "type": "Edm.String", "searchable": true },
    { "name": "pdfbox_author", "type": "Edm.String", "filterable": true },
    { "name": "pdfbox_chunks", "type": "Collection(Edm.String)", "searchable": true },
    { "name": "pdfbox_chunk_count", "type": "Edm.Int32", "filterable": true }
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
        "file_data": {
          "$type": "file",
          "data": "JVBERi0xLjcK..."
        }
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
        "content": "Hello World!\n",
        "page_count": 1,
        "word_count": 2,
        "character_count": 13,
        "metadata_title": null,
        "metadata_author": null,
        "metadata_created_date": null,
        "metadata_modified_date": null,
        "metadata": {},
        "pages": [
          { "page_num": 1, "text": "Hello World!\n", "char_count": 13, "word_count": 2 }
        ],
        "chunks": ["Hello World!"],
        "chunk_count": 1,
        "extraction_time_ms": 42.3
      },
      "errors": [],
      "warnings": []
    }
  ]
}
```

## Production Deployment

Use gunicorn with a **single worker** (the JVM is shared per-process):

```bash
gunicorn -w 1 -b 0.0.0.0:5280 app:app
```

For multiple workers, each will start its own JVM — ensure enough memory.
