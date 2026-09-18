param(
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
    throw "CSM-Forge install directory does not exist: $rootPath"
}

$required = @(
    'CSM.Forge.Runtime.Cities1.dll',
    'CSM.Forge.Core.dll',
    'CSM.Forge.Protocol.dll',
    'CSM.Forge.Transport.LiteNet.dll',
    'CSM.Forge.Checkpoints.dll',
    'LiteNetLib.dll',
    'CitiesHarmony.API.dll',
    'SHA256SUMS.txt'
)
foreach ($name in $required) {
    $path = Join-Path $rootPath $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required candidate file is missing: $path"
    }
}

$forbidden = @(
    'ICities.dll', 'Assembly-CSharp.dll', 'ColossalManaged.dll', 'UnityEngine.dll',
    'UnityEngine.UI.dll', '0Harmony.dll', 'CitiesHarmony.Harmony.dll',
    'mscorlib.dll', 'System.dll', 'System.Core.dll'
)
foreach ($name in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $rootPath $name) -PathType Leaf) {
        throw "Forbidden game/runtime binary is present in the CSM-Forge folder: $name"
    }
}

$manifestPath = Join-Path $rootPath 'SHA256SUMS.txt'
$entries = @(Get-Content -LiteralPath $manifestPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($entries.Count -eq 0) { throw 'SHA256SUMS.txt is empty.' }

$checked = 0
foreach ($line in $entries) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') {
        throw "Invalid SHA256SUMS.txt line: $line"
    }
    $expected = $Matches[1].ToLowerInvariant()
    $relative = $Matches[2]
    if ($relative -eq 'SHA256SUMS.txt') { throw 'SHA256SUMS.txt must not hash itself.' }
    $path = Join-Path $rootPath $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Manifest file is missing: $relative"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "SHA256 mismatch for $relative. expected=$expected actual=$actual"
    }
    $checked++
}

$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$buildInfoPath = Join-Path $rootPath 'BUILD_INFO.json'
$sourceCommit = 'unknown'
$sourceRef = 'unknown'
if (Test-Path -LiteralPath $buildInfoPath -PathType Leaf) {
    $info = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json
    if ($info.source_commit -and $info.source_commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "BUILD_INFO.json contains an invalid source_commit: $($info.source_commit)"
    }
    if ($info.source_commit) { $sourceCommit = [string]$info.source_commit }
    if ($info.source_ref) { $sourceRef = [string]$info.source_ref }
}

Write-Host 'CSM-Forge candidate install verification: PASS'
Write-Host "root=$rootPath"
Write-Host "checked_files=$checked"
Write-Host "manifest_sha256=$manifestHash"
Write-Host "source_commit=$sourceCommit"
Write-Host "source_ref=$sourceRef"
Write-Host 'Run this on every machine. source_commit and manifest_sha256 must match before Host/Join testing.'
