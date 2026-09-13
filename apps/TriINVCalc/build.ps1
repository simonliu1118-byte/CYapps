$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = (Get-Content (Join-Path $projectDir 'VERSION') -Raw).Trim()
$build = [int]((Get-Content (Join-Path $projectDir 'BUILD') -Raw).Trim())
$displayVersion = if ($build -gt 0) { "$version Build $build" } else { $version }

$distDir = Join-Path $projectDir 'dist'
$packageDir = Join-Path $distDir 'TriINVCalc'
$zipName = if ($build -gt 0) {
    "TriINVCalc_V{0}_Build{1}.zip" -f $version, $build
} else {
    "TriINVCalc_V{0}.zip" -f $version
}
$zipPath = Join-Path $distDir $zipName

if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item $packageDir -ItemType Directory -Force | Out-Null

$iconB64Path = Join-Path $projectDir 'assets/icon.ico.b64'
$iconPath = Join-Path $projectDir 'assets/icon.ico'
$resourcePath = Join-Path $projectDir 'rsrc.syso'
$manifestPath = Join-Path $projectDir 'assets/app.manifest'

$iconBase64 = (Get-Content $iconB64Path -Raw) -replace '\s', ''
[IO.File]::WriteAllBytes($iconPath, [Convert]::FromBase64String($iconBase64))

go run github.com/akavel/rsrc@v0.10.2 -manifest $manifestPath -ico $iconPath -o $resourcePath

$env:GOOS = 'windows'
$env:GOARCH = 'amd64'
$env:CGO_ENABLED = '0'

go test ./...

$ldflags = "-H windowsgui -s -w -X 'main.appVersion=$displayVersion'"
go build -buildvcs=false -trimpath -ldflags $ldflags -o (Join-Path $packageDir 'TriINVCalc.exe') .

Copy-Item (Join-Path $projectDir '使用說明.txt') $packageDir
Copy-Item (Join-Path $projectDir ("V{0}.txt" -f $version)) $packageDir
Compress-Archive -Path $packageDir -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Built $zipPath"
Write-Host "Version: V$displayVersion"
