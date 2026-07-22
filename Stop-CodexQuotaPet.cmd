@echo off
setlocal
cd /d "%~dp0"

if not exist "bin\CodexQuotaPet.Cli.exe" exit /b 1
"bin\CodexQuotaPet.Cli.exe" --stop
exit /b %errorlevel%
