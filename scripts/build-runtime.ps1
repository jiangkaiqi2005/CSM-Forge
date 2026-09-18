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
    '-m:1' `
    '-p:UseSharedCompilation=false' `
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

$startupProbe = Join-Path $repo 'tools/Forge.RuntimeStartupProbe/Forge.RuntimeStartupProbe.csproj'
& dotnet run --project $startupProbe -c Release -- $runtimeDll $managed
if ($LASTEXITCODE -ne 0) {
    throw "Runtime startup probe failed with exit code $LASTEXITCODE"
}

$notice = @'
CSM-Forge V3 minimum-playable Alpha / Ultimate Framework development candidate

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

Minimum-playable Alpha scope retained:
- supported: host/join snapshot flow, roads/networks, buildings, zoning, districts and policies, tax/budgets/cash/loans, area unlock, pause/speed, transport lines, stable names/city name, demand/weather authority, and Tree/Prop create/move/delete through Forge Stable IDs;
- recovery: journal catch-up, fixed replay barrier, activation grant, snapshot rebaseline for one lagging client;
- diagnostic-only projection audit logs projection-drift/local drift without automatically kicking/resyncing a client;
- Tree/Prop and Terrain absolute height shards are code-complete for their authority batches but still require real multi-machine gameplay validation; Terrain remains one of the Host-only tools during multiplayer;
- still outside this framework milestone: real multi-machine Citizen/Vehicle/Path/Terrain validation, production-authenticated Internet transport, and broad real-world Mod/DLC soak certification.

Ultimate Framework code coverage in this candidate:
- official gameplay DLC are classified to core authority or dedicated absolute-state adapters; see docs/DLC-COVERAGE-V1.zh-CN.md in the repository;
- Parklife / Industries / Campus / Airports / pedestrian-area systems use stable DistrictPark identities, sharded park-grid state, controls, Campus deep state, and conservative deep value-state projection;
- Match Day / Concerts use Event Stable IDs, Building Stable-ID references, local Event slot materialization and Host absolute lifecycle/result projection;
- Natural Disasters use Disaster Stable IDs and local Disaster slot materialization; Client persistent disaster creation/release/random-start paths are fail-closed;
- third-party Mods can declare ExactMatch / ClientOnly / ForgeSynchronized / Blocked and can register absolute/sharded/interactive Forge adapters;
- ForgeSynchronized declarations without a state adapter fail closed; unknown simulation-changing Mods remain exact-match by default.

First two-machine test:
1. Back up the Host city and install the exact same ZIP + CitiesHarmony on both machines.
2. Run VERIFY-ALPHA-INSTALL.ps1 on both machines and compare source_commit + manifest_sha256.
3. Host enters the city to share, presses Esc, then chooses FORGE 多人联机 -> 创建房间（当前城市作为房主）.
4. Host opens CSM-Forge multiplayer from the pause menu and chooses 邀请 Steam 好友. Forge publishes a Steam Rich Presence join command and also copies the direct-connect invitation text. A friend may use Steam's Join Game action, or receive and paste the copied invitation manually. This is direct UDP discovery only and does not provide NAT traversal or relay.
5. Client stays at the main menu. A Steam Join Game request fills the Forge join panel and starts joining automatically; alternatively choose FORGE 联机, paste the invitation text, then choose 加入房间. Forge downloads and loads the Host snapshot automatically; the Client must not load a placeholder city first.
6. Wait until Client status is ClientLive before editing.
7. Test pause/speed, one road, one building, zoning, district brush/policy, tax/budget, area unlock and one transport line.
8. Then test installed DLC in small isolated steps: park/campus/industry/airport area edits, Event controls/results, and a Host-started disaster.
9. Test Host and Client Tree/Prop create, move and delete in small isolated steps; record any identity, slot-reuse or projection failure. These paths are not yet gameplay-validated by CI.
10. On the Host only, test a small Terrain brush and undo, then verify Terrain absolute height shards and Net/Building collateral on every Client. Client Terrain tools remain blocked. These paths are not yet gameplay-validated by CI.
11. Test a second client join/rejoin while the first client and Host remain live, then repeat a Tree/Prop edit after hot join.
12. Open 玩家列表 and 多人聊天, press T to open chat, and confirm remote player tool cursors/names are visible during edits.
13. On any failure or projection warning, click 写入诊断日志 in CSM-Forge settings before leaving the city, then run COLLECT-ALPHA-DIAGNOSTICS.ps1 on every involved machine.

A successful build proves compilation/package integrity and source-level authority contracts, not multi-hour gameplay acceptance. Steam click-to-join, player roster/chat/tool cursors, and all two-machine behavior remain real-gameplay unverified until the E4/RC multiplayer gates are run. BUILD_INFO.json intentionally says gameplay_validation=NOT RUN BY CI. Keep using backed-up saves until those gates pass.
'@
$notice | Set-Content -LiteralPath (Join-Path $stage 'README-DEV.txt') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')

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
