$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $root '../..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$build = (Get-Content (Join-Path $root 'BUILD') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $build -notmatch '^\d+$') {
    throw "Invalid VERSION/BUILD: $version / $build"
}
# Bundled fonts (pinned URL + SHA-256, not stored in Git); the app is compiled with them embedded.
& (Join-Path $root 'tools/fetch-fonts.ps1')
$stageRoot = Join-Path $root 'dist/stage'
$output = Join-Path $stageRoot 'CYEnvelope'
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtime = Join-Path $output 'Runtime'
$launcherStage = Join-Path $root 'dist/launcher-stage'
if (Test-Path $launcherStage) { Remove-Item $launcherStage -Recurse -Force }

dotnet publish (Join-Path $root 'src/CYEnvelope/CYEnvelope.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:DebugSymbols=false -o $runtime --nologo
if ($LASTEXITCODE -ne 0) { throw 'CYEnvelope publish failed' }
if (-not (Test-Path (Join-Path $runtime 'CYEnvelope.exe'))) {
    throw 'Runtime/CYEnvelope.exe missing from published output'
}
if ((Get-Item (Join-Path $runtime 'CYEnvelope.exe')).Length -lt 1000000) {
    throw 'Runtime/CYEnvelope.exe unexpectedly small'
}
Move-Item (Join-Path $runtime 'VERSION') $output
Move-Item (Join-Path $runtime 'BUILD') $output
Move-Item (Join-Path $runtime 'FONT_LICENSES.txt') $output
dotnet publish (Join-Path $root 'src/CYEnvelope.Launcher/CYEnvelope.Launcher.csproj') -c Release -r win-x64 -p:DebugType=None -p:DebugSymbols=false -o $launcherStage --nologo
if ($LASTEXITCODE -ne 0) { throw 'CYEnvelope native launcher publish failed' }
Move-Item (Join-Path $launcherStage 'CYEnvelope.exe') $output
Remove-Item $launcherStage -Recurse -Force
$nativeNames = @(
    'D3DCompiler_47_cor3.dll', 'e_sqlite3.dll', 'PenImc_cor3.dll',
    'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll', 'wpfgfx_cor3.dll'
)
foreach ($name in $nativeNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name) -PathType Leaf)) {
        throw "Missing Runtime native dependency: $name"
    }
}
$rootNames = @(Get-ChildItem -LiteralPath $output -Force | Select-Object -ExpandProperty Name)
$expectedRoot = @('CYEnvelope.exe', 'VERSION', 'BUILD', 'FONT_LICENSES.txt', 'Runtime')
if (@(Compare-Object $expectedRoot $rootNames).Count -ne 0) {
    throw "Unexpected portable root contents: $($rootNames -join ', ')"
}
$runtimeNames = @(Get-ChildItem -LiteralPath $runtime -Force | Select-Object -ExpandProperty Name)
if (@(Compare-Object (@('CYEnvelope.exe') + $nativeNames) $runtimeNames).Count -ne 0) {
    throw "Unexpected Runtime contents: $($runtimeNames -join ', ')"
}
Write-Host "Portable root: $($rootNames -join ', '); Runtime: $($runtimeNames -join ', ')"
python (Join-Path $repoRoot '.github/scripts/scan-public-package.py') $stageRoot
if ($LASTEXITCODE -ne 0) { throw 'Public package safety scan failed' }
Write-Host "Portable folder: $output"
