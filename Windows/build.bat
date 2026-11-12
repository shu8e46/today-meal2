@echo off
rem Build with the MSBuild and C# compiler that ship with Windows (no SDK needed).
setlocal
set "MSB=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
if not exist "%MSB%" set "MSB=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"

"%MSB%" "%~dp0TodayMeal.csproj" /nologo /v:m /p:Configuration=Release
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)
echo.
echo OK: %~dp0bin\Release\TodayMeal.exe
