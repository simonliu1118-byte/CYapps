$ErrorActionPreference = "Stop"

$sourceRevision = "887633147ef363b5b412458f687354293159c131"
$expectedSha256 = "b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11"
$sourceUrl = "https://raw.githubusercontent.com/simonliu1118-byte/AITeam/$sourceRevision/shared/cy-visual/icon-family/apps/erp-autoinput/Auto.ico"

$projectRoot = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $projectRoot "assets"
$target = Join-Path $assetDir "Auto.ico"

New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -OutFile $target

$actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expectedSha256) {
    Remove-Item $target -Force -ErrorAction SilentlyContinue
    throw "Canonical CYERPAutoInput icon hash mismatch. expected=$expectedSha256 actual=$actual"
}

Write-Host "Canonical CYERPAutoInput Auto.ico ready: $target"
Write-Host "AITeam revision: $sourceRevision"
Write-Host "SHA-256: $actual"
