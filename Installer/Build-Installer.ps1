param(
    [string]$PayloadDirectory,
    [string]$OutputDirectory,
    [string]$Version = '0.1.0-alpha.6',
    [switch]$AddInOnly,
    [string]$CompilerPath = $env:INNO_SETUP_COMPILER
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?$') { throw 'Invalid release version' }
$taskBinaryVersion = $Matches[1] + '.0'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPayload = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $taskRoot 'out\addin' }
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'out\installer' }
if (!$CompilerPath) {
    $taskCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup or set INNO_SETUP_COMPILER to ISCC.exe' }
$taskFiles = @('MechCue.AddIn.comhost.dll','MechCue.AddIn.dll','MechCue.AddIn.deps.json','MechCue.AddIn.runtimeconfig.json','MechCue.dll','MechCue.runtimeconfig.json','MechCue.deps.json')
if (!$AddInOnly) { $taskFiles += 'MechCue.exe' }
foreach ($taskFile in $taskFiles) { if (!(Test-Path -LiteralPath (Join-Path $taskPayload $taskFile))) { throw "Missing payload: $taskFile" } }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskBase = if ($AddInOnly) { 'MechCue-AddIn-Setup' } else { 'MechCue-Setup' }
$taskMode = if ($AddInOnly) { 1 } else { 0 }
& $CompilerPath /Qp "/DAppVersion=$Version" "/DBinaryVersion=$taskBinaryVersion" "/DPayloadDir=$taskPayload" "/DOutputFolder=$taskOutput" "/DProjectRoot=$taskRoot" "/DAddInOnly=$taskMode" "/F$taskBase" (Join-Path $PSScriptRoot 'MechCue.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }
$taskSetup = Join-Path $taskOutput ($taskBase + '.exe')
$taskRecords = foreach ($taskFile in $taskFiles) { @{ Name = $taskFile; SHA256 = (Get-FileHash -LiteralPath (Join-Path $taskPayload $taskFile) -Algorithm SHA256).Hash.ToLowerInvariant() } }
@{ Format = 'Inno Setup'; Version = $Version; AddInOnly = [bool]$AddInOnly; Payload = @($taskRecords); SetupSHA256 = (Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant(); Signed = ((Get-AuthenticodeSignature -LiteralPath $taskSetup).Status -eq 'Valid') } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($taskSetup + '.manifest.json') -Encoding UTF8
Write-Output $taskSetup