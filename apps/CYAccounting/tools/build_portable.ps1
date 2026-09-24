param([string]$PythonVersion = "3.11.9")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Version = (Get-Content (Join-Path $ProjectRoot "VERSION") -Raw).Trim()
$BuildText = (Get-Content (Join-Path $ProjectRoot "BUILD") -Raw).Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $BuildText -notmatch '^(0|[1-9][0-9]*)$') {
    throw "VERSION or BUILD is invalid"
}
$Build = [int]$BuildText
$BuildSuffix = if ($Build -gt 0) { "_Build_$Build" } else { "" }
$Marker = Join-Path $ProjectRoot "V$Version.txt"
if (!(Test-Path $Marker -PathType Leaf)) { throw "Missing version marker: $Marker" }
$SourceVersion = (& python -c "import sys; sys.path.insert(0, 'app'); from util import APP_VERSION; print(APP_VERSION)" | Select-Object -Last 1).Trim()
$ExpectedVersion = "V$Version" + $(if ($Build -gt 0) { " Build $Build" } else { "" })
if ($LASTEXITCODE -ne 0 -or $SourceVersion -ne $ExpectedVersion) {
    throw "Application version $SourceVersion does not match $ExpectedVersion"
}

# Work only inside these dedicated, initially absent CI output directories.
$StageRoot = Join-Path $ProjectRoot "out/portable-stage"
$DistRoot = Join-Path $ProjectRoot "out/portable-dist"
if ((Test-Path $StageRoot) -or (Test-Path $DistRoot)) {
    throw "Portable output already exists; use a clean checkout instead of overwriting it"
}
$ReleaseDir = Join-Path $StageRoot "CYAccounting"
$RuntimeDir = Join-Path $ReleaseDir "runtime"
$ZipName = "CYAccounting_V${Version}${BuildSuffix}_Windows_x64.zip"
$ZipPath = Join-Path $DistRoot $ZipName
New-Item -ItemType Directory -Path @($RuntimeDir, (Join-Path $ReleaseDir "Data"), $DistRoot) | Out-Null

$EmbedName = "python-$PythonVersion-embed-amd64.zip"
$EmbedArchive = Join-Path $StageRoot $EmbedName
$EmbedUrl = "https://www.python.org/ftp/python/$PythonVersion/$EmbedName"
Invoke-WebRequest -Uri $EmbedUrl -OutFile $EmbedArchive
Expand-Archive -LiteralPath $EmbedArchive -DestinationPath $RuntimeDir

$SitePackages = Join-Path $RuntimeDir "Lib/site-packages"
New-Item -ItemType Directory $SitePackages | Out-Null
& python -m pip install --disable-pip-version-check --no-compile --target $SitePackages -r (Join-Path $ProjectRoot "requirements-runtime.txt")
if ($LASTEXITCODE -ne 0) { throw "Could not install embedded runtime dependencies" }
$PthFile = Get-ChildItem -LiteralPath $RuntimeDir -Filter "python*._pth" -File | Select-Object -First 1
if ($null -eq $PthFile) { throw "Embedded Python path file is missing" }
@("python311.zip", ".", "Lib", "Lib\site-packages", "..\app", "import site") |
    Set-Content -LiteralPath $PthFile.FullName -Encoding ascii

Copy-Item (Join-Path $ProjectRoot "app") $ReleaseDir -Recurse
Copy-Item (Join-Path $ProjectRoot "README.txt") $ReleaseDir
Copy-Item $Marker $ReleaseDir
Copy-Item (Join-Path $ProjectRoot "VERSION") $ReleaseDir
Copy-Item (Join-Path $ProjectRoot "BUILD") $ReleaseDir
Get-ChildItem -LiteralPath $ReleaseDir -Directory -Filter "__pycache__" -Recurse |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $ReleaseDir -File -Include "*.pyc", "*.pyo" -Recurse |
    Remove-Item -Force

$Launcher = Join-Path $ReleaseDir "CYAccounting.exe"
$ResourceFile = Join-Path $ProjectRoot "launcher_go/rsrc_windows_amd64.syso"
if (Test-Path $ResourceFile) { throw "Unexpected pre-existing launcher resource file" }
Push-Location $ProjectRoot
try {
    & go run github.com/akavel/rsrc@v0.10.2 -arch amd64 -ico app/resources/app.ico -o $ResourceFile
    if ($LASTEXITCODE -ne 0) { throw "Could not generate launcher icon resource" }
    & go build -trimpath -buildvcs=false -ldflags="-H windowsgui" -o $Launcher ./launcher_go
    if ($LASTEXITCODE -ne 0) { throw "Could not build launcher" }
}
finally {
    Pop-Location
    if (Test-Path $ResourceFile) { Remove-Item -LiteralPath $ResourceFile -Force }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$Archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void]$Archive.CreateEntry("CYAccounting/")
    foreach ($Directory in Get-ChildItem -LiteralPath $ReleaseDir -Directory -Recurse) {
        $Relative = [IO.Path]::GetRelativePath($StageRoot, $Directory.FullName).Replace('\', '/') + "/"
        [void]$Archive.CreateEntry($Relative)
    }
    foreach ($File in Get-ChildItem -LiteralPath $ReleaseDir -File -Recurse) {
        $Relative = [IO.Path]::GetRelativePath($StageRoot, $File.FullName).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Archive, $File.FullName, $Relative, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $Archive.Dispose() }

$Hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $ZipName" | Set-Content -LiteralPath "$ZipPath.sha256" -Encoding ascii
Write-Host "PORTABLE_ZIP=$ZipPath"
Write-Host "PORTABLE_SHA256=$Hash"
