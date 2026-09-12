param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$PackagePath = (Resolve-Path $Package).Path
$ChecksumPath = "$PackagePath.sha256"
if (!(Test-Path $ChecksumPath -PathType Leaf)) {
    throw "Package checksum file is missing: $ChecksumPath"
}
$ExpectedHash = ((Get-Content -LiteralPath $ChecksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$ActualHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedHash -ne $ActualHash) {
    throw "Package SHA-256 does not match its checksum file"
}

$VerifyRoot = Join-Path ([IO.Path]::GetTempPath()) ("CYAccountingVerify-" + [guid]::NewGuid().ToString("N"))
New-Item $VerifyRoot -ItemType Directory -Force | Out-Null
$StartedProcess = $null
try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $VerifyRoot -Force
    $Root = Join-Path $VerifyRoot "CYAccounting"
    $Required = @(
        "CYAccounting.exe",
        "runtime/pythonw.exe",
        "runtime/python.exe",
        "app/main.py",
        "app/resources/app.ico",
        "README.txt",
        ("V{0}.txt" -f $Version),
        "Data"
    )
    foreach ($Relative in $Required) {
        if (!(Test-Path (Join-Path $Root $Relative))) {
            throw "Portable package is missing: $Relative"
        }
    }

    $UnexpectedRoots = @(Get-ChildItem -LiteralPath $VerifyRoot -Force | Where-Object Name -ne "CYAccounting")
    if ($UnexpectedRoots.Count -ne 0) {
        throw "Portable ZIP must contain exactly one CYAccounting root folder"
    }
    $DataItems = @(Get-ChildItem -LiteralPath (Join-Path $Root "Data") -Force)
    if ($DataItems.Count -ne 0) {
        throw "Portable package Data folder must be empty"
    }
    $Forbidden = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
        $_.Name -eq "config.json" -or
        $_.Extension -in @(".db", ".log", ".pyc", ".pyo")
    })
    if ($Forbidden.Count -ne 0) {
        throw "Portable package contains runtime or confidential data: $($Forbidden.FullName -join ', ')"
    }
    $VersionMarkers = @(Get-ChildItem -LiteralPath $Root -File -Filter "V*.txt")
    if ($VersionMarkers.Count -ne 1 -or $VersionMarkers[0].Name -ne "V$Version.txt") {
        throw "Portable package has an incorrect version marker"
    }

    $OldQtPlatform = $env:QT_QPA_PLATFORM
    $env:QT_QPA_PLATFORM = "offscreen"
    $ReadyPath = Join-Path $Root "Data/startup_ready.flag"
    $StartedProcess = Start-Process `
        -FilePath (Join-Path $Root "runtime/pythonw.exe") `
        -ArgumentList (Join-Path $Root "app/main.py") `
        -WorkingDirectory $Root `
        -PassThru
    $Deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $Deadline -and !(Test-Path $ReadyPath)) {
        if ($StartedProcess.HasExited) {
            $LogPath = Join-Path $Root "Data/startup_trace.log"
            $LogText = if (Test-Path $LogPath) { Get-Content $LogPath -Raw } else { "no startup log" }
            throw "Portable runtime exited before ready. $LogText"
        }
        Start-Sleep -Milliseconds 200
        $StartedProcess.Refresh()
    }
    if (!(Test-Path $ReadyPath)) {
        throw "Portable runtime did not become ready within 30 seconds"
    }
    if (!(Test-Path (Join-Path $Root "Data/CYaccounting.db") -PathType Leaf)) {
        throw "Portable runtime did not create its first local accounting database"
    }
    Write-Host "Portable runtime reached the application-ready state"
}
finally {
    if ($null -ne $StartedProcess -and !$StartedProcess.HasExited) {
        Stop-Process -Id $StartedProcess.Id -Force -ErrorAction SilentlyContinue
        $StartedProcess.WaitForExit(5000) | Out-Null
    }
    if (Get-Variable OldQtPlatform -ErrorAction SilentlyContinue) {
        $env:QT_QPA_PLATFORM = $OldQtPlatform
    }
    if (Test-Path $VerifyRoot) {
        Remove-Item $VerifyRoot -Recurse -Force
    }
}

Write-Host "Portable package verification passed: $PackagePath"
