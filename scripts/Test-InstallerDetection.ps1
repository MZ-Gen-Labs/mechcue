param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskCompiler = $env:INNO_SETUP_COMPILER
if (!$taskCompiler) { $taskCompiler = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
if (!$taskCompiler) { throw 'Inno Setup compiler missing' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskInclude = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Installer\AddInOnlyDetection.iss'))
# The shared detection code reads a temporary per-user key instead of the real uninstall key.
$taskKeyName = 'Software\MechCue-InstallerDetectionTest-' + [Guid]::NewGuid().ToString('N')
$taskHarness = @"
#define DetectRoot "HKCU"
#define DetectKey "$taskKeyName"
[Setup]
AppName=MechCue add-in-only detection test
AppVersion=1
DefaultDirName={tmp}\NeverInstalled
PrivilegesRequired=lowest
Uninstallable=no
OutputDir=$taskOutput
OutputBaseFilename=DetectionTest
[Code]
#include "$taskInclude"
function InitializeSetup: Boolean;
begin
  if PreviousAddInOnlySetup then SaveStringToFile(ExpandConstant('{param:RESULT}'), '1', False)
  else SaveStringToFile(ExpandConstant('{param:RESULT}'), '0', False);
  Result := False;
end;
"@
$taskSource = Join-Path $taskOutput 'DetectionTest.iss'
[IO.File]::WriteAllText($taskSource,$taskHarness,[Text.UTF8Encoding]::new($true))
& $taskCompiler /Qp $taskSource
if ($LASTEXITCODE -ne 0) { throw 'Add-in-only detection harness compile failed' }
$taskWithExe = Join-Path $taskOutput 'previous-full'; $taskWithoutExe = Join-Path $taskOutput 'previous-addin'
New-Item -ItemType Directory -Path $taskWithExe,$taskWithoutExe -Force | Out-Null
Set-Content -LiteralPath (Join-Path $taskWithExe 'MechCue.exe') -Value 'placeholder'
# Display names of the former editions, kept ASCII-only for Windows PowerShell 5.1.
$taskFullName = [regex]::Unescape('MechCue \u30bf\u30a4\u30e0\u30c1\u30e3\u30fc\u30c8')
$taskAddInName = $taskFullName + [regex]::Unescape('\uff08\u30a2\u30c9\u30a4\u30f3\u7248\uff09')
$taskCases = @(
    @{ Name = 'no previous installation'; Values = $null; Expected = '0' },
    @{ Name = 'standalone recorded as selected'; Values = @{ 'Inno Setup: Selected Components' = 'core,standalone,mcp'; 'Inno Setup: App Path' = $taskWithoutExe }; Expected = '0' },
    @{ Name = 'standalone recorded as deselected'; Values = @{ 'Inno Setup: Selected Components' = 'core'; 'Inno Setup: Deselected Components' = 'standalone,mcp'; 'Inno Setup: App Path' = $taskWithoutExe }; Expected = '0' },
    @{ Name = 'former add-in-only setup (no MechCue.exe)'; Values = @{ 'Inno Setup: Selected Components' = 'core,mcp'; 'Inno Setup: App Path' = $taskWithoutExe }; Expected = '1' },
    @{ Name = 'former full setup (MechCue.exe present)'; Values = @{ 'Inno Setup: Selected Components' = 'core,mcp'; 'Inno Setup: App Path' = $taskWithExe; 'DisplayName' = $taskAddInName }; Expected = '0' },
    @{ Name = 'missing folder, add-in display name'; Values = @{ 'Inno Setup: App Path' = (Join-Path $taskOutput 'missing'); 'DisplayName' = $taskAddInName }; Expected = '1' },
    @{ Name = 'missing folder, full display name'; Values = @{ 'DisplayName' = $taskFullName }; Expected = '0' }
)
$taskResults = @()
try {
    foreach ($taskCase in $taskCases) {
        Remove-Item -LiteralPath "HKCU:\$taskKeyName" -Recurse -ErrorAction SilentlyContinue
        if ($taskCase.Values) {
            New-Item -Path "HKCU:\$taskKeyName" -Force | Out-Null
            foreach ($taskValue in $taskCase.Values.GetEnumerator()) { New-ItemProperty -Path "HKCU:\$taskKeyName" -Name $taskValue.Key -Value $taskValue.Value -PropertyType String -Force | Out-Null }
        }
        $taskResult = Join-Path $taskOutput 'result.txt'; Remove-Item -LiteralPath $taskResult -ErrorAction SilentlyContinue
        $taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'DetectionTest.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',('/RESULT="' + $taskResult + '"')) -WindowStyle Hidden -Wait -PassThru
        $taskActual = if (Test-Path -LiteralPath $taskResult) { ([IO.File]::ReadAllText($taskResult)).Trim() } else { "missing (exit $($taskProcess.ExitCode))" }
        if ($taskActual -ne $taskCase.Expected) { throw "Add-in-only detection, $($taskCase.Name): expected $($taskCase.Expected), got $taskActual" }
        $taskResults += $taskCase.Name
    }
} finally { Remove-Item -LiteralPath "HKCU:\$taskKeyName" -Recurse -ErrorAction SilentlyContinue }
'PASS: Add-in-only update detection (' + ($taskResults -join '; ') + '); no installation performed.'
