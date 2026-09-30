param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
# Run with Windows PowerShell (.NET Framework); load without launching the elevated EXE.
$taskAssembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Path))
$taskTest = $taskAssembly.GetType('MechCueInstaller.Program').GetMethod('Test', [Reflection.BindingFlags]'NonPublic,Static')
$taskTest.Invoke($null, @()) | Out-Null
Write-Output 'PASS: installer package checks (no registry or installation changes)'
