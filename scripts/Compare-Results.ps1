<#
.SYNOPSIS
    Compare Azure AI Search Simulator responses with a real Azure AI Search service.

.DESCRIPTION
    Sends identical HTTP requests to both the simulator and a real Azure AI Search
    service, then compares the JSON responses side by side, highlighting differences.

    Environment variables are loaded from the .env file in the repository root.

.PARAMETER Scenario
    Which comparison scenario to run. Defaults to "All".
    Values: All, ListIndexes, CreateIndex, UploadDocs, SimpleSearch, FilterSearch,
            CountSearch, PagingSearch, GetDocument, DocCount, ServiceStats, Cleanup

.PARAMETER IndexName
    The index name to use for testing. Defaults to "compare-test".

.PARAMETER IgnoreFields
    Comma-separated list of JSON property names to ignore when comparing
    (e.g. dynamic values like @odata.etag, elapsed times). Defaults to a sensible set.

.PARAMETER ShowFullResponse
    If set, prints the full JSON responses even when they match.

.EXAMPLE
    .\Compare-Results.ps1
    .\Compare-Results.ps1 -Scenario SimpleSearch
    .\Compare-Results.ps1 -Scenario All -ShowFullResponse
    .\Compare-Results.ps1 -Scenario Cleanup
#>

[CmdletBinding()]
param(
    [ValidateSet("All", "ListIndexes", "CreateIndex", "UploadDocs",
                 "SimpleSearch", "FilterSearch", "CountSearch", "PagingSearch",
                 "GetDocument", "DocCount", "ServiceStats", "Cleanup")]
    [string]$Scenario = "All",

    [string]$IndexName = "compare-test",

    [string[]]$IgnoreFields = @("@odata.etag", "elapsed", "requestId",
                                 "@odata.context", "statusCode"),

    [switch]$ShowFullResponse,

    [string]$OutputFile = (Join-Path $PSScriptRoot ("compare-results-" + (Get-Date -Format "yyyy-MM-dd_HHmmss") + ".json")),

    [switch]$NoSave
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Accumulator for JSON report
$script:comparisonResults = @()

# -- Load .env ---------------------------------------------------------
$envFile = Join-Path $PSScriptRoot "..\.env"
if (-not (Test-Path $envFile)) {
    Write-Error "No .env file found at $envFile. Copy .env.example to .env and fill in values."
    return
}

Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith("#")) {
        $parts = $line -split "=", 2
        if ($parts.Length -eq 2) {
            [Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), "Process")
        }
    }
}

# -- Read config from env ----------------------------------------------
$simBase     = $env:BASE_URL;         if (-not $simBase)     { Write-Error "BASE_URL not set in .env"; return }
$azureBase   = $env:AZURE_BASE_URL;   if (-not $azureBase)   { Write-Error "AZURE_BASE_URL not set in .env"; return }
$apiVersion  = $env:API_VERSION;      if (-not $apiVersion)  { $apiVersion = "2024-07-01" }
$simAdmin    = $env:ADMIN_KEY;        if (-not $simAdmin)    { Write-Error "ADMIN_KEY not set in .env"; return }
$simQuery    = $env:QUERY_KEY;        if (-not $simQuery)    { Write-Error "QUERY_KEY not set in .env"; return }
$azureAdmin  = $env:AZURE_ADMIN_KEY;  if (-not $azureAdmin)  { Write-Error "AZURE_ADMIN_KEY not set in .env"; return }
$azureQuery  = $env:AZURE_QUERY_KEY;  if (-not $azureQuery)  { Write-Error "AZURE_QUERY_KEY not set in .env"; return }

# -- Helpers ------------------------------------------------------------

# Accept self-signed certs from local simulator
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $script:SkipCert = @{ SkipCertificateCheck = $true }
} else {
    # PowerShell 5.1 -- disable cert validation globally (for this session)
    Add-Type @"
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public class TrustAll {
    public static void Enable() {
        ServicePointManager.ServerCertificateValidationCallback =
            (s, cert, chain, errors) => true;
    }
}
"@
    [TrustAll]::Enable()
    $script:SkipCert = @{}
}

