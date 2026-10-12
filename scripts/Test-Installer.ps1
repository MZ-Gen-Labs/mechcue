param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$taskSetup = Get-Item -LiteralPath $Path
$taskManifest = Get-Content -LiteralPath ($taskSetup.FullName + '.manifest.json') -Raw | ConvertFrom-Json
if ($taskManifest.Format -ne 'Inno Setup') { throw 'Unexpected installer format' }
if ((Get-FileHash -LiteralPath $taskSetup.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskManifest.SetupSHA256) { throw 'Setup checksum differs from build record' }
$taskRequired = @('MechCue.AddIn.comhost.dll','MechCue.AddIn.dll','MechCue.AddIn.deps.json','MechCue.AddIn.runtimeconfig.json','MechCue.dll','MechCue.runtimeconfig.json','MechCue.deps.json')
foreach ($taskName in $taskRequired) { if ($taskName -notin $taskManifest.Payload.Name) { throw "Missing compiler input: $taskName" } }
if (!$taskManifest.StandaloneComponent -or 'MechCue.exe' -notin $taskManifest.Payload.Name) { throw 'Setup lacks the optional standalone component' }
$taskScript = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Installer\MechCue.iss') -Raw) + (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Installer\AddInOnlyDetection.iss') -Raw)
if ($taskScript -match 'ValueName: "InstallMode"') { throw 'InstallMode registry values do not persist in the Inno uninstall key' }
foreach ($taskPattern in @('Name: "standalone";[^\r\n]*Types: standard custom','Name: "addin";','MechCue\.exe"; DestDir: "\{app\}"; Flags: ignoreversion; Components: standalone','Name: "\{app\}\\MechCue\.exe"; Components: not standalone',"WizardSelectComponents\('!standalone'\)",'PreviousAddInOnlySetup','Inno Setup: Deselected Components')) {
    if ($taskScript -notmatch $taskPattern) { throw "Installer component definition missing: $taskPattern" }
}
if ($taskManifest.Payload.Name | Where-Object { $_ -match 'CADTeam|[\\/]|\.pdb$' }) { throw 'Unexpected compiler input' }
if ($taskManifest.McpIncluded) {
    foreach ($taskName in @('MechCue.Mcp.exe','MechCue.Mcp.dll','MechCue.Mcp.deps.json','MechCue.Mcp.runtimeconfig.json','MechCue.dll','README.md','mcp-config.example.json','MCP-Guide.html','control\MechCue.Mcp.Control.exe','control\MechCue.Mcp.Control.dll','control\MechCue.Mcp.Control.runtimeconfig.json')) {
        if ($taskName -notin $taskManifest.McpPayload.Name) { throw "Missing optional MCP compiler input: $taskName" }
    }
    if ('MechCue.exe' -in $taskManifest.McpPayload.Name) { throw 'MCP component unexpectedly includes standalone EXE' }
    if (!($taskManifest.McpPayload.Name | Where-Object { $_ -like 'ThirdPartyNotices*' })) { throw 'MCP dependency notices missing' }
}
if ($taskSetup.VersionInfo.CompanyName.Trim() -ne 'MZ-Gen-Labs' -or $taskSetup.VersionInfo.ProductVersion.Trim() -ne $taskManifest.Version) { throw 'Publisher or version resources do not match' }
$taskResult = 'PASS: Inno Setup compiled, expected compiler inputs, optional standalone/MCP components, add-in-only update default, setup SHA256, publisher/version resources. No installation performed.'
$taskResult | Set-Content -LiteralPath (Join-Path $taskSetup.DirectoryName 'installer-test-result.txt') -Encoding UTF8
Write-Output $taskResult