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

dotnet publish (Join-Path $root 'src/CYEnvelope/CYEnvelope.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:DebugSymbols=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'CYEnvelope publish failed' }
if (-not (Test-Path (Join-Path $output 'CYEnvelope.exe'))) {
    throw 'CYEnvelope.exe missing from published output'
}
if ((Get-Item (Join-Path $output 'CYEnvelope.exe')).Length -lt 1000000) {
    throw 'CYEnvelope.exe unexpectedly small'
}
$nativeNames = @(
    'D3DCompiler_47_cor3.dll', 'e_sqlite3.dll', 'PenImc_cor3.dll',
    'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll', 'wpfgfx_cor3.dll'
)
$runtime = Join-Path $output 'Runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach ($name in $nativeNames) {
    $source = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing native dependency: $name" }
    Move-Item -LiteralPath $source -Destination (Join-Path $runtime $name)
}
$rootNames = @(Get-ChildItem -LiteralPath $output -Force | Select-Object -ExpandProperty Name)
$expectedRoot = @('CYEnvelope.exe', 'VERSION', 'BUILD', 'Runtime')
if (@(Compare-Object $expectedRoot $rootNames).Count -ne 0) {
    throw "Unexpected portable root contents: $($rootNames -join ', ')"
}
$runtimeNames = @(Get-ChildItem -LiteralPath $runtime -Force | Select-Object -ExpandProperty Name)
if (@(Compare-Object $nativeNames $runtimeNames).Count -ne 0) {
    throw "Unexpected Runtime contents: $($runtimeNames -join ', ')"
}
Write-Host "Portable root: $($rootNames -join ', '); Runtime: $($runtimeNames -join ', ')"
python (Join-Path $repoRoot '.github/scripts/scan-public-package.py') $stageRoot
if ($LASTEXITCODE -ne 0) { throw 'Public package safety scan failed' }
Write-Host "Portable folder: $output"
