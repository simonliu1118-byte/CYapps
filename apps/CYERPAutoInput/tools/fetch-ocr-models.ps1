$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$modelDir = Join-Path $root "runtime\ocr"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

$rapidRevision = "a8814c3b298bab79219341e73f687ba710ab4031"
$models = @(
    @{
        Name = "ch_PP-OCRv5_det_mobile.onnx"
        Url = "https://raw.githubusercontent.com/meloht/RapidOCRSharpOnnx/$rapidRevision/RapidOCRSharpOnnx.TestCommon/Models/ch_PP-OCRv5_det_mobile.onnx"
        MinBytes = 4000000
    },
    @{
        Name = "ch_PP-OCRv5_rec_mobile.onnx"
        Url = "https://raw.githubusercontent.com/meloht/RapidOCRSharpOnnx/$rapidRevision/RapidOCRSharpOnnx.TestCommon/Models/ch_PP-OCRv5_rec_mobile.onnx"
        MinBytes = 15000000
    },
    @{
        Name = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"
        Url = "https://raw.githubusercontent.com/meloht/RapidOCRSharpOnnx/$rapidRevision/RapidOCRSharpOnnx.TestCommon/Models/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"
        MinBytes = 900000
    }
)

function Get-Model([hashtable]$model) {
    $dest = Join-Path $modelDir $model.Name
    if (Test-Path $dest) {
        $existing = Get-Item $dest
        if ($existing.Length -ge [int64]$model.MinBytes) {
            Write-Host "OCR model already present: $($model.Name) ($($existing.Length) bytes)"
            return
        }
        Remove-Item -Force $dest
    }

    $temp = "$dest.download"
    Remove-Item -Force $temp -ErrorAction SilentlyContinue
    Write-Host "Downloading pinned OCR model: $($model.Name)"
    & curl.exe -L --fail --retry 3 --retry-delay 2 --output $temp $model.Url
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to download $($model.Name), curl exit code $LASTEXITCODE"
    }

    $item = Get-Item $temp
    if ($item.Length -lt [int64]$model.MinBytes) {
        Remove-Item -Force $temp
        throw "Downloaded OCR model $($model.Name) is unexpectedly small: $($item.Length) bytes"
    }
    Move-Item -Force $temp $dest
}

foreach ($model in $models) {
    Get-Model $model
}

# Remove any stale server-recognizer experiment from local workspaces so the
# package contains only the validated model family used by this build.
Remove-Item -Force (Join-Path $modelDir "PP-OCRv5_server_rec.onnx") -ErrorAction SilentlyContinue

$hashLines = foreach ($model in $models) {
    $path = Join-Path $modelDir $model.Name
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($model.Name)"
}
$hashLines | Set-Content -Encoding ascii (Join-Path $modelDir "SHA256.txt")

Write-Host "PaddleOCR runtime models ready at $modelDir"
Get-ChildItem $modelDir | Select-Object Name, Length
