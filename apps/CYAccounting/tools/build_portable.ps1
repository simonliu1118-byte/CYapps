param(
    [string]$Version = "1.0.26",
    [string]$PythonVersion = "3.11.9"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ExpectedMarker = Join-Path $ProjectRoot ("V{0}.txt" -f $Version)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid CYAccounting version: $Version"
}
if (!(Test-Path $ExpectedMarker -PathType Leaf)) {
    throw "Version marker is missing: $ExpectedMarker"
}

$SourceVersion = (& python -c "import sys; sys.path.insert(0, r'$($ProjectRoot.Replace("'", "''"))\app'); import util; print(util.APP_VERSION.removeprefix('V'))").Trim()
if ($LASTEXITCODE -ne 0 -or $SourceVersion -ne $Version) {
    throw "Source version $SourceVersion does not match package version $Version"
}

$BuildRoot = Join-Path $ProjectRoot "build/portable"
$DistRoot = Join-Path $ProjectRoot "dist"
$ReleaseDir = Join-Path $DistRoot "CYAccounting"
$RuntimeDir = Join-Path $ReleaseDir "runtime"
$ZipName = "CYAccounting_V${Version}_Windows.zip"
$ZipPath = Join-Path $DistRoot $ZipName

if (Test-Path $BuildRoot) {
    Remove-Item $BuildRoot -Recurse -Force
}
if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}
New-Item $BuildRoot -ItemType Directory -Force | Out-Null
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null
New-Item $RuntimeDir -ItemType Directory -Force | Out-Null
New-Item (Join-Path $ReleaseDir "Data") -ItemType Directory -Force | Out-Null

$EmbedName = "python-$PythonVersion-embed-amd64.zip"
$EmbedArchive = Join-Path $BuildRoot $EmbedName
$EmbedUrl = "https://www.python.org/ftp/python/$PythonVersion/$EmbedName"
Write-Host "Downloading portable Python $PythonVersion"
Invoke-WebRequest -Uri $EmbedUrl -OutFile $EmbedArchive
Expand-Archive -LiteralPath $EmbedArchive -DestinationPath $RuntimeDir -Force

$SitePackages = Join-Path $RuntimeDir "Lib/site-packages"
New-Item $SitePackages -ItemType Directory -Force | Out-Null
& python -m pip install `
    --disable-pip-version-check `
    --no-compile `
    --target $SitePackages `
    -r (Join-Path $ProjectRoot "requirements-runtime.txt")
if ($LASTEXITCODE -ne 0) {
    throw "Installing portable runtime dependencies failed"
}

$PthFile = Get-ChildItem -LiteralPath $RuntimeDir -Filter "python*._pth" -File | Select-Object -First 1
if ($null -eq $PthFile) {
    throw "Embedded Python path file was not found"
}
@(
    "python311.zip"
    "."
    "Lib"
    "Lib\site-packages"
    "..\app"
    "import site"
) | Set-Content -LiteralPath $PthFile.FullName -Encoding ascii

Copy-Item (Join-Path $ProjectRoot "app") $ReleaseDir -Recurse
Copy-Item (Join-Path $ProjectRoot "README.txt") $ReleaseDir
Copy-Item $ExpectedMarker $ReleaseDir

Get-ChildItem -LiteralPath $ReleaseDir -Directory -Filter "__pycache__" -Recurse |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $ReleaseDir -File -Include "*.pyc", "*.pyo" -Recurse |
    Remove-Item -Force

$Launcher = Join-Path $ReleaseDir "CYAccounting.exe"
$ResourceFile = Join-Path $ProjectRoot "launcher_go/rsrc_windows_amd64.syso"
Push-Location $ProjectRoot
try {
    & go run github.com/akavel/rsrc@v0.10.2 `
        -arch amd64 `
        -ico app/resources/app.ico `
        -o $ResourceFile
    if ($LASTEXITCODE -ne 0) {
        throw "Generating the Windows launcher icon failed"
    }
    & go build -trimpath -buildvcs=false -ldflags="-H windowsgui" -o $Launcher ./launcher_go
    if ($LASTEXITCODE -ne 0) {
        throw "Building the Windows launcher failed"
    }
}
finally {
    Pop-Location
    if (Test-Path $ResourceFile) {
        Remove-Item $ResourceFile -Force
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$Archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void]$Archive.CreateEntry("CYAccounting/")
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

$Hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $ZipName" | Set-Content -LiteralPath "$ZipPath.sha256" -Encoding ascii
Write-Host "PORTABLE_ZIP=$ZipPath"
Write-Host "PORTABLE_SHA256=$Hash"
