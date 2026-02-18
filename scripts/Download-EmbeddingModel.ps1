<#
.SYNOPSIS
    Downloads a BERT sentence-transformer model in ONNX format from HuggingFace.

.DESCRIPTION
    Downloads the model.onnx and vocab.txt files for a supported embedding model
    from HuggingFace and places them in the local models directory.

.PARAMETER ModelName
    The model to download. Supported values:
      - all-MiniLM-L6-v2  (384 dimensions, ~80 MB) [default]
      - bge-small-en-v1.5 (384 dimensions, ~130 MB)
      - all-mpnet-base-v2 (768 dimensions, ~420 MB)

.PARAMETER OutputDir
    The directory where model files will be saved.
    Defaults to ./data/models relative to the repository root.

.EXAMPLE
    .\Download-EmbeddingModel.ps1
    Downloads the default all-MiniLM-L6-v2 model.

.EXAMPLE
    .\Download-EmbeddingModel.ps1 -ModelName bge-small-en-v1.5
    Downloads the BGE small model.

.EXAMPLE
    .\Download-EmbeddingModel.ps1 -ModelName all-MiniLM-L6-v2 -OutputDir C:\models
    Downloads to a custom directory.
#>
param(
    [ValidateSet("all-MiniLM-L6-v2", "bge-small-en-v1.5", "all-mpnet-base-v2")]
    [string]$ModelName = "all-MiniLM-L6-v2",

    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

# Model registry: model name -> HuggingFace repo
$models = @{
    "all-MiniLM-L6-v2"  = @{
        Repo = "sentence-transformers/all-MiniLM-L6-v2"
        OnnxPath = "onnx/model.onnx"
        VocabPath = "vocab.txt"
        Dimensions = 384
        SizeMB = "~80"
    }
    "bge-small-en-v1.5"  = @{
        Repo = "BAAI/bge-small-en-v1.5"
        OnnxPath = "onnx/model.onnx"
        VocabPath = "vocab.txt"
        Dimensions = 384
        SizeMB = "~130"
    }
    "all-mpnet-base-v2"  = @{
        Repo = "sentence-transformers/all-mpnet-base-v2"
        OnnxPath = "onnx/model.onnx"
        VocabPath = "vocab.txt"
        Dimensions = 768
        SizeMB = "~420"
    }
}

# Resolve output directory
if ([string]::IsNullOrEmpty($OutputDir)) {
    # Default: repo root / src / AzureAISearchSimulator.Api / data / models
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $repoRoot = Split-Path -Parent $scriptDir
    $OutputDir = Join-Path $repoRoot "src" "AzureAISearchSimulator.Api" "data" "models"
}

$modelInfo = $models[$ModelName]
if (-not $modelInfo) {
    Write-Error "Unknown model: $ModelName. Supported models: $($models.Keys -join ', ')"
    exit 1
}

$modelDir = Join-Path $OutputDir $ModelName
$onnxFile = Join-Path $modelDir "model.onnx"
$vocabFile = Join-Path $modelDir "vocab.txt"

# Check if already downloaded
if ((Test-Path $onnxFile) -and (Test-Path $vocabFile)) {
    Write-Host "Model '$ModelName' already exists at $modelDir" -ForegroundColor Green
    Write-Host "  model.onnx: $([math]::Round((Get-Item $onnxFile).Length / 1MB, 1)) MB"
    Write-Host "  vocab.txt:  $([math]::Round((Get-Item $vocabFile).Length / 1KB, 1)) KB"
    Write-Host "To re-download, delete the directory and run again."
    exit 0
}

# Create directory
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

$repo = $modelInfo.Repo
$baseUrl = "https://huggingface.co/$repo/resolve/main"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Downloading: $ModelName" -ForegroundColor Cyan
Write-Host "  Repository:  $repo" -ForegroundColor Cyan
Write-Host "  Dimensions:  $($modelInfo.Dimensions)" -ForegroundColor Cyan
Write-Host "  Size:        $($modelInfo.SizeMB) MB" -ForegroundColor Cyan
Write-Host "  Target:      $modelDir" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Download ONNX model
$onnxUrl = "$baseUrl/$($modelInfo.OnnxPath)"
Write-Host "Downloading model.onnx from $onnxUrl ..." -ForegroundColor Yellow
try {
    $ProgressPreference = 'SilentlyContinue'  # Speed up Invoke-WebRequest
    Invoke-WebRequest -Uri $onnxUrl -OutFile $onnxFile -UseBasicParsing
    $size = [math]::Round((Get-Item $onnxFile).Length / 1MB, 1)
    Write-Host "  Downloaded model.onnx ($size MB)" -ForegroundColor Green
}
catch {
    Write-Error "Failed to download model.onnx: $_"
    exit 1
}

# Download vocabulary
$vocabUrl = "$baseUrl/$($modelInfo.VocabPath)"
Write-Host "Downloading vocab.txt from $vocabUrl ..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $vocabUrl -OutFile $vocabFile -UseBasicParsing
    $size = [math]::Round((Get-Item $vocabFile).Length / 1KB, 1)
    Write-Host "  Downloaded vocab.txt ($size KB)" -ForegroundColor Green
}
catch {
    Write-Error "Failed to download vocab.txt: $_"
    exit 1
}

Write-Host ""
Write-Host "Done! Model '$ModelName' saved to:" -ForegroundColor Green
Write-Host "  $modelDir" -ForegroundColor Green
Write-Host ""
Write-Host "Usage in skillset JSON:" -ForegroundColor Cyan
Write-Host "  ""resourceUri"": ""local://$ModelName""" -ForegroundColor White
