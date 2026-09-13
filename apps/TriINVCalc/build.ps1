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

$assetsDir = Join-Path $projectDir 'assets'
$iconParts = @(Get-ChildItem $assetsDir -Filter 'icon.ico.b64.part*' | Sort-Object Name)
if ($iconParts.Count -eq 0) { throw 'Icon base64 parts not found.' }
$iconPath = Join-Path $assetsDir 'icon.ico'
$resourcePath = Join-Path $projectDir 'rsrc.syso'
$manifestPath = Join-Path $assetsDir 'app.manifest'

$iconBase64 = (($iconParts | ForEach-Object { Get-Content $_.FullName -Raw }) -join '') -replace '\s', ''
[IO.File]::WriteAllBytes($iconPath, [Convert]::FromBase64String($iconBase64))

$expectedIconSha256 = '423E8B292DC2C812653DD72E9B0CC90E439B0189457F7808A7DFA70CC77BB758'
$actualIconSha256 = (Get-FileHash $iconPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actualIconSha256 -ne $expectedIconSha256) {
    throw "Icon checksum mismatch. Expected $expectedIconSha256, got $actualIconSha256"
}

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
Write-Host "Icon SHA-256: $actualIconSha256"
