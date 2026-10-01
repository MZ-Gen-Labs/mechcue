@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Open-ReleaseList.ps1"
if errorlevel 1 (
  echo.
  echo Failed to open the release list. See docs\RELEASE_LIST.md.
  pause
)
