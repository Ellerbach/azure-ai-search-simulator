# PDF Extraction Test Notebook

This notebook lets you visually inspect what text and metadata the PDF extraction produces for the sample PDF documents. It helps evaluate the quality of PDF cracking before documents go through the Azure AI Search Simulator indexer pipeline.

## Prerequisites

1. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```

2. Sample PDF files should already be present in `../data/pdfs/` (downloaded from [Azure-Samples/azure-search-sample-data](https://github.com/Azure-Samples/azure-search-sample-data))

## What You'll See

- Extracted text content per page
- PDF metadata (title, author, creation date, producer, etc.)
- Word/character counts
- Side-by-side comparison of extraction libraries (PyMuPDF vs pdfplumber)
- Optionally, results from the simulator's own `PdfCracker` endpoint
