$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Version = (Get-Content "$Root\VERSION" -Raw).Trim()
$Build = [int](Get-Content "$Root\BUILD" -Raw).Trim()
$DisplayVersion = if ($Build -gt 0) { "$Version Build $Build" } else { $Version }

# The approved seven-layer ICO lives in AITeam, not in a second app-specific icon design.
# Pin the exact accepted source revision and verify its manifest SHA-256 on every build.
$CanonicalRevision = 'b24d8c22c445ee54dc1b532dfd93292764316eba'
$CanonicalIconSha256 = '0cba7fcb2946e10c9499d7df9951d645045ff773f12862ede9ac330ae4acae56'
$CanonicalIconUrl = "https://raw.githubusercontent.com/simonliu1118-byte/AITeam/$CanonicalRevision/shared/cy-visual/icon-family/apps/converter/CVT.ico"
$TempIcon = Join-Path $env:TEMP "SMARTCOPIConverter-CVT.ico"
$Syso = Join-Path $Root "rsrc_windows_amd64.syso"
$Exe = Join-Path $Root "SMARTCOPIConverter.exe"

try {
    Invoke-WebRequest -Uri $CanonicalIconUrl -OutFile $TempIcon -ErrorAction Stop
    $ActualIconSha256 = (Get-FileHash -Path $TempIcon -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ActualIconSha256 -ne $CanonicalIconSha256) {
        throw "CVT icon SHA-256 mismatch; refusing to build with an unapproved icon."
    }

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
