$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $root '../..')
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$build = (Get-Content (Join-Path $root 'BUILD') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $build -notmatch '^\d+$') {
    throw "Invalid VERSION/BUILD: $version / $build"
}
$versionLabel = "V$version"
if ([int]$build -gt 0) { $versionLabel += "_Build$build" }
$stageRoot = Join-Path $root 'dist/stage'
$output = Join-Path $stageRoot 'CYEnvelope'
$zip = Join-Path $root "dist/CYEnvelope_${versionLabel}_windows_x64_test.zip"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

dotnet publish (Join-Path $root 'src/CYEnvelope/CYEnvelope.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'CYEnvelope publish failed' }
if (-not (Test-Path (Join-Path $output 'CYEnvelope.exe'))) {
    throw 'CYEnvelope.exe missing from published output'
}
if ((Get-Item (Join-Path $output 'CYEnvelope.exe')).Length -lt 100000) {
    throw 'CYEnvelope.exe unexpectedly small'
}
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $output -DestinationPath $zip -CompressionLevel Optimal

python (Join-Path $repoRoot '.github/scripts/scan-public-package.py') $zip
if ($LASTEXITCODE -ne 0) { throw 'Public package safety scan failed' }
$digest = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$zip.sha256"
"$digest  $(Split-Path $zip -Leaf)" | Set-Content $hashFile -Encoding ascii
Write-Host "Package: $zip"
Write-Host "SHA-256: $digest"
