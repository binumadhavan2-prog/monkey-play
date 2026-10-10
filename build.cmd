@echo off
rem Builds "Monkey Play.exe" from MonkeyPlay.cs, using the C# compiler that comes with Windows (.NET Framework 4).
rem Extra arguments go to the compiler, e.g.  build -define:SITDEMO  for a test build.
setlocal
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo Can't find the C# compiler ^(csc.exe^) from .NET Framework 4.
    exit /b 1
)

tasklist /fi "imagename eq Monkey Play.exe" 2>nul | find /i "Monkey Play.exe" >nul
if not errorlevel 1 (
    echo The monkey is running. Quit it first: right-click it, then "Quit monkey".
    exit /b 1
)

"%FW%\csc.exe" -nologo -target:winexe -optimize -out:"Monkey Play.exe" ^
    -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
    -r:"%FW%\WPF\UIAutomationClient.dll" -r:"%FW%\WPF\UIAutomationTypes.dll" -r:"%FW%\WPF\WindowsBase.dll" ^
    %* MonkeyPlay.cs
if errorlevel 1 exit /b 1
echo Built "Monkey Play.exe".
