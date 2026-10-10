param(
    # Used only by the OCR model mirror workflow, which creates the Release
    # asset that normal builds download first.
    [switch]$UpstreamOnly
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$modelDir = Join-Path $root "runtime\ocr"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

# tools/ocr-models.json is the single source of the model set. SHA-256 is the
# integrity gate: every source must deliver exactly these bytes.
$manifest = Get-Content (Join-Path $PSScriptRoot "ocr-models.json") -Raw | ConvertFrom-Json
$releaseBaseUrl = "https://github.com/simonliu1118-byte/CYapps/releases/download/$($manifest.releaseTag)"

function Test-Model([string]$path, $model) {
    if (-not (Test-Path $path)) { return $false }
    if ((Get-Item $path).Length -ne [int64]$model.size) { return $false }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return $hash -eq $model.sha256
}

function Get-Model($model) {
    $dest = Join-Path $modelDir $model.name
    if (Test-Model $dest $model) {
        Write-Host "OCR model already present and verified: $($model.name)"
        return
    }
    Remove-Item -Force $dest -ErrorAction SilentlyContinue

    # Primary source is this repository's Release asset. The pinned upstream
    # revision is used only when the Release asset cannot be downloaded.
    $sources = @()
    if (-not $UpstreamOnly) { $sources += "$releaseBaseUrl/$($model.name)" }
    $sources += "$($manifest.upstreamBaseUrl)/$($model.name)"

    $temp = "$dest.download"
    foreach ($url in $sources) {
        Remove-Item -Force $temp -ErrorAction SilentlyContinue
        & curl.exe -L --fail --silent --show-error --retry 3 --retry-delay 2 --output $temp $url
        if ($LASTEXITCODE -ne 0) {
            Write-Host "OCR model source unavailable (curl exit $LASTEXITCODE): $url"
            continue
        }
        if (-not (Test-Model $temp $model)) {
            Remove-Item -Force $temp
            throw "OCR model $($model.name) from $url does not match the pinned size/SHA-256."
        }
        Move-Item -Force $temp $dest
        Write-Host "OCR model downloaded and verified: $($model.name) <- $url"
        return
    }
    throw "No source delivered OCR model $($model.name)."
}

foreach ($model in $manifest.models) {
    Get-Model $model
}

$hashLines = foreach ($model in $manifest.models) { "$($model.sha256)  $($model.name)" }
$hashLines | Set-Content -Encoding ascii (Join-Path $modelDir "SHA256.txt")

Write-Host "PaddleOCR runtime models ready at $modelDir"
Get-ChildItem $modelDir | Select-Object Name, Length
