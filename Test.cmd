@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

call Build.cmd
if errorlevel 1 exit /b %errorlevel%

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "GAC=%SystemRoot%\Microsoft.NET\assembly"
set "REF_CORE=%GAC%\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
set "REF_FRAMEWORK=%GAC%\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
set "REF_BASE=%GAC%\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
set "REF_XAML=%GAC%\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"

"%CSC%" /nologo /target:exe /platform:x64 /out:"bin\CodexQuotaPet.Tests.exe" /main:CodexQuotaPet.Tests.TestEntry /reference:"%REF_CORE%" /reference:"%REF_FRAMEWORK%" /reference:"%REF_BASE%" /reference:"%REF_XAML%" "src\*.cs" "tests\*.cs"
if errorlevel 1 exit /b %errorlevel%

"bin\CodexQuotaPet.Tests.exe"
exit /b %errorlevel%
