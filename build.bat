@echo off
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll /r:Microsoft.VisualBasic.dll
set OPTS=/nologo /target:winexe /platform:x64 /optimize+ /unsafe /codepage:65001 %REFS%
if exist logo.png set OPTS=%OPTS% /resource:logo.png,logo.png
if exist icon.ico set OPTS=%OPTS% /win32icon:icon.ico
if exist Clutch.manifest set OPTS=%OPTS% /win32manifest:Clutch.manifest
set OUT=Clutch.exe
set SRC=src\*.cs
if /i "%~1"=="dev" set OUT=Clutch-dev.exe
if /i "%~1"=="dev" set OPTS=%OPTS% /define:DEV
if /i "%~1"=="dev" set SRC=%SRC% dev\*.cs

"%CSC%" %OPTS% /out:%OUT% %SRC% || goto fail
echo Built %OUT%
if /i not "%~1"=="dev" copy /y Clutch.exe ClutchSetup.exe >nul && echo Built ClutchSetup.exe
exit /b 0

:fail
echo BUILD FAILED
pause
exit /b 1
