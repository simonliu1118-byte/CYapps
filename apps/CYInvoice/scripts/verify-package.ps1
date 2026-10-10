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
    $EntryLengths = @{}
    foreach ($Entry in $Archive.Entries) {
        $NormalizedName = $Entry.FullName -replace '\\', '/'
        $EntryLengths[$NormalizedName] = $Entry.Length
    }
    foreach ($Identity in @{ VERSION = $Version; BUILD = [string]$Build }.GetEnumerator()) {
        $IdentityEntry = $Archive.GetEntry("CYInvoice/$($Identity.Key)")
        if ($null -eq $IdentityEntry) { throw "Package ZIP is missing $($Identity.Key)." }
        $IdentityReader = [System.IO.StreamReader]::new($IdentityEntry.Open())
        try { $IdentityText = $IdentityReader.ReadToEnd().Trim() }
        finally { $IdentityReader.Dispose() }
        if ($IdentityText -cne $Identity.Value) { throw "Package $($Identity.Key) does not match the build identity." }
    }
    $VersionEntry = $Archive.GetEntry("CYInvoice/$ArtifactVersion.txt")
    if ($null -eq $VersionEntry) { throw "Package ZIP is missing the version identity file." }
    $Reader = [System.IO.StreamReader]::new($VersionEntry.Open())
    try { $VersionText = $Reader.ReadToEnd() }
    finally { $Reader.Dispose() }
    $ManifestEntry = $Archive.GetEntry("CYInvoice/package-files.json")
    if ($null -eq $ManifestEntry) { throw "Package ZIP is missing the original-file manifest." }
    $ManifestReader = [System.IO.StreamReader]::new($ManifestEntry.Open())
    try { $Manifest = @($ManifestReader.ReadToEnd() | ConvertFrom-Json) }
    finally { $ManifestReader.Dispose() }
    if ($Manifest.Count -lt 1 -or $Manifest.Count -gt 4096 -or $Manifest -notcontains "CYInvoice.exe") {
        throw "Package manifest is invalid."
    }
    foreach ($File in $Manifest) {
        if ($File -isnot [string] -or $File -match '(^/|\\|:|(^|/)\.{1,2}(/|$)|^(Data|Cache|Logs)(/|$))') {
            throw "Package manifest contains a runtime or unsafe path."
        }
    }
    $PackagedFiles = @($Archive.Entries | Where-Object {
        $_.Length -ge 0 -and -not $_.FullName.EndsWith('/') -and $_.FullName -ne "CYInvoice/package-files.json"
    } | ForEach-Object { ($_.FullName -replace '\\', '/') -replace '^CYInvoice/', '' })
    if (@(Compare-Object ($Manifest | Sort-Object -Unique) ($PackagedFiles | Sort-Object -Unique)).Count -ne 0) {
        throw "Package manifest does not match the original package files."
    }
}
finally {
    $Archive.Dispose()
}

$RequiredEntries = @(
    "CYInvoice/CYInvoice.exe",
    "CYInvoice/VERSION",
    "CYInvoice/BUILD",
    "CYInvoice/package-files.json",
    "CYInvoice/$ArtifactVersion.txt",
    "CYInvoice/使用說明.txt",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Core.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.WinForms.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Wpf.dll"
)
foreach ($Required in $RequiredEntries) {
    if ($Entries -notcontains $Required) {
        throw "Package ZIP is missing: $Required"
    }
}
foreach ($RequiredAssembly in @(
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Core.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.WinForms.dll",
    "CYInvoice/Runtime/WebView2/Microsoft.Web.WebView2.Wpf.dll")) {
    if ($EntryLengths[$RequiredAssembly] -le 0) {
        throw "Package ZIP contains an empty required assembly: $RequiredAssembly"
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
