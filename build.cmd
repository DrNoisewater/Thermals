@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK is required to build Thermals.
  echo Install it with:   winget install Microsoft.DotNet.SDK.8
  echo or download it from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo Building Thermals (self-contained, single file)...
dotnet publish src\Thermals.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o dist
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Done:  %~dp0dist\Thermals.exe
echo Copy that one file anywhere and run it. No install needed.
pause
