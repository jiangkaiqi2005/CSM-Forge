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

$manifest = @()
Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $manifest += ('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_.Name)
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding UTF8

$notice = @'
CSM-Forge V3 development package

This package intentionally does NOT contain Cities: Skylines, Unity, Steam, or other game-owned assemblies.
It also does not bundle Harmony's implementation DLL; install/enable CitiesHarmony separately.
It currently uses the development LiteNetLib room-key transport. This is suitable for controlled LAN/development testing, not a claim of production-authenticated Internet transport.

Windows install target:
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM-Forge

Copy the CONTENTS of this package's CSM-Forge folder so that CSM.Forge.Runtime.Cities1.dll is directly inside that directory. Enable CSM-Forge and CitiesHarmony in Content Manager, then restart the game.

Install/test only against a backed-up city. The repository acceptance gates remain authoritative; a successful build is not gameplay acceptance.
'@
$notice | Set-Content -LiteralPath (Join-Path $stage 'README-DEV.txt') -Encoding UTF8

if (-not $NoZip) {
    $zip = Join-Path $OutputPath 'CSM-Forge-runtime.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Package: $zip"
}
Write-Host "Stage: $stage"
Write-Host ("Files: " + ($copied -join ', '))
