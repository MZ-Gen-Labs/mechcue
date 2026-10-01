param([Parameter(Mandatory)][string]$McpDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskCompiler = $env:INNO_SETUP_COMPILER
if (!$taskCompiler) { $taskCompiler = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
if (!$taskCompiler) { throw 'Inno Setup compiler missing' }
$taskControl = Join-Path ([IO.Path]::GetFullPath($McpDirectory)) 'control'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskEntries = @('MechCue.Mcp.Control.exe','MechCue.Mcp.Control.dll','MechCue.Mcp.Control.deps.json','MechCue.Mcp.Control.runtimeconfig.json','MechCue.dll') | ForEach-Object { 'Source: "' + (Join-Path $taskControl $_) + '"; DestDir: "{tmp}\MechCueUpdate"; Flags: dontcopy' }
$taskHarness = @"
[Setup]
AppName=MechCue update extraction test
AppVersion=1
DefaultDirName={tmp}\NeverInstalled
PrivilegesRequired=lowest
Uninstallable=no
OutputDir=$taskOutput
OutputBaseFilename=UpdateExtractionTest
[Files]
$($taskEntries -join "`n")
[Code]
function InitializeSetup: Boolean;
var ExitCode: Integer;
begin
  ExtractTemporaryFiles('{tmp}\MechCueUpdate\*');
  ExitCode := -1;
  if Exec(ExpandConstant('{tmp}\MechCueUpdate\MechCue.Mcp.Control.exe'),
    '--shutdown-for-update "' + ExpandConstant('{tmp}\NeverInstalled') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    SaveStringToFile(ExpandConstant('{param:RESULT}'), IntToStr(ExitCode), False);
  Result := False;
end;
"@
$taskSource = Join-Path $taskOutput 'UpdateExtractionTest.iss'
[IO.File]::WriteAllText($taskSource,$taskHarness,[Text.UTF8Encoding]::new($false))
& $taskCompiler /Qp $taskSource
if ($LASTEXITCODE -ne 0) { throw 'Update extraction harness compile failed' }
$taskResult = Join-Path $taskOutput 'result.txt'
$taskTest = Start-Process -FilePath (Join-Path $taskOutput 'UpdateExtractionTest.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES',('/RESULT="' + $taskResult + '"')) -WindowStyle Hidden -Wait -PassThru
if (!(Test-Path -LiteralPath $taskResult) -or ([IO.File]::ReadAllText($taskResult)).Trim() -ne '0') { throw "Installer helper extraction/run failed: $($taskTest.ExitCode)" }
'PASS: Inno temporary helper extraction and execution; no installation performed.'