function Invoke-SearchApi {
    param(
        [string]$Label,
        [string]$BaseUrl,
        [string]$Path,
        [string]$Method = "GET",
        [string]$ApiKey,
        [object]$Body
    )
    $uri = "$BaseUrl$Path"
    if ($uri -notmatch "[?&]api-version=") {
        $sep = if ($uri.Contains("?")) { "&" } else { "?" }
        $uri += "${sep}api-version=$apiVersion"
    }

    $headers = @{ "api-key" = $ApiKey }
    $params = @{
        Uri         = $uri
        Method      = $Method
        Headers     = $headers
        ContentType = "application/json"
        ErrorAction = "Stop"
    } + $script:SkipCert

    if ($Body) {
        $jsonBody = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 20 }
        $params["Body"] = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)
    }

    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        $dash = [char]0x2D
        Write-Warning "[$Label] HTTP $statusCode $dash $($_.Exception.Message)"
        return $null
    }
}

function Remove-IgnoredFields {
    param([object]$Obj, [string[]]$Fields)
    if ($null -eq $Obj) { return $null }
    $json = $Obj | ConvertTo-Json -Depth 20
    $parsed = $json | ConvertFrom-Json

    function Strip([object]$node) {
        if ($node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($f in $Fields) {
                if ($node.PSObject.Properties[$f]) {
                    $node.PSObject.Properties.Remove($f)
                }
            }
            foreach ($prop in $node.PSObject.Properties) {
                $prop.Value = Strip $prop.Value
            }
        }
        elseif ($node -is [System.Collections.IEnumerable] -and $node -isnot [string]) {
            $list = @()
            foreach ($item in $node) { $list += (Strip $item) }
            return $list
        }
        return $node
    }

    return Strip $parsed
}

