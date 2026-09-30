param([switch]$Unregister, [switch]$AllUsers, [switch]$CheckOnly, [string]$AddInDirectory = (Join-Path $PSScriptRoot '..\out\addin'))
$ErrorActionPreference = 'Stop'
$taskClsid = '{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}'
$taskClasses = if ($AllUsers) { "HKLM:\SOFTWARE\Classes" } else { "HKCU:\Software\Classes" }
$taskRoot = "$taskClasses\CLSID\$taskClsid"
if ($CheckOnly) {
    $taskAction = if ($Unregister) { 'UNREGISTER' } else { 'REGISTER' }
    Write-Output "$taskAction $taskRoot (check only; no changes)"
    return
}
if ($Unregister) {
    if (Test-Path -LiteralPath $taskRoot) { Remove-Item -LiteralPath $taskRoot -Recurse }
    $taskProgId = "$taskClasses\MechCue.TimeChartAddIn"
    if (Test-Path -LiteralPath $taskProgId) { Remove-Item -LiteralPath $taskProgId -Recurse }
    if ((Test-Path -LiteralPath $taskRoot) -or (Test-Path -LiteralPath $taskProgId)) { throw 'Registration keys remain; unregistration failed.' }
    Write-Output "Registration removed and verified: $taskRoot"
    return
}
$taskHost = Join-Path ([System.IO.Path]::GetFullPath($AddInDirectory)) 'MechCue.AddIn.comhost.dll'
if (!(Test-Path -LiteralPath $taskHost)) { throw "Build the add-in first: $taskHost" }
New-Item -Path "$taskRoot\InprocServer32" -Force | Out-Null
Set-Item -LiteralPath $taskRoot -Value 'MechCue Time Chart'
Set-Item -LiteralPath "$taskRoot\InprocServer32" -Value $taskHost
New-Item -Path "$taskRoot\ProgID" -Force | Out-Null
Set-Item -LiteralPath "$taskRoot\ProgID" -Value 'MechCue.TimeChartAddIn'
New-ItemProperty -LiteralPath "$taskRoot\InprocServer32" -Name ThreadingModel -Value Both -PropertyType String -Force | Out-Null
New-ItemProperty -LiteralPath $taskRoot -Name AutoConnect -Value 1 -PropertyType DWord -Force | Out-Null
foreach ($taskLocale in @('409','411')) { New-ItemProperty -LiteralPath $taskRoot -Name $taskLocale -Value 'MechCue Time Chart' -PropertyType String -Force | Out-Null }
New-Item -Path "$taskRoot\Summary" -Force | Out-Null
foreach ($taskLocale in @('409','411')) { New-ItemProperty -LiteralPath "$taskRoot\Summary" -Name $taskLocale -Value 'Time-displacement assembly control' -PropertyType String -Force | Out-Null }
New-Item -Path "$taskRoot\Implemented Categories\{26B1D2D1-2B03-11D2-B589-080036E8B802}" -Force | Out-Null
New-Item -Path "$taskRoot\Environment Categories\{26618395-09D6-11D1-BA07-080036230602}" -Force | Out-Null
New-Item -Path "$taskClasses\MechCue.TimeChartAddIn\CLSID" -Force | Out-Null
Set-Item -LiteralPath "$taskClasses\MechCue.TimeChartAddIn\CLSID" -Value $taskClsid
Write-Output "Add-in registered: $taskRoot"
