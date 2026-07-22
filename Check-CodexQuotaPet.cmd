@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "bin\CodexQuotaPet.Cli.exe" call Build.cmd
if errorlevel 1 exit /b %errorlevel%

echo Environment:
"bin\CodexQuotaPet.Cli.exe" --check
echo.
echo Live read:
"bin\CodexQuotaPet.Cli.exe" --once
echo.
pause
