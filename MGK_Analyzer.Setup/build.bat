@echo off
REM Build script for MGK Analyzer Installer
REM This script builds the Release version of the installer

setlocal enabledelayedexpansion

echo ================================
echo MGK Analyzer Installer Build
echo ================================
echo.

REM Get the script directory
set SCRIPT_DIR=%~dp0
set SOLUTION_DIR=%SCRIPT_DIR:~0,-1%
for %%A in ("!SOLUTION_DIR!\.") do set SOLUTION_DIR=%%~dpA

echo Script Directory: %SCRIPT_DIR%
echo Solution Directory: %SOLUTION_DIR%

REM Check if WiX is installed
set WIX_PATH=C:\Program Files (x86)\WiX Toolset v3.14\bin
if not exist "%WIX_PATH%" (
    echo ERROR: WiX Toolset not found at %WIX_PATH%
    echo Please install WiX Toolset from: https://wixtoolset.org/releases/
    pause
    exit /b 1
)

echo WiX Toolset found at: %WIX_PATH%
echo.

REM Build the main application first
echo Building MGK_Analyzer project...
cd /d "%SOLUTION_DIR%"
dotnet build MGK_Analyzer.csproj -c Release -p:Platform=x64

if errorlevel 1 (
    echo ERROR: Failed to build MGK_Analyzer project
    pause
    exit /b 1
)

echo MGK_Analyzer project built successfully
echo.

REM Build the WiX setup project
echo Building WiX setup project...
cd /d "%SCRIPT_DIR%"
msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64

if errorlevel 1 (
    echo ERROR: Failed to build WiX setup project
    pause
    exit /b 1
)

echo WiX setup project built successfully
echo.

echo ================================
echo BUILD SUCCESSFUL!
echo ================================
echo.
echo Installer location: %SCRIPT_DIR%bin\Release\MGK_Analyzer.msi
echo.
echo To install, run: msiexec /i "%SCRIPT_DIR%bin\Release\MGK_Analyzer.msi"
echo Or double-click the MSI file
echo.
pause
