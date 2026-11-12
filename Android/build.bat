@echo off
rem Build the Android app with the SDK build-tools only (no Gradle).
rem Needs: Android SDK (build-tools + a platform) and a JDK 11+ (Android Studio ships one).
setlocal
set "HERE=%~dp0"
set "PKG=io\github\shu8e46\todaymeal2"

set "SDK=%ANDROID_HOME%"
if not exist "%SDK%\build-tools" set "SDK=%LOCALAPPDATA%\Android\Sdk"
if not exist "%SDK%\build-tools" set "SDK=%USERPROFILE%\Android\Sdk"
if not exist "%SDK%\build-tools" (
  echo Android SDK not found. Set ANDROID_HOME.
  exit /b 1
)
for /d %%d in ("%SDK%\build-tools\*") do set "BT=%%d"
for /d %%d in ("%SDK%\platforms\android-*") do set "PLATFORM=%%d"

if exist "%ProgramFiles%\Android\Android Studio\jbr\bin\javac.exe" set "JAVA_HOME=%ProgramFiles%\Android\Android Studio\jbr"
if not exist "%JAVA_HOME%\bin\javac.exe" (
  echo JDK not found. Install Android Studio or set JAVA_HOME to a JDK 11 or newer.
  exit /b 1
)
set "PATH=%JAVA_HOME%\bin;%PATH%"

set "OUT=%HERE%build"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%\apk\assets\web" "%OUT%\classes"
if not exist "%HERE%bin" mkdir "%HERE%bin"

rem The app shows the web version, packed inside the apk.
xcopy /e /q /y "%HERE%..\Web\*" "%OUT%\apk\assets\web\" >nul || goto fail

"%BT%\aapt2.exe" compile --dir "%HERE%res" -o "%OUT%\res.zip" || goto fail
"%BT%\aapt2.exe" link -o "%OUT%\base.apk" -I "%PLATFORM%\android.jar" --manifest "%HERE%AndroidManifest.xml" "%OUT%\res.zip" || goto fail

javac -encoding UTF-8 --release 11 -Xlint:-options -cp "%PLATFORM%\android.jar" -d "%OUT%\classes" "%HERE%src\%PKG%\MainActivity.java" || goto fail
jar cf "%OUT%\classes.jar" -C "%OUT%\classes" . || goto fail
call "%BT%\d8.bat" --release --min-api 26 --lib "%PLATFORM%\android.jar" --output "%OUT%\apk" "%OUT%\classes.jar" || goto fail

rem Add the code and the web files. (jar writes forward-slash zip paths; aapt2 -A does not on Windows.)
jar uf "%OUT%\base.apk" -C "%OUT%\apk" . || goto fail

"%BT%\zipalign.exe" -f -p 4 "%OUT%\base.apk" "%OUT%\aligned.apk" || goto fail

rem The signing key is made once and reused, so updates install over the old app. Keep the file.
if not exist "%HERE%todaymeal.keystore" (
  keytool -genkeypair -keystore "%HERE%todaymeal.keystore" -storepass todaymeal -keypass todaymeal -alias todaymeal -keyalg RSA -keysize 2048 -validity 20000 -dname "CN=TodayMeal2" || goto fail
)
call "%BT%\apksigner.bat" sign --ks "%HERE%todaymeal.keystore" --ks-pass pass:todaymeal --key-pass pass:todaymeal --ks-key-alias todaymeal --out "%HERE%bin\TodayMeal.apk" "%OUT%\aligned.apk" || goto fail

echo.
echo OK: %HERE%bin\TodayMeal.apk
exit /b 0

:fail
echo.
echo BUILD FAILED
exit /b 1