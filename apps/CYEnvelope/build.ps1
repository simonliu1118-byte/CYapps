$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = (Get-Content (Join-Path $projectDir 'VERSION') -Raw).Trim()
$distDir = Join-Path $projectDir 'dist'
$packageDir = Join-Path $distDir 'CYEnvelope'
$zipPath = Join-Path $distDir ("CYEnvelope_V{0}.zip" -f $version)

if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item $packageDir -ItemType Directory -Force | Out-Null
New-Item (Join-Path $packageDir 'Data') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $packageDir 'Logs') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $packageDir 'Cache') -ItemType Directory -Force | Out-Null

$iconPath = Join-Path $projectDir 'assets/icon.ico'
$resourcePath = Join-Path $projectDir 'cmd/CYEnvelope/rsrc.syso'
$iconBase64 = (Get-Content (Join-Path $projectDir 'assets/icon.ico.b64') -Raw).Trim()
[IO.File]::WriteAllBytes($iconPath, [Convert]::FromBase64String($iconBase64))
go run github.com/akavel/rsrc@v0.10.2 -manifest (Join-Path $projectDir 'assets/app.manifest') -ico $iconPath -o $resourcePath

$env:GOOS = 'windows'
$env:GOARCH = 'amd64'
$env:CGO_ENABLED = '0'
go test ./...
go build -buildvcs=false -trimpath -ldflags '-H windowsgui -s -w' -o (Join-Path $packageDir 'CYEnvelope.exe') ./cmd/CYEnvelope
Copy-Item (Join-Path $projectDir '使用說明.txt') $packageDir
Copy-Item (Join-Path $projectDir ("V{0}.txt" -f $version)) $packageDir
Compress-Archive -Path $packageDir -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Built $zipPath"
