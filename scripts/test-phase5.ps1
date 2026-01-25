# Test script for Phase 5: Skillsets
# Run the API first: dotnet run --project src/AzureAISearchSimulator.Api --urls "https://localhost:7250"

$baseUrl = "https://localhost:7250"
$apiKey = "admin-key-12345"
$headers = @{ "api-key" = $apiKey; "Content-Type" = "application/json" }

# Skip SSL certificate validation for local development
if (-not ([System.Management.Automation.PSTypeName]'TrustAllCertsPolicy').Type) {
    Add-Type @"
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAllCertsPolicy : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint srvPoint, X509Certificate certificate, WebRequest request, int certificateProblem) { return true; }
}
"@
}
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

# Helper function for API calls with certificate bypass
function Invoke-SimulatorApi {
    param(
        [string]$Uri,
        [string]$Method = "Get",
        [string]$Body = $null,
        [switch]$SilentError
    )
    $params = @{
        Uri = $Uri
        Method = $Method
        Headers = $headers
        SkipCertificateCheck = $true
    }
    if ($Body) { $params.Body = $Body }
    if ($SilentError) { $params.ErrorAction = "SilentlyContinue" }
    Invoke-RestMethod @params
}

Write-Host "=== Phase 5: Skillsets Test ===" -ForegroundColor Cyan

