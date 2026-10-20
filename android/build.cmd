@echo off
rem Builds "Monkey Play.apk" for Android from the files in this folder, with only the Android SDK's
rem command-line tools and Java (JDK 17 or newer) - no Android Studio or Gradle.
rem Needs: platforms;android-34 and build-tools;34.0.0, in %ANDROID_HOME% or %LOCALAPPDATA%\Android\Sdk.
setlocal
cd /d "%~dp0"

if "%ANDROID_HOME%"=="" set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"
set "BT=%ANDROID_HOME%\build-tools\34.0.0"
set "JAR=%ANDROID_HOME%\platforms\android-34\android.jar"
if not exist "%JAR%" (
    echo Can't find the Android SDK at "%ANDROID_HOME%". Install platforms;android-34 and build-tools;34.0.0.
    exit /b 1
)

rem Java's tools (javac, jar, keytool): from JAVA_HOME, else the newest JDK in Program Files, else the PATH.
if "%JAVA_HOME%"=="" for /d %%d in ("%ProgramFiles%\Java\jdk*" "%ProgramFiles%\Eclipse Adoptium\jdk*" "%ProgramFiles%\Microsoft\jdk*") do if exist "%%~d\bin\javac.exe" set "JAVA_HOME=%%~d"
if not "%JAVA_HOME%"=="" set "PATH=%JAVA_HOME%\bin;%PATH%"
where javac >nul 2>nul || (
    echo Can't find javac. Install a JDK ^(17 or newer^) or set JAVA_HOME.
    exit /b 1
)

if exist out rmdir /s /q out
mkdir out\gen out\classes out\dex

rem Resources (icons, colours) and the manifest.
"%BT%\aapt2.exe" compile --dir res -o out\res.zip || exit /b 1
"%BT%\aapt2.exe" link -o out\app.unaligned.apk -I "%JAR%" --manifest AndroidManifest.xml --java out\gen ^
    --min-sdk-version 26 --target-sdk-version 34 --version-code 1 --version-name 1.0 out\res.zip || exit /b 1

rem The code.
javac -nowarn -Xlint:-options -source 8 -target 8 -encoding UTF-8 -bootclasspath "%JAR%;%BT%\core-lambda-stubs.jar" -d out\classes ^
    src\com\binumadhavan\monkeyplay\*.java out\gen\com\binumadhavan\monkeyplay\R.java || exit /b 1
jar -cf out\classes.jar -C out\classes . || exit /b 1
call "%BT%\d8.bat" --release --min-api 26 --lib "%JAR%" --output out\dex out\classes.jar || exit /b 1
jar -uf out\app.unaligned.apk -C out\dex classes.dex || exit /b 1

rem Align and sign it. The signing key is made once and kept in your user folder; updates must use the same key.
set "KS=%USERPROFILE%\.android\monkeyplay.keystore"
if not exist "%KS%" (
    if not exist "%USERPROFILE%\.android" mkdir "%USERPROFILE%\.android"
    keytool -genkeypair -keystore "%KS%" -alias monkeyplay -keyalg RSA -keysize 2048 -validity 10000 ^
        -storepass monkeyplay -keypass monkeyplay -dname "CN=Binu Madhavan, O=Monkey Play" >nul || exit /b 1
)
"%BT%\zipalign.exe" -p -f 4 out\app.unaligned.apk out\app.aligned.apk || exit /b 1
call "%BT%\apksigner.bat" sign --ks "%KS%" --ks-pass pass:monkeyplay --key-pass pass:monkeyplay ^
    --out "Monkey Play.apk" out\app.aligned.apk || exit /b 1
echo Built "Monkey Play.apk".
