param(
    [string]$Version = "",
    [string]$Commit = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $ProjectRoot "VERSION") -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid CYInvoice version: $Version"
}

if ([string]::IsNullOrWhiteSpace($Commit)) {
    try {
        $Commit = (& git -C $ProjectRoot rev-parse --short=12 HEAD).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "git rev-parse failed."
        }
    }
    catch {
        $Commit = "unknown"
    }
}

$ResourceFile = Join-Path $ProjectRoot "cmd/CYInvoice/rsrc_windows_amd64.syso"
$DistRoot = Join-Path $ProjectRoot "dist"
$ReleaseDir = Join-Path $DistRoot "CYInvoice"
$ExePath = Join-Path $ReleaseDir "CYInvoice.exe"
$ZipPath = Join-Path $DistRoot ("CYInvoice_V{0}.zip" -f $Version)

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Data") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Cache/InvoicePDF") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Logs") -ItemType Directory -Force | Out-Null

$OldGOOS = $env:GOOS
$OldGOARCH = $env:GOARCH
$OldGOAMD64 = $env:GOAMD64
$OldCGO = $env:CGO_ENABLED

try {
    $env:GOOS = "windows"
    $env:GOARCH = "amd64"
    $env:GOAMD64 = "v1"
    $env:CGO_ENABLED = "0"

    Push-Location $ProjectRoot
    try {
        & go run github.com/akavel/rsrc@v0.10.2 `
            -arch amd64 `
            -ico "assets/CYInvoice.ico" `
            -manifest "assets/CYInvoice.exe.manifest" `
            -o $ResourceFile
        if ($LASTEXITCODE -ne 0) {
            throw "Resource generation failed."
        }

        $LdFlags = "-H=windowsgui -s -w -X cyinvoice/internal/version.Value=$Version -X cyinvoice/internal/version.Commit=$Commit"
        & go build -trimpath -buildvcs=false -ldflags $LdFlags -o $ExePath ./cmd/CYInvoice
        if ($LASTEXITCODE -ne 0) {
            throw "Go build failed."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:GOOS = $OldGOOS
    $env:GOARCH = $OldGOARCH
    $env:GOAMD64 = $OldGOAMD64
    $env:CGO_ENABLED = $OldCGO
    if (Test-Path $ResourceFile) {
        Remove-Item $ResourceFile -Force
    }
}

Copy-Item (Join-Path $ProjectRoot "使用說明.txt") $ReleaseDir

$ReleaseDate = Get-Date -Format "yyyy/MM/dd"
$VersionNote = @"
Version: V$Version
Date: $ReleaseDate
Commit: $Commit

CYInvoice $Version。

已包含手動開立、MO店+／酷澎共用匯入確認、開立清單、狀態查詢、防重複開票及測試池個資遮蔽。
鼎新 ERP 已統一確認入口，待取得實際銷貨單樣本與欄位規則後完成來源解析器。
"@
Set-Content -Path (Join-Path $ReleaseDir ("V{0}.txt" -f $Version)) -Value $VersionNote -Encoding UTF8

Add-Type -AssemblyName System.IO.Compression.FileSystem
$Archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void]$Archive.CreateEntry("CYInvoice/")
    foreach ($Directory in Get-ChildItem -LiteralPath $ReleaseDir -Directory -Recurse) {
        $Relative = [IO.Path]::GetRelativePath($DistRoot, $Directory.FullName).Replace('\', '/') + "/"
        [void]$Archive.CreateEntry($Relative)
    }
    foreach ($File in Get-ChildItem -LiteralPath $ReleaseDir -File -Recurse) {
        $Relative = [IO.Path]::GetRelativePath($DistRoot, $File.FullName).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Archive,
            $File.FullName,
            $Relative,
            [System.IO.Compression.CompressionLevel]::Optimal
        )
    }
}
finally {
    $Archive.Dispose()
}

Write-Host "Built: $ExePath"
Write-Host "Package: $ZipPath"
