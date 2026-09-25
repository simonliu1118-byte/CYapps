$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$modelDir = Join-Path $root "runtime\ocr"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

$rapidRevision = "a8814c3b298bab79219341e73f687ba710ab4031"
$paddleRevision = "b70df217f4fd99d14f970bad092cebe7d74cc4d1"

$models = @(
    @{
        Name = "ch_PP-OCRv5_det_mobile.onnx"
        Url = "https://raw.githubusercontent.com/meloht/RapidOCRSharpOnnx/$rapidRevision/RapidOCRSharpOnnx.TestCommon/Models/ch_PP-OCRv5_det_mobile.onnx"
        MinBytes = 4000000
    },
    @{
        Name = "PP-OCRv5_server_rec.onnx"
        Url = "https://huggingface.co/PaddlePaddle/PP-OCRv5_server_rec_onnx/resolve/$paddleRevision/inference.onnx?download=true"
        MinBytes = 80000000
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

$hashLines = foreach ($model in $models) {
    $path = Join-Path $modelDir $model.Name
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($model.Name)"
}
$hashLines | Set-Content -Encoding ascii (Join-Path $modelDir "SHA256.txt")

Write-Host "PaddleOCR runtime models ready at $modelDir"
Get-ChildItem $modelDir | Select-Object Name, Length
