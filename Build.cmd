@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "GAC=%SystemRoot%\Microsoft.NET\assembly"
set "REF_CORE=%GAC%\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
set "REF_FRAMEWORK=%GAC%\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
set "REF_BASE=%GAC%\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
set "REF_XAML=%GAC%\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"

if not exist "%CSC%" (
  echo [ERROR] Windows C# compiler was not found.
  exit /b 2
)
if not exist "%REF_CORE%" (
  echo [ERROR] WPF desktop runtime was not found.
  exit /b 3
)

if not exist bin mkdir bin
copy /y "settings.example.json" "bin\settings.example.json" >nul

echo Building CodexQuotaPet.exe...
"%CSC%" /nologo /target:winexe /platform:x64 /out:"bin\CodexQuotaPet.exe" /main:CodexQuotaPet.AppEntry /win32icon:"assets\taskbar-icon.ico" /resource:"assets\tray-icon.png",CodexQuotaPet.TrayIcon.png /reference:"%REF_CORE%" /reference:"%REF_FRAMEWORK%" /reference:"%REF_BASE%" /reference:"%REF_XAML%" "src\*.cs"
if errorlevel 1 exit /b %errorlevel%

echo Building CodexQuotaPet.Cli.exe...
"%CSC%" /nologo /target:exe /platform:x64 /out:"bin\CodexQuotaPet.Cli.exe" /main:CodexQuotaPet.AppEntry /win32icon:"assets\taskbar-icon.ico" /resource:"assets\tray-icon.png",CodexQuotaPet.TrayIcon.png /reference:"%REF_CORE%" /reference:"%REF_FRAMEWORK%" /reference:"%REF_BASE%" /reference:"%REF_XAML%" "src\*.cs"
if errorlevel 1 exit /b %errorlevel%

echo Build complete: %CD%\bin
exit /b 0
