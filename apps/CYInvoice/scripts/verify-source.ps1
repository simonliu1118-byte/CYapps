$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$RepositoryRoot = (& git -C $ProjectRoot rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    throw "Unable to locate the Git repository root."
}

$TrackedFiles = @(& git -C $RepositoryRoot ls-files -- "apps/CYInvoice")
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect tracked CYInvoice files."
}

$ForbiddenPathPatterns = @(
    '^apps/CYInvoice/(?:Data|Cache|Logs|dist|build|out)/',
    '(?:^|/)(?:settings|invoices|buyer_names)\.json$',
    '\.(?:exe|dll|zip|7z|msi|xls|xlsx|pdf|log)$'
)
foreach ($RelativePath in $TrackedFiles) {
    foreach ($Pattern in $ForbiddenPathPatterns) {
        if ($RelativePath -match $Pattern) {
            throw "Confidential runtime data or a binary artifact is tracked: $RelativePath"
        }
    }
}

$PrivateKeyPattern = '-----BEGIN ' + '(?:RSA |EC |OPENSSH )?' + 'PRIVATE KEY-----'
$LegacyPasswordBridge = 'CYINVOICE_XLS_' + 'PASSWORD'
$LiteralSecretPattern = @'
(?i)\b(?:AppKey|app_key|ProdAppKey|ProdAppKeyEnc|MOPassword|MOPasswordEnc)\w*\s*(?::=|=|:)\s*["'](?<value>[^"']+)["']
'@

$TextExtensions = @('.cs', '.csproj', '.sln', '.props', '.md', '.ps1', '.yml', '.yaml', '.json', '.xml', '.manifest', '.txt')
foreach ($RelativePath in $TrackedFiles) {
    $Extension = [IO.Path]::GetExtension($RelativePath).ToLowerInvariant()
    if ($TextExtensions -notcontains $Extension) {
        continue
    }
    $FullPath = Join-Path $RepositoryRoot $RelativePath
    $Content = Get-Content -LiteralPath $FullPath -Raw
    if ($Content -match $PrivateKeyPattern) {
        throw "A private key marker is present in: $RelativePath"
    }
    if ($Content -match $LegacyPasswordBridge) {
        throw "The legacy plaintext Excel password bridge is present in: $RelativePath"
    }
    foreach ($Match in [regex]::Matches($Content, $LiteralSecretPattern)) {
        $Value = $Match.Groups['value'].Value
        $IsPublishedAmegoTestKey = $RelativePath -eq 'apps/CYInvoice/src/CYInvoice.Core/AmegoContracts.cs' -and $Value -eq 'sHeq7t8G1wiQvhAuIM27'
        if (!$IsPublishedAmegoTestKey -and $Value -notmatch '^TEST[-_]' -and $Value -ne 'secret') {
            throw "A possible literal App Key or MO password is present in: $RelativePath"
        }
    }
}

Write-Host "CYInvoice tracked-source confidentiality checks passed."
