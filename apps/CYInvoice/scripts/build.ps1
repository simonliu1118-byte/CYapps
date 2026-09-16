param(
    [string]$Version = "",
    [string]$Build = "",
    [string]$Commit = "",
    [switch]$Engineering
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $ProjectRoot "VERSION") -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid CYInvoice version: $Version" }
if ([string]::IsNullOrWhiteSpace($Build)) {
    $Build = (Get-Content (Join-Path $ProjectRoot "BUILD") -Raw).Trim()
}
if ($Build -notmatch '^\d+$') { throw "Invalid CYInvoice build: $Build" }

$BuildNumber = [int]$Build
$DisplayVersion = "V$Version"
$ArtifactVersion = "V$Version"
if ($BuildNumber -gt 0) {
    $DisplayVersion = "V$Version Build $BuildNumber"
    $ArtifactVersion = "V${Version}_Build${BuildNumber}"
}
if ([string]::IsNullOrWhiteSpace($Commit)) {
    try { $Commit = (& git -C $ProjectRoot rev-parse --short=12 HEAD).Trim() }
    catch { $Commit = "unknown" }
}

$DistRoot = Join-Path $ProjectRoot "dist"
$PublishDir = Join-Path $ProjectRoot "out"
$ReleaseDir = Join-Path $DistRoot "CYInvoice"
$ZipPath = Join-Path $DistRoot ("CYInvoice_{0}.zip" -f $ArtifactVersion)
if (Test-Path $DistRoot) { Remove-Item $DistRoot -Recurse -Force }
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null

Push-Location $ProjectRoot
try {
    & dotnet publish src/CYInvoice.WinForms/CYInvoice.WinForms.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
finally { Pop-Location }

Copy-Item (Join-Path $PublishDir "*") $ReleaseDir -Recurse -Force
if (!(Test-Path (Join-Path $ReleaseDir "CYInvoice.exe") -PathType Leaf)) { throw "Published CYInvoice.exe is missing." }
Get-ChildItem -LiteralPath $ReleaseDir -Filter "Microsoft.Web.WebView2.*.xml" -File | Remove-Item -Force
Copy-Item (Join-Path $ProjectRoot "使用說明.txt") $ReleaseDir -Force
New-Item (Join-Path $ReleaseDir "Data") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Cache/InvoicePDF") -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Logs") -ItemType Directory -Force | Out-Null

$ReleaseDate = Get-Date -Format "yyyy/MM/dd"
$PackageChannel = if ($Engineering) { "engineering" } else { "formal" }
@"
Version: $DisplayVersion
Date: $ReleaseDate
Commit: $Commit
Channel: $PackageChannel

CYInvoice $DisplayVersion，Windows 10/11 x64。
C#／WinForms 已自 V2.0.0 起成為唯一正式產品線。
"@ | Set-Content -Path (Join-Path $ReleaseDir ("{0}.txt" -f $ArtifactVersion)) -Encoding UTF8

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
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $File.FullName, $Relative, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $Archive.Dispose() }

Write-Host "Built: $(Join-Path $ReleaseDir 'CYInvoice.exe')"
Write-Host "Package: $ZipPath"
