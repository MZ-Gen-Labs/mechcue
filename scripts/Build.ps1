param(
    [switch]$WithAddIn,
    [string]$Version = '0.1.0-alpha.2',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid version' }
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot "artifacts\$Version" }
if (Test-Path -LiteralPath $taskOutput) { throw "Use a new output directory to avoid packaging stale files: $taskOutput" }
$taskStandalone = Join-Path $taskOutput 'standalone'
& dotnet publish (Join-Path $taskRoot 'MechCue.csproj') -c Release --self-contained false -p:PlatformTarget=x64 "-p:RestoreConfigFile=$taskRoot\NuGet.Config" "-p:Version=$Version" -o $taskStandalone
if ($LASTEXITCODE -ne 0) { throw 'Standalone build failed' }
foreach ($taskCheck in @('--self-test','--smoke-test')) {
    $taskProcess = Start-Process -FilePath (Join-Path $taskStandalone 'MechCue.exe') -ArgumentList $taskCheck -PassThru -Wait -WindowStyle Hidden
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
if ($WithAddIn) {
    $taskAddIn = Join-Path $taskOutput 'addin'
    & dotnet publish (Join-Path $taskRoot 'AddIn\MechCue.AddIn.csproj') -c Release --self-contained false "-p:RestoreConfigFile=$taskRoot\NuGet.Config" "-p:Version=$Version" -o $taskAddIn
    if ($LASTEXITCODE -ne 0) { throw 'Add-in build failed' }
    & dotnet run --project (Join-Path $taskRoot 'tools\ComContractCheck\ComContractCheck.csproj') -c Release "-p:RestoreConfigFile=$taskRoot\NuGet.Config"
    if ($LASTEXITCODE -ne 0) { throw 'COM contract checks failed' }
    $taskInstaller = Join-Path $taskOutput 'installer'
    & (Join-Path $taskRoot 'Installer\Build-Installer.ps1') -PayloadDirectory $taskAddIn -OutputDirectory $taskInstaller -Version $Version
    & powershell.exe -NoProfile -STA -File (Join-Path $PSScriptRoot 'Test-Installer.ps1') -Path (Join-Path $taskInstaller 'MechCue-Setup.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Installer package checks failed' }
    Copy-Item -LiteralPath (Join-Path $taskInstaller 'MechCue-Setup.exe') -Destination (Join-Path $taskDistribution "MechCue-$Version-Setup.exe")
}
Get-ChildItem -LiteralPath $taskDistribution -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -LiteralPath (Join-Path $taskDistribution 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Build and checks passed: $taskDistribution"
