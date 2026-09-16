param(
    [string]$Root = $PSScriptRoot,
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $env:TEMP ("CSM-Forge-Diagnostics-" + $stamp)
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Copy-OpenFile([string]$Source, [string]$Destination) {
    $sourceStream = $null
    $destinationStream = $null
    try {
        $sourceStream = New-Object IO.FileStream($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $destinationStream = New-Object IO.FileStream($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        if ($destinationStream -ne $null) { $destinationStream.Dispose() }
        if ($sourceStream -ne $null) { $sourceStream.Dispose() }
    }
}

$metadataFiles = @('BUILD_INFO.json','SHA256SUMS.txt','README-DEV.txt')
foreach ($name in $metadataFiles) {
    $source = Join-Path $rootPath $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $output $name)
    }
}

$summary = @()
$summary += 'CSM-Forge Alpha diagnostics'
$summary += ('collected_utc=' + [DateTime]::UtcNow.ToString('o'))
$summary += ('machine=' + $env:COMPUTERNAME)
$summary += ('os=' + [Environment]::OSVersion.VersionString)
$summary += ('powershell=' + $PSVersionTable.PSVersion.ToString())
$summary += ('install_root=' + $rootPath)
foreach ($name in @('CSM.Forge.Runtime.Cities1.dll','CSM.Forge.Core.dll','CSM.Forge.Protocol.dll','CSM.Forge.Transport.LiteNet.dll')) {
    $path = Join-Path $rootPath $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $summary += ($name + '_sha256=' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
    }
}
$summary | Set-Content -LiteralPath (Join-Path $output 'machine-summary.txt') -Encoding UTF8

$logCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Colossal Order\Cities_Skylines\output_log.txt'),
    (Join-Path $env:LOCALAPPDATA 'Colossal Order\Cities_Skylines\Player.log'),
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities_Skylines\Player.log')
)
$copiedLogs = @()
foreach ($candidate in $logCandidates) {
    if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $name = [IO.Path]::GetFileName($candidate)
    $destination = Join-Path $output $name
    if (Test-Path -LiteralPath $destination) {
        $destination = Join-Path $output (([IO.Path]::GetFileNameWithoutExtension($name)) + '-alt' + [IO.Path]::GetExtension($name))
    }
    Copy-OpenFile $candidate $destination
    $copiedLogs += $destination
}

$forgeLines = @()
foreach ($log in $copiedLogs) {
    try {
        $forgeLines += @(Select-String -LiteralPath $log -SimpleMatch '[CSM-Forge]' | ForEach-Object { $_.Line })
    }
    catch { }
}
if ($forgeLines.Count -gt 0) {
    $forgeLines | Set-Content -LiteralPath (Join-Path $output 'CSM-Forge-lines.txt') -Encoding UTF8
}
else {
    'No [CSM-Forge] lines were found in the discovered game logs. Use the in-game "写入诊断日志" button before collecting again.' |
        Set-Content -LiteralPath (Join-Path $output 'CSM-Forge-lines.txt') -Encoding UTF8
}

$zipPath = $output + '.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Diagnostics ZIP: $zipPath"
Write-Host 'The collector intentionally does not record the Forge room key or network configuration.'
