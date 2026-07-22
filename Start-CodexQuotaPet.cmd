@echo off
setlocal
cd /d "%~dp0"

if not exist "bin\CodexQuotaPet.exe" (
  call Build.cmd
  if errorlevel 1 (
    echo.
    echo Build failed. Press any key to close.
    pause >nul
    exit /b 1
  )
)

start "" "bin\CodexQuotaPet.exe"
exit /b 0
