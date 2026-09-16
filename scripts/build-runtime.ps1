param(
    [Parameter(Mandatory=$true)][string]$CitiesManagedPath,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputPath = '',
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$managed = [IO.Path]::GetFullPath($CitiesManagedPath)
$required = @('ICities.dll','Assembly-CSharp.dll','ColossalManaged.dll','UnityEngine.dll')
foreach ($name in $required) {
    $path = Join-Path $managed $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required Cities: Skylines assembly: $path"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repo 'dist/runtime'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$stage = Join-Path $OutputPath 'CSM-Forge'
if (Test-Path -LiteralPath $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$project = Join-Path $repo 'src/Forge.Runtime.Cities1/Forge.Runtime.Cities1.csproj'
& dotnet build $project -c $Configuration --nologo `
    "-p:CitiesManagedPath=$managed" `
    "-p:ContinuousIntegrationBuild=true"
if ($LASTEXITCODE -ne 0) { throw "Runtime build failed with exit code $LASTEXITCODE" }

$bin = Join-Path $repo "src/Forge.Runtime.Cities1/bin/$Configuration/net35"
if (-not (Test-Path -LiteralPath $bin -PathType Container)) {
    throw "Expected runtime output directory was not produced: $bin"
}

$forbidden = @(
    'ICities.dll',
    'Assembly-CSharp.dll',
    'ColossalManaged.dll',
    'UnityEngine.dll',
    'UnityEngine.UI.dll',
    '0Harmony.dll',
    'CitiesHarmony.Harmony.dll',
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll'
)
$copied = @()
Get-ChildItem -LiteralPath $bin -File | Where-Object { $_.Extension -in @('.dll','.pdb') } | ForEach-Object {
    if ($forbidden -contains $_.Name) {
        return
    }
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $_.Name)
    $copied += $_.Name
}

$runtimeDll = Join-Path $stage 'CSM.Forge.Runtime.Cities1.dll'
if (-not (Test-Path -LiteralPath $runtimeDll -PathType Leaf)) {
    throw 'CSM.Forge.Runtime.Cities1.dll is missing from the staged package.'
}

$requiredRuntime = @(
    'CSM.Forge.Runtime.Cities1.dll',
    'CSM.Forge.Core.dll',
    'CSM.Forge.Protocol.dll',
    'CSM.Forge.Transport.LiteNet.dll',
    'CSM.Forge.Checkpoints.dll',
    'LiteNetLib.dll',
    'CitiesHarmony.API.dll'
)
foreach ($name in $requiredRuntime) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) {
        throw "Required runtime dependency is missing from staged package: $name"
    }
}

foreach ($name in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $stage $name)) {
        throw "Forbidden runtime binary leaked into staged package: $name"
    }
}

$notice = @'
CSM-Forge V3 minimum-playable Alpha

This package intentionally does NOT contain Cities: Skylines, Unity, Steam, or other game-owned assemblies.
It also does not bundle Harmony's implementation DLL; install/enable CitiesHarmony separately.
It currently uses the development LiteNetLib room-key transport. Use it for controlled LAN/development testing, not as a production-authenticated Internet transport.

Windows install target:
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM-Forge

Copy the CONTENTS of this package's CSM-Forge folder so that CSM.Forge.Runtime.Cities1.dll is directly inside that directory. Enable CSM-Forge and CitiesHarmony in Content Manager, then restart the game.

Before testing on each machine:
1. Run VERIFY-ALPHA-INSTALL.ps1 from the installed CSM-Forge directory.
2. Confirm it reports PASS.
3. Confirm source_commit and manifest_sha256 are identical on every Host/Client machine.

Minimum-playable Alpha scope:
- supported: host/join snapshot flow, roads/networks, buildings, zoning, districts and policies, tax/budgets/cash/loans, area unlock, pause/speed, transport lines, stable names/city name, demand and weather authority;
- recovery: journal catch-up, fixed replay barrier, activation grant, snapshot rebaseline for one lagging client;
- diagnostic-only projection audit logs projection-drift/local drift without automatically kicking/resyncing a client;
- temporarily blocked in multiplayer for safety: direct Tree/Prop create/move/delete and Terrain brush writes;
- not yet claimed complete: Terrain authority, Tree/Prop authority, Event/Campus/DLC-specific systems, full Citizen/Vehicle/Path authority, production-authenticated Internet transport.

First two-machine test:
1. Back up the Host city and install the exact same Alpha ZIP + CitiesHarmony on both machines.
2. Run VERIFY-ALPHA-INSTALL.ps1 on both machines and compare source_commit + manifest_sha256.
3. Enter a city on both machines. Host opens CSM-Forge settings and chooses Host current save.
4. Client enters Host IPv4, same UDP port and temporary room key, then chooses Join Host snapshot.
5. Wait until Client status is ClientLive before editing.
6. Test pause/speed, one road, one building, zoning, district brush/policy, tax/budget, area unlock and one transport line.
7. Do NOT use Tree/Prop/Terrain tools in this Alpha; they are fail-closed intentionally.
8. Test a second client join/rejoin while the first client and Host remain live.
9. On any failure or projection warning, click 写入诊断日志 in CSM-Forge settings before leaving the city.
10. Run COLLECT-ALPHA-DIAGNOSTICS.ps1 on every involved machine and keep the generated ZIPs together with the action that immediately preceded the failure.

A successful build proves compilation/package integrity, not multi-hour gameplay acceptance. Keep using backed-up saves until the E4 multiplayer gates pass.
'@
$notice | Set-Content -LiteralPath (Join-Path $stage 'README-DEV.txt') -Encoding UTF8

$packagedTools = @(
    @('scripts/verify-alpha-install.ps1', 'VERIFY-ALPHA-INSTALL.ps1'),
    @('scripts/collect-alpha-diagnostics.ps1', 'COLLECT-ALPHA-DIAGNOSTICS.ps1')
)
foreach ($tool in $packagedTools) {
    $source = Join-Path $repo $tool[0]
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required Alpha support script is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $tool[1])
}

foreach ($scriptName in @('VERIFY-ALPHA-INSTALL.ps1','COLLECT-ALPHA-DIAGNOSTICS.ps1')) {
    $scriptPath = Join-Path $stage $scriptName
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) {
        $messages = ($parseErrors | ForEach-Object { $_.Message }) -join '; '
        throw "Packaged Alpha support script does not parse: $scriptName :: $messages"
    }
}

$manifest = @()
Get-ChildItem -LiteralPath $stage -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $manifest += ('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_.Name)
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii

if (-not $NoZip) {
    $zip = Join-Path $OutputPath 'CSM-Forge-runtime.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Package: $zip"
}
Write-Host "Stage: $stage"
Write-Host ("Files: " + ($copied -join ', '))
