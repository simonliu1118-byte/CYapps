$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Version = (Get-Content "$Root\VERSION" -Raw).Trim()
$Build = [int](Get-Content "$Root\BUILD" -Raw).Trim()
$DisplayVersion = if ($Build -gt 0) { "$Version Build $Build" } else { $Version }

$TempIcon = Join-Path $env:TEMP "SMARTCOPIConverter.ico"
$Syso = Join-Path $Root "rsrc_windows_amd64.syso"
$Exe = Join-Path $Root "SMARTCOPIConverter.exe"

try {
    [IO.File]::WriteAllBytes($TempIcon, [Convert]::FromBase64String((Get-Content "$Root\assets\app.ico.b64" -Raw)))

    $Rsrc = Join-Path (go env GOPATH) "bin\rsrc.exe"
    if (-not (Test-Path $Rsrc)) {
        go install github.com/akavel/rsrc@v0.10.2
    }
    & $Rsrc -ico $TempIcon -o $Syso

    go test ./...
    $env:GOOS = 'windows'
    $env:GOARCH = 'amd64'
    go build -trimpath -ldflags "-H windowsgui -X 'main.version=$DisplayVersion'" -o $Exe .
    Write-Host "Built: $Exe"
}
finally {
    Remove-Item $TempIcon -ErrorAction SilentlyContinue
    Remove-Item $Syso -ErrorAction SilentlyContinue
}
