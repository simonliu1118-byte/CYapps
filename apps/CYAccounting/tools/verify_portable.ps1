param([Parameter(Mandatory = $true)][string]$Package)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Version = (Get-Content (Join-Path $ProjectRoot "VERSION") -Raw).Trim()
$Build = [int]((Get-Content (Join-Path $ProjectRoot "BUILD") -Raw).Trim())
$PackagePath = (Resolve-Path $Package).Path
$ChecksumPath = "$PackagePath.sha256"
if (!(Test-Path $ChecksumPath -PathType Leaf)) { throw "Missing SHA-256 checksum file" }
$ExpectedHash = ((Get-Content -LiteralPath $ChecksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$ActualHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedHash -ne $ActualHash) { throw "Portable ZIP SHA-256 does not match" }

$VerifyRoot = Join-Path ([IO.Path]::GetTempPath()) ("CYAccountingVerify-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $VerifyRoot | Out-Null
$StartedProcess = $null
$OldQtPlatform = $env:QT_QPA_PLATFORM
try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $VerifyRoot
    $Root = Join-Path $VerifyRoot "CYAccounting"
    $Required = @("CYAccounting.exe", "runtime/pythonw.exe", "runtime/python.exe",
        "app/main.py", "app/resources/app.ico", "README.txt", "VERSION", "BUILD",
        "V$Version.txt", "Data")
    foreach ($Relative in $Required) {
        if (!(Test-Path (Join-Path $Root $Relative))) { throw "Portable package is missing: $Relative" }
    }
    $OtherRoots = @(Get-ChildItem -LiteralPath $VerifyRoot -Force | Where-Object Name -ne "CYAccounting")
    if ($OtherRoots.Count -ne 0) { throw "Portable ZIP must contain only the CYAccounting root" }
    if (@(Get-ChildItem -LiteralPath (Join-Path $Root "Data") -Force).Count -ne 0) {
        throw "Portable Data must be empty before first launch"
    }
    if ((Get-Content (Join-Path $Root "VERSION") -Raw).Trim() -ne $Version -or
        [int]((Get-Content (Join-Path $Root "BUILD") -Raw).Trim()) -ne $Build) {
        throw "Portable version files do not match source"
    }
    $Forbidden = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
        $_.Name -in @("config.json", "config.json.bak", "startup_trace.log") -or
        $_.Extension -in @(".db", ".log", ".pyc", ".pyo")
    })
    if ($Forbidden.Count -ne 0) { throw "Portable ZIP contains runtime or user data" }

    # Probe the bundled interpreter independently from the checkout's Python.
    & (Join-Path $Root "runtime/python.exe") -c "import PySide6.QtWidgets, openpyxl; from util import APP_VERSION; print(APP_VERSION)"
    if ($LASTEXITCODE -ne 0) { throw "Embedded Python failed dependency/version import" }
    $env:QT_QPA_PLATFORM = "offscreen"
    $ReadyPath = Join-Path $Root "Data/startup_ready.flag"
    $StartedProcess = Start-Process -FilePath (Join-Path $Root "runtime/pythonw.exe") `
        -ArgumentList (Join-Path $Root "app/main.py") -WorkingDirectory $Root -PassThru
    $Deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $Deadline -and !(Test-Path $ReadyPath)) {
        $StartedProcess.Refresh()
        if ($StartedProcess.HasExited) {
            $LogPath = Join-Path $Root "Data/startup_trace.log"
            $LogText = if (Test-Path $LogPath) { Get-Content $LogPath -Raw } else { "no startup log" }
            throw "Embedded application exited before ready. $LogText"
        }
        Start-Sleep -Milliseconds 200
    }
    if (!(Test-Path $ReadyPath)) { throw "Embedded application did not become ready within 30 seconds" }
    if (!(Test-Path (Join-Path $Root "Data/CYaccounting.db") -PathType Leaf)) {
        throw "Embedded application did not initialize its test database"
    }
    Write-Host "Portable startup and clean-package verification passed"
}
finally {
    if ($null -ne $StartedProcess -and !$StartedProcess.HasExited) {
        Stop-Process -Id $StartedProcess.Id -Force -ErrorAction SilentlyContinue
        $StartedProcess.WaitForExit(5000) | Out-Null
    }
    $env:QT_QPA_PLATFORM = $OldQtPlatform
    if (Test-Path $VerifyRoot) { Remove-Item -LiteralPath $VerifyRoot -Recurse -Force }
}
