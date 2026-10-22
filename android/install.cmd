@echo off
rem Builds "Monkey Play.apk" and installs it straight onto your phone, over USB or Wi-Fi, keeping the
rem app's settings (it's an update, signed with the same key). Then it opens the app on the phone.
rem
rem   install            build, then install on the connected phone
rem   install nobuild    install the last build without building again
rem   install pair       first-time Wi-Fi setup (Android 11+): pairs and connects this PC to the phone
rem   install wifi       reconnect over Wi-Fi to a phone paired before
rem
rem Needs platform-tools (adb) in %ANDROID_HOME% or %LOCALAPPDATA%\Android\Sdk, and on the phone:
rem Settings > About phone > tap "Build number" 7 times, then Settings > Developer options > turn on
rem "USB debugging" (for a cable) or "Wireless debugging" (for Wi-Fi).
setlocal
cd /d "%~dp0"

if "%ANDROID_HOME%"=="" set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"
set "ADB=%ANDROID_HOME%\platform-tools\adb.exe"
if not exist "%ADB%" (
    echo Can't find adb at "%ADB%". Install the SDK's platform-tools.
    exit /b 1
)
set "PKG=com.binumadhavan.monkeyplay"
set "LAST=%USERPROFILE%\.android\monkeyplay-phone.txt"

if /i "%~1"=="pair" goto pair
if /i "%~1"=="wifi" goto wifi

:install
if /i not "%~1"=="nobuild" (
    call "%~dp0build.cmd" || exit /b 1
)
if not exist "Monkey Play.apk" (
    echo There's no "Monkey Play.apk" yet. Run install without "nobuild" first.
    exit /b 1
)

rem Find the phone: a cable, or the Wi-Fi address used last time.
"%ADB%" start-server >nul 2>nul
call :count
if %N%==0 if exist "%LAST%" (
    set /p ADDR=<"%LAST%"
    call :connect
    call :count
)
if %N%==0 (
    echo.
    echo No phone found. Either:
    echo   - plug it in with a USB cable, turn on USB debugging, and tap "Allow" on the phone, or
    echo   - run "install pair" once to set up Wi-Fi.
    exit /b 1
)
if not %N%==1 (
    echo More than one phone is connected. Unplug or disconnect all but one:
    "%ADB%" devices -l
    exit /b 1
)

echo Installing on the phone...
"%ADB%" install -r "Monkey Play.apk"
if errorlevel 1 (
    echo.
    echo The install didn't go through.
    echo   - "not enough space": the phone's storage is full. Free up some space on it, then run install again.
    echo   - signatures don't match: the phone has a copy built on another PC. Uninstall Monkey Play on the
    echo     phone once, then run install again.
    exit /b 1
)
"%ADB%" shell am start -n %PKG%/.MainActivity >nul 2>nul
echo Done. Monkey Play is updated and open on your phone.
exit /b 0

:pair
echo On the phone: Settings ^> Developer options ^> Wireless debugging ^> "Pair device with pairing code".
echo (The phone and this PC must be on the same Wi-Fi.)
set /p PAIRADDR=Type the IP address and port shown under the code (like 192.168.1.5:37123):
set /p CODE=Type the six-digit pairing code:
"%ADB%" pair %PAIRADDR% %CODE% || exit /b 1
echo.
echo Paired. Now the address on the main Wireless debugging screen (its port is different from the pairing one).
set /p ADDR=Type the "IP address & Port" shown there:
call :connect
call :count
if %N%==0 exit /b 1
echo Connected. From now on just run "install".
exit /b 0

:wifi
set /p ADDR=Type the "IP address & Port" from Wireless debugging on the phone:
call :connect
exit /b 0

:connect
"%ADB%" connect %ADDR% | find /i "connected to" >nul
if errorlevel 1 (
    echo Couldn't connect to %ADDR% over Wi-Fi. Check Wireless debugging is on; its port changes each time it's turned on.
    exit /b 0
)
if not exist "%USERPROFILE%\.android" mkdir "%USERPROFILE%\.android"
>"%LAST%" echo %ADDR%
echo Connected to the phone at %ADDR%.
exit /b 0

:count
set N=0
for /f "skip=1 tokens=1,2" %%a in ('"%ADB%" devices') do if "%%b"=="device" set /a N+=1
exit /b 0
