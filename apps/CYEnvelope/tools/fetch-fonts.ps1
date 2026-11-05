# Downloads the bundled fonts listed in fonts.json into src/CYEnvelope/Fonts and verifies size and SHA-256.
# Safe to run repeatedly: files that already match are kept. Works in Windows PowerShell 5.1 and PowerShell 7.
$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Get-Content (Join-Path $toolsDir 'fonts.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$target = Join-Path $toolsDir '../src/CYEnvelope/Fonts'
New-Item -ItemType Directory -Path $target -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Test-Font($path, $font) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne $font.bytes) { return $false }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $font.sha256
}

foreach ($font in $manifest.fonts) {
    $path = Join-Path $target $font.file
    if (Test-Font $path $font) { Write-Host "OK      $($font.file)"; continue }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    $done = $false
    foreach ($attempt in 1..3) {
        try {
            Write-Host "Fetch   $($font.file) (attempt $attempt)"
            Invoke-WebRequest -Uri $font.url -OutFile $path -UseBasicParsing
            if (Test-Font $path $font) { $done = $true; break }
            Remove-Item -LiteralPath $path -Force
            throw "Downloaded file does not match the pinned size/SHA-256: $($font.file)"
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    if (-not $done) { throw "Could not fetch $($font.file)" }
}
Write-Host 'Fonts ready.'
