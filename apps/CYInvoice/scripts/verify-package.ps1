param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [int]$Build = 0,

    [ValidateSet("formal", "engineering")]
    [string]$Channel = "formal"
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
    $VersionEntry = $Archive.GetEntry("CYInvoice/$ArtifactVersion.txt")
    if ($null -eq $VersionEntry) { throw "Package ZIP is missing the version identity file." }
    $Reader = [System.IO.StreamReader]::new($VersionEntry.Open())
    try { $VersionText = $Reader.ReadToEnd() }
    finally { $Reader.Dispose() }
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
    "CYInvoice/Cache/InvoicePreview/",
    "CYInvoice/Runtime/",
    "CYInvoice/Runtime/WebView2/",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Core.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.WinForms.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Wpf.dll",
    "CYInvoice/Logs/"
)
foreach ($Required in $RequiredEntries) {
    if ($Entries -notcontains $Required) {
        throw "Package ZIP is missing: $Required"
    }
}

if ($Entries | Where-Object { $_ -match '^CYInvoice/Version/' }) {
    throw "Package ZIP must not contain a Version directory."
}
if ($Entries | Where-Object { $_ -match '(^|/)todo\.txt$' }) {
    throw "Package ZIP must not contain todo.txt."
}
if ($Entries | Where-Object { $_ -match '^CYInvoice/Microsoft\.Web\.WebView2\..*\.xml$' }) {
    throw "Package ZIP must not contain WebView2 API documentation XML files."
}
if ($Entries | Where-Object { $_ -match '^CYInvoice/Microsoft\.Web\.WebView2\..*\.dll$' }) {
    throw "Package ZIP must keep WebView2 managed assemblies under Runtime/WebView2."
}
$VersionFiles = @($Entries | Where-Object { $_ -match '^CYInvoice/V[^/]+\.txt$' })
if ($VersionFiles.Count -ne 1) {
    throw "Package ZIP must contain exactly one root-level version TXT file."
}
if ($Channel -eq "engineering" -and $VersionText -notmatch '(?m)^Channel: engineering\r?$') {
    throw "Engineering ZIP must contain engineering channel metadata."
}
if ($Channel -eq "formal" -and $VersionText -notmatch '(?m)^Channel: formal\r?$') {
    throw "Formal ZIP must contain formal channel metadata."
}

Write-Host "Package layout and $Channel channel verified for CYInvoice $ArtifactVersion."
