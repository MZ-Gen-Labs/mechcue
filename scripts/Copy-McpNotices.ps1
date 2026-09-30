param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskAssets=Get-Content -LiteralPath (Join-Path $taskRoot 'Mcp/obj/project.assets.json') -Raw | ConvertFrom-Json
$taskDestination=Join-Path $OutputDirectory 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'Mcp/ThirdPartyLicenses/MCP-Apache-2.0.txt'),(Join-Path $taskRoot 'Mcp/ThirdPartyLicenses/Microsoft-MIT.txt') -Destination $taskDestination
foreach($taskLibrary in $taskAssets.libraries.PSObject.Properties) {
    if($taskLibrary.Value.type -ne 'package'){continue}
    $taskPackage=$null
    foreach($taskPackageRoot in $taskAssets.packageFolders.PSObject.Properties.Name) {
        $taskCandidate=Join-Path $taskPackageRoot $taskLibrary.Value.path
        if(Test-Path -LiteralPath $taskCandidate){$taskPackage=$taskCandidate;break}
    }
    if(!$taskPackage){throw "Missing package metadata for $($taskLibrary.Name)"}
    $taskNoticeDir=Join-Path $taskDestination ($taskLibrary.Name.Replace('/','-'))
    New-Item -ItemType Directory -Path $taskNoticeDir -Force | Out-Null
    Get-ChildItem -LiteralPath $taskPackage -File | Where-Object { $_.Extension -eq '.nuspec' -or $_.Name -match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES)' } | Copy-Item -Destination $taskNoticeDir
}