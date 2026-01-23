#!/usr/bin/env pwsh
# Phase 3 Testing Script for Azure AI Search Simulator

$BaseUrl = "http://localhost:5250"
$ApiKey = "admin-key-12345"

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Endpoint,
        [string]$Body = $null
    )
    
    $headers = @{
        "api-key" = $ApiKey
        "Content-Type" = "application/json"
    }
    
    try {
        if ($Body) {
            $result = Invoke-RestMethod -Method $Method -Uri "$BaseUrl$Endpoint" -Headers $headers -Body $Body
        } else {
            $result = Invoke-RestMethod -Method $Method -Uri "$BaseUrl$Endpoint" -Headers $headers
        }
        return $result | ConvertTo-Json -Depth 10
    }
    catch {
        return "Error: $($_.Exception.Message)"
    }
}

Write-Host "=== Testing Phase 3: Data Sources and Indexers ===" -ForegroundColor Cyan

# Test 1: Create an index for the documents
Write-Host "`n1. Creating test index..." -ForegroundColor Yellow
$indexBody = @{
    name = "documents-index"
    fields = @(
        @{ name = "id"; type = "Edm.String"; key = $true; filterable = $true }
        @{ name = "content"; type = "Edm.String"; searchable = $true }
        @{ name = "metadata_storage_path"; type = "Edm.String"; filterable = $true }
        @{ name = "metadata_storage_name"; type = "Edm.String"; filterable = $true }
    )
} | ConvertTo-Json -Depth 3

$result = Invoke-Api -Method "PUT" -Endpoint "/indexes/documents-index" -Body $indexBody
Write-Host $result

# Test 2: Create a data source
Write-Host "`n2. Creating data source..." -ForegroundColor Yellow
$dataSourceBody = @{
    name = "local-files"
    type = "filesystem"
    credentials = @{
        connectionString = "c:\Projets\AzureAISimulator\testdata"
    }
    container = @{
        name = "documents"
    }
} | ConvertTo-Json -Depth 3

$result = Invoke-Api -Method "PUT" -Endpoint "/datasources/local-files" -Body $dataSourceBody
Write-Host $result

# Test 3: List data sources
Write-Host "`n3. Listing data sources..." -ForegroundColor Yellow
$result = Invoke-Api -Method "GET" -Endpoint "/datasources"
Write-Host $result

# Test 4: Create an indexer
Write-Host "`n4. Creating indexer..." -ForegroundColor Yellow
$indexerBody = @{
    name = "file-indexer"
    dataSourceName = "local-files"
    targetIndexName = "documents-index"
    fieldMappings = @(
        @{ sourceFieldName = "metadata_storage_path"; targetFieldName = "id" }
    )
} | ConvertTo-Json -Depth 3

$result = Invoke-Api -Method "PUT" -Endpoint "/indexers/file-indexer" -Body $indexerBody
Write-Host $result

# Test 5: Run the indexer
Write-Host "`n5. Running indexer..." -ForegroundColor Yellow
$result = Invoke-Api -Method "POST" -Endpoint "/indexers/file-indexer/run"
Write-Host $result

# Wait for indexer to complete
Start-Sleep -Seconds 2

# Test 6: Check indexer status
Write-Host "`n6. Checking indexer status..." -ForegroundColor Yellow
$result = Invoke-Api -Method "GET" -Endpoint "/indexers/file-indexer/status"
Write-Host $result

# Test 7: Search the indexed documents
Write-Host "`n7. Searching indexed documents..." -ForegroundColor Yellow
$searchBody = @{
    search = "search"
    count = $true
} | ConvertTo-Json

$result = Invoke-Api -Method "POST" -Endpoint "/indexes/documents-index/docs/search" -Body $searchBody
Write-Host $result

Write-Host "`n=== Phase 3 Testing Complete ===" -ForegroundColor Cyan
