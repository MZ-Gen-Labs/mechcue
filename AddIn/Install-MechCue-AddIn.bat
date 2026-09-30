@echo off
setlocal
if /i "%~1"=="--test-messages" goto success
set "MECHCUE_INSTALL_BATCH=%~f0"
if /i "%~1"=="--elevated" goto install

powershell.exe -NoProfile -Command "if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }"
if not errorlevel 1 goto install
echo Please select Yes in the Windows administrator prompt.
powershell.exe -NoProfile -Command "try { Start-Process -FilePath $env:MECHCUE_INSTALL_BATCH -ArgumentList '--elevated' -Verb RunAs -ErrorAction Stop; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
if errorlevel 1 (
  echo Could not start as administrator. Right-click this file and select Run as administrator.
  pause
  exit /b 1
)
exit /b 0

:install
echo MechCue Add-in Registration
echo.
tasklist /fi "IMAGENAME eq Edge.exe" /nh 2>nul | find /i "Edge.exe" >nul
if not errorlevel 1 (
  echo Solid Edge is running. Save your work, close Solid Edge, and run this file again.
  pause
  exit /b 1
)
if not exist "%~dp0Register-AddIn.ps1" (
  echo Register-AddIn.ps1 was not found.
  pause
  exit /b 1
)
if not exist "%~dp0..\out\addin\MechCue.AddIn.comhost.dll" (
  echo Add-in file was not found in the out\addin folder.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-AddIn.ps1" -AllUsers -AddInDirectory "%~dp0..\out\addin"
if errorlevel 1 (
  echo Registration failed. See the error above.
  pause
  exit /b 1
)
"%SystemRoot%\System32\reg.exe" query "HKLM\SOFTWARE\Classes\CLSID\{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}\InprocServer32" /ve /reg:64
if errorlevel 1 (
  echo Could not verify the registration.
  pause
  exit /b 1
)
echo.
:success
echo Registration completed successfully.
echo Start Solid Edge and check MechCue Time Chart in the add-in list.
echo Open the time chart using the MechCue command in the assembly environment.
if /i "%~1"=="--test-messages" exit /b 0
pause
exit /b 0
