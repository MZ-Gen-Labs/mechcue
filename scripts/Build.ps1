param(
    [switch]$WithAddIn,
    [switch]$WithMcp,
    [string]$Version = '0.3.1-alpha.05',
    [string]$OutputDirectory,
    [string]$PythonPath = 'python'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid version' }
# Keep the requested display version; normalize only the SDK/NuGet identifier.
$taskSdkVersion = [regex]::Replace($Version, '-alpha\.0([0-9])$', '-alpha.$1')
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot "artifacts\$Version" }
if (Test-Path -LiteralPath $taskOutput) { throw "Use a new output directory to avoid packaging stale files: $taskOutput" }
$taskStandalone = Join-Path $taskOutput 'standalone'
& dotnet publish (Join-Path $taskRoot 'MechCue.csproj') -c Release --self-contained false -p:PlatformTarget=x64 "-p:RestoreConfigFile=$taskRoot\NuGet.Config" "-p:Version=$taskSdkVersion" "-p:MechCueReleaseVersion=$Version" -o $taskStandalone
if ($LASTEXITCODE -ne 0) { throw 'Standalone build failed' }
foreach ($taskCheck in @('--self-test','--smoke-test')) {
    $taskCheckLog = Join-Path $taskStandalone ($taskCheck.TrimStart('-') + '-stdout.txt')
    $taskCheckError = Join-Path $taskStandalone ($taskCheck.TrimStart('-') + '-stderr.txt')
    # The console host exposes exception details from the WinExe test entry point.
    $taskProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"' + (Join-Path $taskStandalone 'MechCue.dll') + '"'), $taskCheck) -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput $taskCheckLog -RedirectStandardError $taskCheckError
    Get-Content -LiteralPath $taskCheckLog, $taskCheckError
    if ($taskProcess.ExitCode -ne 0) { throw "Check failed: $taskCheck" }
}
$taskDistribution = Join-Path $taskOutput 'release'
New-Item -ItemType Directory -Path $taskDistribution -Force | Out-Null
# Stage only runtime files; test reports and screenshots are diagnostic artifacts.
$taskPortable = Join-Path $taskOutput 'portable'
New-Item -ItemType Directory -Path $taskPortable -Force | Out-Null
Get-ChildItem -LiteralPath $taskStandalone -File | Where-Object { $_.Extension -in @('.exe','.dll','.json') } | Copy-Item -Destination $taskPortable
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $taskRoot 'README.md') -Destination $taskPortable
Copy-Item -LiteralPath (Join-Path $taskRoot 'examples') -Destination $taskPortable -Recurse
Compress-Archive -Path (Join-Path $taskPortable '*') -DestinationPath (Join-Path $taskDistribution "MechCue-$Version-win-x64.zip")
$taskMcp = $null
if ($WithMcp) {
    $taskMcp = Join-Path $taskOutput 'mcp'
    & dotnet restore (Join-Path $taskRoot 'Mcp/MechCue.Mcp.csproj') --configfile (Join-Path $taskRoot 'Mcp/NuGet.Config') --locked-mode "-p:Version=$taskSdkVersion" "-p:MechCueReleaseVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'MCP locked dependency restore failed' }
    & dotnet publish (Join-Path $taskRoot 'Mcp/MechCue.Mcp.csproj') -c Release --no-restore --self-contained false -p:DebugType=none -p:DebugSymbols=false "-p:Version=$taskSdkVersion" "-p:MechCueReleaseVersion=$Version" -o $taskMcp
    if ($LASTEXITCODE -ne 0) { throw 'MCP build failed' }
    $taskControl = Join-Path $taskMcp 'control'
    & dotnet publish (Join-Path $taskRoot 'McpControl/MechCue.Mcp.Control.csproj') -c Release --self-contained false "-p:RestoreConfigFile=$taskRoot\NuGet.Config" "-p:Version=$taskSdkVersion" "-p:MechCueReleaseVersion=$Version" -o $taskControl
    if ($LASTEXITCODE -ne 0) { throw 'MCP tray controller build failed' }
    $taskOldSettings = $env:MECHCUE_MCP_SETTINGS_PATH
    try {
        $env:MECHCUE_MCP_SETTINGS_PATH = Join-Path $taskOutput 'tray-test-settings.json'
        $taskProcess = Start-Process -FilePath (Join-Path $taskControl 'MechCue.Mcp.Control.exe') -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
        if ($taskProcess.ExitCode -ne 0) { throw 'MCP tray controller test failed' }
    } finally { $env:MECHCUE_MCP_SETTINGS_PATH = $taskOldSettings }
    Get-ChildItem -LiteralPath $taskControl -File | Where-Object { $_.Name -eq 'MechCue.exe' -or $_.Extension -eq '.pdb' -or $_.Name -eq 'tray-test-result.txt' -or $_.Name -eq 'monitor-test-preview.png' } | Remove-Item

    & (Join-Path $PSScriptRoot 'Copy-McpNotices.ps1') -OutputDirectory $taskMcp
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Mcp/README.md'),(Join-Path $taskRoot 'Mcp/mcp-config.example.json'),(Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination $taskMcp
    & $PythonPath (Join-Path $PSScriptRoot 'Test-Mcp.py') --mechcue (Join-Path $taskStandalone 'MechCue.exe') --mcp (Join-Path $taskMcp 'MechCue.Mcp.exe') --report (Join-Path $taskOutput 'mcp-test-result.txt')
    if ($LASTEXITCODE -ne 0) { throw 'MCP protocol and UI integration test failed' }
    & $PythonPath (Join-Path $PSScriptRoot 'Test-McpTraffic.py') --mcp (Join-Path $taskMcp 'MechCue.Mcp.exe') --report (Join-Path $taskOutput 'mcp-traffic-test-result.txt')
    if ($LASTEXITCODE -ne 0) { throw 'MCP traffic monitor transport test failed' }
    & $PythonPath (Join-Path $PSScriptRoot 'Test-McpUpdate.py') --mcp (Join-Path $taskMcp 'MechCue.Mcp.exe') --report (Join-Path $taskOutput 'mcp-update-test-result.txt')
    if ($LASTEXITCODE -ne 0) { throw 'MCP update shutdown test failed' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/CONCEPT_TARGETS.md') -Destination (Join-Path $taskMcp 'CONCEPT_TARGETS.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/DRAWING_AUTOMATION.md') -Destination (Join-Path $taskMcp 'DRAWING_AUTOMATION.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/PMI_AUTOMATION.md') -Destination (Join-Path $taskMcp 'PMI_AUTOMATION.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/SIMULATION_AUTOMATION.md') -Destination (Join-Path $taskMcp 'SIMULATION_AUTOMATION.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/MCP_WORKFLOW_FIXES_20261002.md') -Destination (Join-Path $taskMcp 'MCP_WORKFLOW_FIXES_20261002.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/DETAIL_MCP_IMPROVEMENTS_20261003.md') -Destination (Join-Path $taskMcp 'DETAIL_MCP_IMPROVEMENTS_20261003.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/MACHINING_MCP_20261003.md') -Destination (Join-Path $taskMcp 'MACHINING_MCP_20261003.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/ADAPTIVE_MOTION_INSPECTION.md') -Destination (Join-Path $taskMcp 'ADAPTIVE_MOTION_INSPECTION.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/CONTINUOUS_MOTION_INSPECTION.md') -Destination (Join-Path $taskMcp 'CONTINUOUS_MOTION_INSPECTION.md')
    $taskMcpReadme = Join-Path $taskMcp 'README.md'
    [IO.File]::WriteAllText($taskMcpReadme, [IO.File]::ReadAllText($taskMcpReadme).Replace('../docs/CONCEPT_TARGETS.md','CONCEPT_TARGETS.md').Replace('../docs/DRAWING_AUTOMATION.md','DRAWING_AUTOMATION.md').Replace('../docs/PMI_AUTOMATION.md','PMI_AUTOMATION.md').Replace('../docs/SIMULATION_AUTOMATION.md','SIMULATION_AUTOMATION.md').Replace('../docs/MCP_WORKFLOW_FIXES_20261002.md','MCP_WORKFLOW_FIXES_20261002.md').Replace('../docs/DETAIL_MCP_IMPROVEMENTS_20261003.md','DETAIL_MCP_IMPROVEMENTS_20261003.md'))
    [IO.File]::WriteAllText($taskMcpReadme, [IO.File]::ReadAllText($taskMcpReadme).Replace('../docs/MACHINING_MCP_20261003.md','MACHINING_MCP_20261003.md'))
    [IO.File]::WriteAllText($taskMcpReadme, [IO.File]::ReadAllText($taskMcpReadme).Replace('../docs/ADAPTIVE_MOTION_INSPECTION.md','ADAPTIVE_MOTION_INSPECTION.md'))
    [IO.File]::WriteAllText($taskMcpReadme, [IO.File]::ReadAllText($taskMcpReadme).Replace('../docs/CONTINUOUS_MOTION_INSPECTION.md','CONTINUOUS_MOTION_INSPECTION.md'))
    Compress-Archive -Path (Join-Path $taskMcp '*') -DestinationPath (Join-Path $taskDistribution "MechCue-$Version-MCP-win-x64.zip")
}
if ($WithAddIn) {
    $taskAddIn = Join-Path $taskOutput 'addin'
    & dotnet publish (Join-Path $taskRoot 'AddIn\MechCue.AddIn.csproj') -c Release --self-contained false "-p:RestoreConfigFile=$taskRoot\NuGet.Config" "-p:Version=$taskSdkVersion" "-p:MechCueReleaseVersion=$Version" -o $taskAddIn
    if ($LASTEXITCODE -ne 0) { throw 'Add-in build failed' }
    & dotnet run --project (Join-Path $taskRoot 'tools\ComContractCheck\ComContractCheck.csproj') -c Release "-p:RestoreConfigFile=$taskRoot\NuGet.Config"
    if ($LASTEXITCODE -ne 0) { throw 'COM contract checks failed' }
    if ($WithMcp) { & (Join-Path $PSScriptRoot 'Test-InstallerUpdate.ps1') -McpDirectory $taskMcp -OutputDirectory (Join-Path $taskOutput 'installer-update-test') }
    $taskInstaller = Join-Path $taskOutput 'installer'
    & (Join-Path $taskRoot 'Installer\Build-Installer.ps1') -PayloadDirectory $taskAddIn -McpDirectory $taskMcp -OutputDirectory $taskInstaller -Version $Version
    & powershell.exe -NoProfile -STA -File (Join-Path $PSScriptRoot 'Test-Installer.ps1') -Path (Join-Path $taskInstaller 'MechCue-Setup.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Installer package checks failed' }
    Copy-Item -LiteralPath (Join-Path $taskInstaller 'MechCue-Setup.exe') -Destination (Join-Path $taskDistribution "MechCue-$Version-Setup.exe")
    $taskAddInInstaller = Join-Path $taskOutput 'installer-addin'
    & (Join-Path $taskRoot 'Installer\Build-Installer.ps1') -PayloadDirectory $taskAddIn -McpDirectory $taskMcp -OutputDirectory $taskAddInInstaller -Version $Version -AddInOnly
    & powershell.exe -NoProfile -STA -File (Join-Path $PSScriptRoot 'Test-Installer.ps1') -Path (Join-Path $taskAddInInstaller 'MechCue-AddIn-Setup.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Add-in-only installer package checks failed' }
    Copy-Item -LiteralPath (Join-Path $taskAddInInstaller 'MechCue-AddIn-Setup.exe') -Destination (Join-Path $taskDistribution "MechCue-$Version-AddIn-Setup.exe")
}
Get-ChildItem -LiteralPath $taskDistribution -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -LiteralPath (Join-Path $taskDistribution 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Build and checks passed: $taskDistribution"
