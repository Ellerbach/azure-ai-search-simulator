# Indexer Test Notebook

A simple Jupyter notebook sample demonstrating how to use Azure AI Search indexers with the Azure AI Search Simulator.

## Overview

This sample shows how to:
1. Create a search index for simple documents
2. Set up a local file system data source
3. Configure an indexer to automatically process documents
4. Verify that all documents are indexed correctly

## Sample Data

The notebook uses simple sample data:
- **JSON metadata files**: Contains document metadata (id, title, author, category, created date)
- **TXT content files**: Contains the actual document text content

## Prerequisites

1. **Start the Azure AI Search Simulator**:
   ```bash
   cd src/AzureAISearchSimulator.Api
   dotnet run --urls "https://localhost:7250"
   ```

2. **Install Python dependencies**:
   ```bash
   pip install -r requirements.txt
   ```

3. **Run the notebook**:
   Open `indexer_test.ipynb` in VS Code or Jupyter and run all cells.

## What Gets Tested

- Data source creation and configuration
- Index schema with various field types
- Indexer execution with JSON metadata parsing
- Document count verification
- Search functionality to validate indexed content
