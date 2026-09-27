$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $root '../..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$build = (Get-Content (Join-Path $root 'BUILD') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $build -notmatch '^\d+$') {
    throw "Invalid VERSION/BUILD: $version / $build"
}
$stageRoot = Join-Path $root 'dist/stage'
$output = Join-Path $stageRoot 'CYEnvelope'
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet publish (Join-Path $root 'src/CYEnvelope/CYEnvelope.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'CYEnvelope publish failed' }
if (-not (Test-Path (Join-Path $output 'CYEnvelope.exe'))) {
    throw 'CYEnvelope.exe missing from published output'
}
if ((Get-Item (Join-Path $output 'CYEnvelope.exe')).Length -lt 1000000) {
    throw 'CYEnvelope.exe unexpectedly small'
}
$unexpected = @(Get-ChildItem -LiteralPath $output -Force | Where-Object {
    $_.PSIsContainer -or $_.Name -notin @('CYEnvelope.exe', 'VERSION', 'BUILD')
})
if ($unexpected.Count -gt 0) {
    throw "Unexpected files in portable folder: $($unexpected.Name -join ', ')"
}
python (Join-Path $repoRoot '.github/scripts/scan-public-package.py') $stageRoot
if ($LASTEXITCODE -ne 0) { throw 'Public package safety scan failed' }
Write-Host "Portable folder: $output"
