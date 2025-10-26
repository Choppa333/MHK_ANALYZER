#!/bin/bash
# Build script for MGK Analyzer Installer (Linux/Mac alternative)
# This script builds the Release version using WiX (via WSL if on Windows)

set -e

echo "================================"
echo "MGK Analyzer Installer Build"
echo "================================"
echo ""

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SOLUTION_DIR="$(dirname "$SCRIPT_DIR")"

echo "Script Directory: $SCRIPT_DIR"
echo "Solution Directory: $SOLUTION_DIR"
echo ""

# Build the main application first
echo "Building MGK_Analyzer project..."
cd "$SOLUTION_DIR"
dotnet build MGK_Analyzer.csproj -c Release -p:Platform=x64

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to build MGK_Analyzer project"
    exit 1
fi

echo "MGK_Analyzer project built successfully"
echo ""

# Note: WiX is Windows-only, this script requires msbuild
# On macOS or Linux with WSL, you would need to run the build in WSL
echo "NOTE: WiX Toolset is Windows-only"
echo "Building WiX setup project requires Windows/WSL with msbuild"
echo ""

# Build the WiX setup project (if msbuild is available)
if command -v msbuild &> /dev/null; then
    echo "Building WiX setup project..."
    cd "$SCRIPT_DIR"
    msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
    
    if [ $? -ne 0 ]; then
        echo "ERROR: Failed to build WiX setup project"
        exit 1
    fi
    
    echo "WiX setup project built successfully"
    echo ""
    echo "================================"
    echo "BUILD SUCCESSFUL!"
    echo "================================"
    echo "Installer location: $SCRIPT_DIR/bin/Release/MGK_Analyzer.msi"
else
    echo "msbuild not found. Please use Windows/WSL or build using Visual Studio"
fi
