@echo off
setlocal
echo MechCue Add-in Update
tasklist /fi "IMAGENAME eq Edge.exe" /nh 2>nul | find /i "Edge.exe" >nul
if not errorlevel 1 (
  echo Save your work and close Solid Edge before updating.
  pause
  exit /b 1
)
if not exist "%~dp0..\out\workspace-addin\MechCue.AddIn.comhost.dll" (
  echo The updated add-in files were not found.
  pause
  exit /b 1
)
robocopy "%~dp0..\out\workspace-addin" "%~dp0..\out\addin" /E /R:1 /W:1 /NFL /NDL /NJH /NJS
if errorlevel 8 (
  echo Update failed. See the error above.
  pause
  exit /b 1
)
echo Update completed. Existing registration is unchanged.
echo Start Solid Edge and open the MechCue time chart.
pause
exit /b 0
