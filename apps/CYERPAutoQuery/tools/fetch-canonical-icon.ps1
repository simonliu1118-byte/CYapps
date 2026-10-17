$ErrorActionPreference = "Stop"

$sourceRevision = "3ec4ed8fc6bbdd2c007b8d687dd3136773b2379d"
$expectedSha256 = "0ce949fc99732895de53ea87d798fbf1d9f7dcfbd52674d0af45da7f402bb999"
$sourceUrl = "https://raw.githubusercontent.com/simonliu1118-byte/AITeam/$sourceRevision/shared/cy-visual/icon-family/apps/erp-autoquery/AutoQ.ico"

$projectRoot = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $projectRoot "assets"
$target = Join-Path $assetDir "AutoQ.ico"

New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -OutFile $target

$actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expectedSha256) {
    Remove-Item $target -Force -ErrorAction SilentlyContinue
    throw "Canonical CYERPAutoQuery icon hash mismatch. expected=$expectedSha256 actual=$actual"
}

Write-Host "Canonical CYERPAutoQuery AutoQ.ico ready: $target"
Write-Host "AITeam revision: $sourceRevision"
Write-Host "SHA-256: $actual"
