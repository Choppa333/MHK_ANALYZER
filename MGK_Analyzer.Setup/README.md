# MGK Analyzer Installer Setup Guide

## Prerequisites

Before creating the installer, you need to install:

### 1. WiX Toolset (Required)
- Download and install WiX Toolset v3.14 from: https://wixtoolset.org/releases/
- This provides the tools needed to create Windows Installer packages (.msi)

### 2. Visual Studio Extension (Optional but Recommended)
- Install "WiX Toolset Visual Studio Extension" from Visual Studio Marketplace
- This allows building WiX projects directly from Visual Studio

## Project Structure

```
MGK_Analyzer.Setup/
戍式式 MGK_Analyzer.Setup.wixproj    # WiX project file
戍式式 Product.wxs                    # Main WiX source file (defines installer structure)
戍式式 License.rtf                    # License file (to be created)
戍式式 Banner.bmp                     # Installer banner (to be created)
戍式式 Dialog.bmp                     # Installer dialog background (to be created)
戌式式 README.md                      # This file
```

## Building the Installer

### Option 1: Using Visual Studio

1. Open the solution in Visual Studio
2. Right-click on the `MGK_Analyzer.Setup` project
3. Select "Build"
4. The .msi installer will be created in: `MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi`

### Option 2: Using PowerShell (build.ps1)

```powershell
.\MGK_Analyzer.Setup\build.ps1
```

### Option 3: Using Command Line

```cmd
cd MGK_Analyzer.Setup
msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
```

## What Gets Installed

- **Application Executable**: `MGK_Analyzer.exe`
- **Application Libraries**: All required .NET 8 assemblies and dependencies
- **Syncfusion Libraries**: SfChart, SfGrid, SfHeatMap, SfInput, and theme assemblies
- **OxyPlot Libraries**: For 2D contour map visualization
- **Program Shortcuts**: Start menu and desktop shortcuts
- **Registry Entries**: For application tracking and uninstall

## Installation Locations

- **Default Install Path**: `C:\Program Files\MGK Analyzer\`
- **Shortcuts**: Start Menu ⊥ MGK Analyzer
- **Desktop Shortcut**: Optional (user can select during installation)

## Configuration Changes Required

Before building, you may want to customize:

1. **Product.wxs** - Update the following values:
   - `Product Id`: Change from `*` to a specific GUID if needed
   - `Version`: Update version number
   - `Manufacturer`: Change to your organization name
   - `UpgradeCode`: Must remain the same for updates/repairs

2. **License.rtf** - Add your license text

3. **Branding Images** - Add custom images:
   - `Banner.bmp`: 493▼58 pixels
   - `Dialog.bmp`: 493▼312 pixels

## Creating Missing Assets

### License File (License.rtf)

Run PowerShell:
```powershell
# Create a simple RTF license file
$rtf = @"
{\rtf1\ansi\ansicpg1252\cocoartf2\cuc
{\colortbl;\red255\green255\blue255;}
{\*\expandedcolortbl;;}
{\fonttbl{\f0\fswiss\fcharset0 Helvetica;}}
{\*\fonttbl {\f0\fswiss Helvetica;}}
\margl1080\margr1080\margtsxn1440\margbsxn1440
\f0\fs20\lang1033 MGK Analyzer License\par
\par
Copyright (c) MGK Analyzer. All rights reserved.\par
\par
Redistribution and use are permitted under the following conditions:\par
1. Redistributions must retain the above copyright notice\par
2. Distributions must include this license notice
}
"@
$rtf | Out-File "MGK_Analyzer.Setup\License.rtf" -Encoding UTF8
```

### Placeholder Images (Banner.bmp & Dialog.bmp)

These can be created using:
- Microsoft Paint
- PowerShell ImageMagick cmdlets
- Online image tools

Or download placeholder images from WiX Toolset documentation.

## Distributing the Installer

Once built, distribute the `.msi` file:

```cmd
MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
```

Users can install by:
1. Double-clicking the .msi file
2. Or running: `msiexec /i MGK_Analyzer.msi`

## Uninstalling

Users can uninstall through:
1. Control Panel ⊥ Programs ⊥ Programs and Features
2. Right-click uninstall
3. Or command line: `msiexec /x MGK_Analyzer.msi`

## Troubleshooting

### "WiX not found" Error
- Ensure WiX Toolset v3.14 is installed
- Restart Visual Studio after installation
- Check that WiX is in Program Files

### Missing DLLs in Installer
- Ensure MGK_Analyzer project is built first
- Check that all dependencies are in the build output folder

### Installer Won't Install
- Run as Administrator
- Check Windows Event Viewer for MSI error logs
- Verify .NET 8 runtime is installed on target machine

## Additional Resources

- [WiX Toolset Documentation](https://wixtoolset.org/documentation/)
- [WiX Tutorial](https://wixtoolset.org/documentation/manual/v3/index.html)
- [Heat Tool (Harvester)](https://wixtoolset.org/documentation/manual/v3/overview/heat.html)

## Next Steps

1. Add `License.rtf` to the project
2. Add or create `Banner.bmp` and `Dialog.bmp`
3. Build the project to generate the .msi installer
4. Test the installer on a clean machine or virtual machine
5. Distribute the .msi file to users