# Test 1: Create a skillset
Write-Host "`n1. Creating skillset..." -ForegroundColor Yellow
$skillset = @{
    name = "test-skillset"
    description = "Test skillset for document processing"
    skills = @(
        @{
            "@odata.type" = "#Microsoft.Skills.Text.SplitSkill"
            name = "text-splitter"
            description = "Split content into pages"
            context = "/document"
            inputs = @(
                @{ name = "text"; source = "/document/content" }
            )
            outputs = @(
                @{ name = "textItems"; targetName = "pages" }
            )
            textSplitMode = "pages"
            maximumPageLength = 1000
        },
        @{
            "@odata.type" = "#Microsoft.Skills.Util.ShaperSkill"
            name = "document-shaper"
            context = "/document"
            inputs = @(
                @{ name = "title"; source = "/document/metadata_title" },
                @{ name = "wordCount"; source = "/document/metadata_word_count" }
            )
            outputs = @(
                @{ name = "output"; targetName = "documentInfo" }
            )
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/skillsets?api-version=2024-07-01" -Method Post -Body $skillset
    Write-Host "  Created skillset: $($response.name)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 2: Get the skillset
Write-Host "`n2. Getting skillset..." -ForegroundColor Yellow
try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/skillsets/test-skillset?api-version=2024-07-01"
    Write-Host "  Skillset: $($response.name)" -ForegroundColor Green
    Write-Host "  Skills: $($response.skills.Count)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 3: List all skillsets
Write-Host "`n3. Listing skillsets..." -ForegroundColor Yellow
try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/skillsets?api-version=2024-07-01"
    Write-Host "  Found $($response.value.Count) skillset(s)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Create an index with vector field
Write-Host "`n4. Creating index with vector field..." -ForegroundColor Yellow
$index = @{
    name = "skillset-test-index"
    fields = @(
        @{ name = "id"; type = "Edm.String"; key = $true; filterable = $true }
        @{ name = "content"; type = "Edm.String"; searchable = $true }
        @{ name = "pages"; type = "Collection(Edm.String)"; searchable = $true }
        @{ name = "documentInfo"; type = "Edm.ComplexType" }
        @{ name = "contentVector"; type = "Collection(Edm.Single)"; searchable = $true; 
           vectorSearchProfile = "vector-profile"; dimensions = 1536 }
    )
    vectorSearch = @{
        profiles = @(
            @{ name = "vector-profile"; algorithm = "hnsw-algorithm" }
        )
        algorithms = @(
            @{ name = "hnsw-algorithm"; kind = "hnsw" }
        )
    }
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/indexes?api-version=2024-07-01" -Method Post -Body $index
    Write-Host "  Created index: $($response.name)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 5: Create a skillset with embedding skill (requires Azure OpenAI)
Write-Host "`n5. Creating skillset with embedding skill..." -ForegroundColor Yellow
$embeddingSkillset = @{
    name = "embedding-skillset"
    description = "Skillset with Azure OpenAI embedding"
    skills = @(
        @{
            "@odata.type" = "#Microsoft.Skills.Text.SplitSkill"
            name = "text-splitter"
            context = "/document"
            inputs = @(
                @{ name = "text"; source = "/document/content" }
            )
            outputs = @(
                @{ name = "textItems"; targetName = "pages" }
            )
            textSplitMode = "pages"
            maximumPageLength = 2000
        },
        @{
            "@odata.type" = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill"
            name = "embedding-generator"
            context = "/document"
            resourceUri = "https://your-openai.openai.azure.com"
            deploymentId = "text-embedding-ada-002"
            modelName = "text-embedding-ada-002"
            inputs = @(
                @{ name = "text"; source = "/document/content" }
            )
            outputs = @(
                @{ name = "embedding"; targetName = "contentVector" }
            )
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/skillsets?api-version=2024-07-01" -Method Post -Body $embeddingSkillset
    Write-Host "  Created skillset: $($response.name)" -ForegroundColor Green
    Write-Host "  (Note: Embedding skill requires Azure OpenAI API key configuration)" -ForegroundColor DarkYellow
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 6: Update skillset
Write-Host "`n6. Updating skillset..." -ForegroundColor Yellow
$updatedSkillset = @{
    name = "test-skillset"
    description = "Updated test skillset"
    skills = @(
        @{
            "@odata.type" = "#Microsoft.Skills.Text.SplitSkill"
            name = "text-splitter"
            context = "/document"
            inputs = @(
                @{ name = "text"; source = "/document/content" }
            )
            outputs = @(
                @{ name = "textItems"; targetName = "chunks" }
            )
            textSplitMode = "sentences"
        }
    )
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/skillsets/test-skillset?api-version=2024-07-01" -Method Put -Body $updatedSkillset
    Write-Host "  Updated skillset: $($response.name)" -ForegroundColor Green
    Write-Host "  Description: $($response.description)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 7: Create a data source for testing with skillset
Write-Host "`n7. Creating data source..." -ForegroundColor Yellow
$testDataPath = Join-Path $PSScriptRoot ".." "test-data" "skillset-test"
if (!(Test-Path $testDataPath)) {
    New-Item -ItemType Directory -Path $testDataPath -Force | Out-Null
}

# Create a test file
$testContent = @"
# Test Document for Skillset Processing

This is a test document that will be processed by the skillset.
The document contains multiple paragraphs to test the text splitting skill.

Azure AI Search is a cloud search service with built-in AI capabilities.
It can enrich content during indexing using cognitive skills.
Skills can extract text, detect language, recognize entities, and generate embeddings.

The simulator provides a local alternative for testing and development.
Developers can experiment with skillsets without incurring Azure costs.
"@
$testContent | Out-File -FilePath (Join-Path $testDataPath "test-document.txt") -Encoding utf8

$dataSource = @{
    name = "skillset-test-datasource"
    type = "azureblob"
    credentials = @{
        connectionString = "DefaultEndpointsProtocol=file;LocalPath=$testDataPath"
    }
    container = @{
        name = "documents"
    }
} | ConvertTo-Json -Depth 5

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/datasources?api-version=2024-07-01" -Method Post -Body $dataSource
    Write-Host "  Created data source: $($response.name)" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "  Data source already exists (OK)" -ForegroundColor Yellow
    } else {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Test 8: Create an indexer with skillset
Write-Host "`n8. Creating indexer with skillset..." -ForegroundColor Yellow
$indexer = @{
    name = "skillset-test-indexer"
    dataSourceName = "skillset-test-datasource"
    targetIndexName = "skillset-test-index"
    skillsetName = "test-skillset"
    fieldMappings = @(
        @{ sourceFieldName = "metadata_storage_path"; targetFieldName = "id" }
    )
    outputFieldMappings = @(
        @{ sourceFieldName = "/document/chunks"; targetFieldName = "pages" }
    )
} | ConvertTo-Json -Depth 5

try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/indexers?api-version=2024-07-01" -Method Post -Body $indexer
    Write-Host "  Created indexer: $($response.name)" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "  Indexer already exists (OK)" -ForegroundColor Yellow
    } else {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Test 9: Run the indexer
Write-Host "`n9. Running indexer..." -ForegroundColor Yellow
try {
    Invoke-SimulatorApi -Uri "$baseUrl/indexers/skillset-test-indexer/run?api-version=2024-07-01" -Method Post
    Write-Host "  Indexer run triggered" -ForegroundColor Green
    Start-Sleep -Seconds 2
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 10: Check indexer status
Write-Host "`n10. Checking indexer status..." -ForegroundColor Yellow
try {
    $response = Invoke-SimulatorApi -Uri "$baseUrl/indexers/skillset-test-indexer/status?api-version=2024-07-01"
    Write-Host "  Status: $($response.status)" -ForegroundColor Green
    if ($response.lastResult) {
        Write-Host "  Last Result: $($response.lastResult.status)" -ForegroundColor Green
        Write-Host "  Items Processed: $($response.lastResult.itemsProcessed)" -ForegroundColor Green
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 11: Search the indexed documents
Write-Host "`n11. Searching indexed documents..." -ForegroundColor Yellow
try {
    $searchBody = @{ search = "*"; select = "id,content,pages" } | ConvertTo-Json
    $response = Invoke-SimulatorApi -Uri "$baseUrl/indexes/skillset-test-index/docs/search?api-version=2024-07-01" -Method Post -Body $searchBody
    Write-Host "  Found $($response.value.Count) document(s)" -ForegroundColor Green
    foreach ($doc in $response.value) {
        Write-Host "    - ID: $($doc.id)" -ForegroundColor Cyan
        if ($doc.pages) {
            Write-Host "      Pages/Chunks: $($doc.pages.Count)" -ForegroundColor Cyan
        }
    }
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Cleanup
Write-Host "`n12. Cleaning up..." -ForegroundColor Yellow
try {
    Invoke-SimulatorApi -Uri "$baseUrl/skillsets/test-skillset?api-version=2024-07-01" -Method Delete -SilentError
    Invoke-SimulatorApi -Uri "$baseUrl/skillsets/embedding-skillset?api-version=2024-07-01" -Method Delete -SilentError
    Invoke-SimulatorApi -Uri "$baseUrl/indexers/skillset-test-indexer?api-version=2024-07-01" -Method Delete -SilentError
    Invoke-SimulatorApi -Uri "$baseUrl/indexes/skillset-test-index?api-version=2024-07-01" -Method Delete -SilentError
    Invoke-SimulatorApi -Uri "$baseUrl/datasources/skillset-test-datasource?api-version=2024-07-01" -Method Delete -SilentError
    Write-Host "  Cleanup completed" -ForegroundColor Green
} catch {
    Write-Host "  Cleanup error (may be OK): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "`n=== Phase 5 Test Complete ===" -ForegroundColor Cyan