function Compare-Responses {
    param(
        [string]$TestName,
        [object]$SimResponse,
        [object]$AzureResponse
    )

    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $TestName" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan

    if ($null -eq $SimResponse -and $null -eq $AzureResponse) {
        Write-Host "  Both returned errors / null" -ForegroundColor Yellow
        $script:comparisonResults += @{
            scenario = $TestName; result = "both_error"
            simulator = $null; azure = $null
        }
        return
    }
    if ($null -eq $SimResponse) {
        Write-Host "  SIM returned error, AZURE returned data" -ForegroundColor Red
        if ($ShowFullResponse) { Write-Host ($AzureResponse | ConvertTo-Json -Depth 20) -ForegroundColor DarkGray }
        $script:comparisonResults += @{
            scenario = $TestName; result = "sim_error"
            simulator = $null; azure = $AzureResponse
        }
        return
    }
    if ($null -eq $AzureResponse) {
        Write-Host "  SIM returned data, AZURE returned error" -ForegroundColor Red
        if ($ShowFullResponse) { Write-Host ($SimResponse | ConvertTo-Json -Depth 20) -ForegroundColor DarkGray }
        $script:comparisonResults += @{
            scenario = $TestName; result = "azure_error"
            simulator = $SimResponse; azure = $null
        }
        return
    }

    $simClean   = Remove-IgnoredFields -Obj $SimResponse   -Fields $IgnoreFields
    $azureClean = Remove-IgnoredFields -Obj $AzureResponse -Fields $IgnoreFields

    $simJson   = $simClean   | ConvertTo-Json -Depth 20
    $azureJson = $azureClean | ConvertTo-Json -Depth 20

    $isMatch = ($simJson -eq $azureJson)

    # Record result for JSON report
    $script:comparisonResults += @{
        scenario  = $TestName
        result    = if ($isMatch) { "match" } else { "different" }
        simulator = $SimResponse
        azure     = $AzureResponse
    }

    if ($isMatch) {
        Write-Host "  MATCH" -ForegroundColor Green
        if ($ShowFullResponse) {
            Write-Host "  Response:" -ForegroundColor DarkGray
            Write-Host $simJson -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "  DIFFERENCES FOUND" -ForegroundColor Red

        # Show side-by-side diff
        $simLines   = $simJson   -split "`n"
        $azureLines = $azureJson -split "`n"
        $maxLines   = [Math]::Max($simLines.Count, $azureLines.Count)

        Write-Host ""
        $pipeChar = [string][char]0x7C
        Write-Host ("  --- SIMULATOR ---".PadRight(50) + " $pipeChar " + "--- AZURE ---") -ForegroundColor Yellow
        Write-Host (("  " + "-" * 48).PadRight(50) + " $pipeChar " + ("-" * 48)) -ForegroundColor DarkGray

        for ($i = 0; $i -lt $maxLines; $i++) {
            $sLine = if ($i -lt $simLines.Count)   { $simLines[$i].TrimEnd()   } else { "" }
            $aLine = if ($i -lt $azureLines.Count)  { $azureLines[$i].TrimEnd() } else { "" }

            $truncS = $sLine.Substring(0, [Math]::Min(48, $sLine.Length))
            $truncA = $aLine.Substring(0, [Math]::Min(48, $aLine.Length))
            $lineFmt = ("  " + $truncS).PadRight(50) + " $pipeChar " + $truncA

            if ($sLine -eq $aLine) {
                Write-Host $lineFmt -ForegroundColor DarkGray
            }
            else {
                Write-Host $lineFmt -ForegroundColor Red
            }
        }
    }
}

# -- Scenarios ----------------------------------------------------------

$indexDef = @{
    name = $IndexName
    fields = @(
        @{ name = "hotelId";             type = "Edm.String";              key = $true; filterable = $true }
        @{ name = "hotelName";           type = "Edm.String";              searchable = $true; filterable = $true; sortable = $true }
        @{ name = "description";         type = "Edm.String";              searchable = $true }
        @{ name = "category";            type = "Edm.String";              searchable = $true; filterable = $true; facetable = $true }
        @{ name = "rating";              type = "Edm.Double";              filterable = $true; sortable = $true }
        @{ name = "parkingIncluded";     type = "Edm.Boolean";             filterable = $true }
        @{ name = "lastRenovationDate";  type = "Edm.DateTimeOffset";      filterable = $true; sortable = $true }
        @{ name = "tags";                type = "Collection(Edm.String)";  searchable = $true; filterable = $true }
    )
}

$docsJsonTemplate = @'
{
  "value": [
    {
      "@search.action": "upload",
      "hotelId": "1",
      "hotelName": "Grand Azure Hotel",
      "description": "A luxurious hotel in the heart of downtown with stunning views and world-class amenities.",
      "category": "Luxury",
      "rating": 4.8,
      "parkingIncluded": true,
      "lastRenovationDate": "2023-06-15T00:00:00Z",
      "tags": ["luxury", "spa", "pool", "wifi"]
    },
    {
      "@search.action": "upload",
      "hotelId": "2",
      "hotelName": "Budget Inn Express",
      "description": "Affordable accommodation for business travelers with free breakfast and parking.",
      "category": "Budget",
      "rating": 3.5,
      "parkingIncluded": true,
      "lastRenovationDate": "2020-01-10T00:00:00Z",
      "tags": ["budget", "breakfast", "wifi"]
    },
    {
      "@search.action": "upload",
      "hotelId": "3",
      "hotelName": "Seaside Resort {AMP} Spa",
      "description": "Beachfront resort with private beach access, multiple pools, and a full-service spa.",
      "category": "Resort",
      "rating": 4.5,
      "parkingIncluded": false,
      "lastRenovationDate": "2022-09-01T00:00:00Z",
      "tags": ["beach", "spa", "pool", "restaurant"]
    },
    {
      "@search.action": "upload",
      "hotelId": "4",
      "hotelName": "Mountain Lodge Retreat",
      "description": "Cozy mountain retreat perfect for hiking enthusiasts and nature lovers.",
      "category": "Boutique",
      "rating": 4.2,
      "parkingIncluded": true,
      "lastRenovationDate": "2021-05-20T00:00:00Z",
      "tags": ["hiking", "nature", "fireplace", "pets"]
    },
    {
      "@search.action": "upload",
      "hotelId": "5",
      "hotelName": "City Center Suites",
      "description": "Modern suites in the business district with kitchenette and workspace.",
      "category": "Business",
      "rating": 4.0,
      "parkingIncluded": false,
      "lastRenovationDate": "2024-02-28T00:00:00Z",
      "tags": ["business", "wifi", "workspace", "kitchen"]
    }
  ]
}
'@
$docsJson = $docsJsonTemplate.Replace('{AMP}', '&')

function Run-Scenario {
    param([string]$Name)

    switch ($Name) {
        "ListIndexes" {
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes" -ApiKey $simAdmin
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes" -ApiKey $azureAdmin
            Compare-Responses "List Indexes" $sim $azure
        }

        "CreateIndex" {
            # Clean up first
            Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName" -Method DELETE -ApiKey $simAdmin   | Out-Null
            Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName" -Method DELETE -ApiKey $azureAdmin | Out-Null
            Start-Sleep -Seconds 1

            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes" -Method POST -ApiKey $simAdmin   -Body $indexDef
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes" -Method POST -ApiKey $azureAdmin -Body $indexDef
            Compare-Responses "Create Index '$IndexName'" $sim $azure
        }

        "UploadDocs" {
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/index" -Method POST -ApiKey $simAdmin   -Body $docsJson
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/index" -Method POST -ApiKey $azureAdmin -Body $docsJson
            Compare-Responses "Upload Documents" $sim $azure
            Write-Host "  (waiting 2s for indexing...)" -ForegroundColor DarkGray
            Start-Sleep -Seconds 2
        }

        "SimpleSearch" {
            $body = @{ search = "luxury spa"; top = 10 }
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $simQuery   -Body $body
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $azureQuery -Body $body
            Compare-Responses "Simple Search: 'luxury spa'" $sim $azure
        }

        "FilterSearch" {
            $body = @{ search = "*"; filter = "rating ge 4.0"; orderby = "rating desc"; top = 10 }
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $simQuery   -Body $body
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $azureQuery -Body $body
            Compare-Responses "Filter Search: rating >= 4.0" $sim $azure
        }

        "CountSearch" {
            $body = @{ search = "*"; count = $true; top = 3 }
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $simQuery   -Body $body
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $azureQuery -Body $body
            Compare-Responses "Search with Count" $sim $azure
        }

        "PagingSearch" {
            $body = @{ search = "*"; orderby = "hotelName"; top = 2; skip = 2 }
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $simQuery   -Body $body
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/search" -Method POST -ApiKey $azureQuery -Body $body
            Compare-Responses "Paged Search: top=2 skip=2" $sim $azure
        }

        "GetDocument" {
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/1" -ApiKey $simQuery
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/1" -ApiKey $azureQuery
            Compare-Responses "Get Document (key=1)" $sim $azure
        }

        "DocCount" {
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName/docs/`$count" -ApiKey $simQuery
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName/docs/`$count" -ApiKey $azureQuery
            Compare-Responses "Document Count" $sim $azure
        }

        "ServiceStats" {
            $sim   = Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/servicestats" -ApiKey $simAdmin
            $azure = Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/servicestats" -ApiKey $azureAdmin
            Compare-Responses "Service Statistics" $sim $azure
        }

        "Cleanup" {
            Write-Host ""
            Write-Host "Cleaning up test index '$IndexName'..." -ForegroundColor Yellow
            Invoke-SearchApi -Label "SIM"   -BaseUrl $simBase   -Path "/indexes/$IndexName" -Method DELETE -ApiKey $simAdmin   | Out-Null
            Invoke-SearchApi -Label "AZURE" -BaseUrl $azureBase -Path "/indexes/$IndexName" -Method DELETE -ApiKey $azureAdmin | Out-Null
            Write-Host "  Done." -ForegroundColor Green
        }
    }
}

# -- Main ---------------------------------------------------------------
Write-Host ""
Write-Host ("+------------------------------------------------------------------+") -ForegroundColor Cyan
Write-Host ("   Azure AI Search -- Simulator vs Real Azure -- Comparison Tool") -ForegroundColor Cyan
Write-Host ("+------------------------------------------------------------------+") -ForegroundColor Cyan
Write-Host ("   Simulator : $simBase") -ForegroundColor White
Write-Host ("   Azure     : $azureBase") -ForegroundColor White
Write-Host ("   API Ver.  : $apiVersion") -ForegroundColor White
Write-Host ("   Index     : $IndexName") -ForegroundColor White
Write-Host ("+------------------------------------------------------------------+") -ForegroundColor Cyan

if ($Scenario -eq "All") {
    $scenarios = @("CreateIndex", "UploadDocs", "SimpleSearch", "FilterSearch",
                   "CountSearch", "PagingSearch", "GetDocument", "DocCount",
                   "ServiceStats", "Cleanup")
    foreach ($s in $scenarios) {
        Run-Scenario $s
    }
}
else {
    Run-Scenario $Scenario
}

# -- Save JSON report ---------------------------------------------------
if (-not $NoSave -and $script:comparisonResults.Count -gt 0) {
    $report = @{
        timestamp  = (Get-Date -Format "o")
        simulator  = $simBase
        azure      = $azureBase
        apiVersion = $apiVersion
        index      = $IndexName
        summary    = @{
            total   = $script:comparisonResults.Count
            match   = ($script:comparisonResults | Where-Object { $_.result -eq "match" }).Count
            differ  = ($script:comparisonResults | Where-Object { $_.result -eq "different" }).Count
            errors  = ($script:comparisonResults | Where-Object { $_.result -match "error" }).Count
        }
        scenarios  = $script:comparisonResults
    }
    $report | ConvertTo-Json -Depth 20 | Out-File -FilePath $OutputFile -Encoding UTF8
    Write-Host ""
    Write-Host "  Results saved to: $OutputFile" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Comparison complete." -ForegroundColor Green
Write-Host ""
