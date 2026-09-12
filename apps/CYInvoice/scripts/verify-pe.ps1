param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Path))
if ($Bytes.Length -lt 512) {
    throw "PE file is too small."
}
if ($Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) {
    throw "Missing MZ signature."
}

$PEOffset = [BitConverter]::ToInt32($Bytes, 0x3C)
if ($PEOffset -lt 0 -or ($PEOffset + 256) -gt $Bytes.Length) {
    throw "Invalid PE header offset."
}
if ($Bytes[$PEOffset] -ne 0x50 -or $Bytes[$PEOffset + 1] -ne 0x45 -or
    $Bytes[$PEOffset + 2] -ne 0 -or $Bytes[$PEOffset + 3] -ne 0) {
    throw "Missing PE signature."
}

$Machine = [BitConverter]::ToUInt16($Bytes, $PEOffset + 4)
$OptionalHeader = $PEOffset + 24
$Magic = [BitConverter]::ToUInt16($Bytes, $OptionalHeader)
$FileAlignment = [BitConverter]::ToUInt32($Bytes, $OptionalHeader + 36)
$SizeOfHeaders = [BitConverter]::ToUInt32($Bytes, $OptionalHeader + 60)
$Subsystem = [BitConverter]::ToUInt16($Bytes, $OptionalHeader + 68)
$ResourceRVA = [BitConverter]::ToUInt32($Bytes, $OptionalHeader + 128)
$ResourceSize = [BitConverter]::ToUInt32($Bytes, $OptionalHeader + 132)

if ($Machine -ne 0x8664) { throw ("Expected AMD64 machine, got 0x{0:X4}." -f $Machine) }
if ($Magic -ne 0x020B) { throw ("Expected PE32+, got 0x{0:X4}." -f $Magic) }
if ($Subsystem -ne 2) { throw "Expected Windows GUI subsystem."
}
if ($FileAlignment -lt 0x200) { throw "Invalid PE file alignment."
}
if ($SizeOfHeaders -eq 0 -or ($SizeOfHeaders % $FileAlignment) -ne 0) {
    throw "Invalid SizeOfHeaders alignment."
}
if ($ResourceRVA -eq 0 -or $ResourceSize -eq 0) {
    throw "Embedded icon/manifest resource directory was not found."
}

Write-Host ("PE verified: AMD64, GUI, headers=0x{0:X}, resource RVA=0x{1:X}, size=0x{2:X}" -f `
    $SizeOfHeaders, $ResourceRVA, $ResourceSize)

