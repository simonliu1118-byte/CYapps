param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [int]$Build = 0
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Build -lt 0) {
    throw "CYInvoice Build cannot be negative: $Build"
}
$ArtifactVersion = "V$Version"
if ($Build -gt 0) {
    $ArtifactVersion = "V${Version}_Build${Build}"
}

$ResolvedPath = Resolve-Path $Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Archive = [System.IO.Compression.ZipFile]::OpenRead($ResolvedPath.Path)
try {
    $Entries = @($Archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
}
finally {
    $Archive.Dispose()
}

$RequiredEntries = @(
    "CYInvoice/CYInvoice.exe",
    "CYInvoice/VERSION",
    "CYInvoice/$ArtifactVersion.txt",
    "CYInvoice/使用說明.txt",
    "CYInvoice/Data/",
    "CYInvoice/Cache/",
    "CYInvoice/Cache/InvoicePDF/",
    "CYInvoice/Logs/"
)
foreach ($Required in $RequiredEntries) {
    if ($Entries -notcontains $Required) {
        throw "Release ZIP is missing: $Required"
    }
}

if ($Entries | Where-Object { $_ -match '^CYInvoice/Version/' }) {
    throw "Release ZIP must not contain a Version directory."
}
if ($Entries | Where-Object { $_ -match '(^|/)todo\.txt$' }) {
    throw "Release ZIP must not contain todo.txt."
}
$VersionFiles = @($Entries | Where-Object { $_ -match '^CYInvoice/V[^/]+\.txt$' })
if ($VersionFiles.Count -ne 1) {
    throw "Release ZIP must contain exactly one root-level version TXT file."
}

Write-Host "Package layout verified for CYInvoice $ArtifactVersion."
