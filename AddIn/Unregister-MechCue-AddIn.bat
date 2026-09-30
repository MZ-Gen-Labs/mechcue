@echo off
setlocal
set "MECHCUE_UNREGISTER_BATCH=%~f0"
if /i "%~1"=="--test-messages" goto success
if /i "%~1"=="--elevated" goto unregister

powershell.exe -NoProfile -Command "if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }"
if not errorlevel 1 goto unregister
echo Please select Yes in the Windows administrator prompt.
powershell.exe -NoProfile -Command "try { Start-Process -FilePath $env:MECHCUE_UNREGISTER_BATCH -ArgumentList '--elevated' -Verb RunAs -WindowStyle Normal -ErrorAction Stop; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 (
  echo Could not start as administrator.
  pause
  exit /b 1
)
exit /b 0

:unregister
echo MechCue Add-in Unregistration
tasklist /fi "IMAGENAME eq Edge.exe" /nh 2>nul | find /i "Edge.exe" >nul
if not errorlevel 1 (
  echo Save your work and close Solid Edge before unregistering.
  pause
  exit /b 1
)
if not exist "%~dp0Register-AddIn.ps1" (
  echo Register-AddIn.ps1 was not found next to this batch file.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-AddIn.ps1" -Unregister -AllUsers
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-AddIn.ps1" -Unregister
if errorlevel 1 goto failed

:success
echo Registration removed. Program files and saved charts are unchanged.
echo If alternate administrator credentials were used, unregister the original user's registration separately.
if /i "%~1"=="--test-messages" exit /b 0
pause
exit /b 0

:failed
echo Unregistration failed. See the error above.
pause
exit /b 1
