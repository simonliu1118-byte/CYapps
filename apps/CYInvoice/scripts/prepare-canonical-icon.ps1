param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

# AITeam is the canonical owner of the CY App Icon Family. Pin both the
# source commit and the production-asset digest so a downstream build can
# never silently consume a changed icon.
$sourceCommit = '13ddf9b710ea6056ebde1f9d6c0242477a040bbe'
$sourceUrl = "https://raw.githubusercontent.com/simonliu1118-byte/AITeam/$sourceCommit/shared/cy-visual/icon-family/apps/invoice/INV.ico"
$expectedBytes = 17241
$expectedSha256 = '6f4f89a1611e2b731d489c76851fb06fa1b75aba2edc9f1d70d9a22ed6e77d6d'

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $bytes = $sha.ComputeHash($stream)
            return ([System.BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-CanonicalIcon([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $expectedBytes) { return $false }
    return (Get-Sha256 $Path) -eq $expectedSha256
}

if (Test-CanonicalIcon $OutputPath) {
    Write-Host "CYInvoice icon already matches AITeam canonical INV.ico."
    exit 0
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$temp = "$OutputPath.download"
try {
    Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -OutFile $temp
    if (-not (Test-CanonicalIcon $temp)) {
        $actualBytes = (Get-Item -LiteralPath $temp).Length
        $actualSha = Get-Sha256 $temp
        throw "AITeam INV.ico integrity mismatch. bytes=$actualBytes sha256=$actualSha"
    }
    Move-Item -Force -LiteralPath $temp -Destination $OutputPath
    Write-Host "Materialized canonical AITeam CYInvoice icon: $expectedSha256"
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -Force -LiteralPath $temp }
}
