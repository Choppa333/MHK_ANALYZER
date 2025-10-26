# MGK Analyzer - Complete Installation Setup Guide

## ?? Overview

This directory contains all necessary files to create a professional installer for MGK Analyzer. Two options are provided:

1. **WiX Toolset** (Recommended) - Professional MSI installer
2. **NSIS** - Alternative lightweight installer

---

## ?? QUICK START (WiX - Recommended)

### Prerequisites

**Step 1: Install WiX Toolset (One-time setup)**

1. Download from: https://wixtoolset.org/releases/v3.14/
2. Download: `WiX314.exe` (Latest stable version)
3. Run installer and complete the setup
4. Default installation: `C:\Program Files (x86)\WiX Toolset v3.14\`

**Step 2: Verify Installation**

Open Command Prompt and verify WiX is installed:
```cmd
dir "C:\Program Files (x86)\WiX Toolset v3.14\bin"
```

### Building the Installer

**From Command Prompt (as Administrator):**

```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
build.bat
```

**From PowerShell (as Administrator):**

```powershell
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
.\build.ps1
```

**From Visual Studio:**

1. Open the solution in Visual Studio
2. Right-click **MGK_Analyzer.Setup** project
3. Click **Build**
4. Output: `MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi`

### Output

After successful build:
```
MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
```

---

## ?? Files Included

### WiX Setup (Main Option)

| File | Purpose |
|------|---------|
| `MGK_Analyzer.Setup.wixproj` | WiX project configuration |
| `Product.wxs` | Installer definition (main installer logic) |
| `License.rtf` | End-user license agreement |
| `build.ps1` | PowerShell build script |
| `build.bat` | Batch build script |
| `build.sh` | Bash build script (for WSL) |

### NSIS Setup (Alternative Option)

| File | Purpose |
|------|---------|
| `MGK_Analyzer.nsi` | NSIS installer script |

### Documentation

| File | Purpose |
|------|---------|
| `README.md` | Detailed technical documentation |
| `QUICK_START.md` | Quick reference guide |
| `INSTALLATION_GUIDE.md` | This comprehensive guide |

---

## ?? Detailed Setup Instructions

### Option 1: Using WiX Toolset (Recommended)

#### Installation Steps for Developers

**1. Install WiX Toolset:**

- Visit: https://wixtoolset.org/
- Download: WiX v3.14 (Latest stable)
- Run `WiX314.exe`
- Install to default location: `C:\Program Files (x86)\WiX Toolset v3.14\`
- Close and restart Visual Studio

**2. Verify Installation:**

```cmd
# Check WiX installation
dir "C:\Program Files (x86)\WiX Toolset v3.14\bin"

# Should show: candle.exe, light.exe, and other tools
```

**3. Build the Installer:**

```cmd
# Navigate to setup directory
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup

# Run build script
build.bat
```

**4. Verify Build Output:**

```cmd
# Check if MSI was created
dir bin\Release\MGK_Analyzer.msi
```

#### What the WiX Installer Includes

- ? MGK_Analyzer.exe executable
- ? All .NET assemblies and dependencies
- ? Syncfusion libraries (SfChart, SfGrid, SfHeatMap, etc.)
- ? OxyPlot visualization libraries
- ? Start Menu shortcuts
- ? Desktop shortcut option
- ? Uninstall support
- ? Registry entries for Add/Remove Programs

#### Building in Different Scenarios

**Scenario A: From Command Prompt**
```cmd
@echo off
cd /d "C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER"
REM Build main application
dotnet build MGK_Analyzer.csproj -c Release -p:Platform=x64
REM Build WiX installer
cd MGK_Analyzer.Setup
msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
pause
```

**Scenario B: From Visual Studio IDE**
- Solution Explorer ⊥ Right-click `MGK_Analyzer.Setup` ⊥ Build
- Check Output window for success message
- Look in `bin\Release\` for `MGK_Analyzer.msi`

**Scenario C: From PowerShell**
```powershell
$ErrorActionPreference = "Stop"
cd "C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup"
Write-Host "Building MGK_Analyzer..." -ForegroundColor Cyan
dotnet build ..\MGK_Analyzer.csproj -c Release
Write-Host "Building WiX Setup..." -ForegroundColor Cyan
msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
Write-Host "Build Complete!" -ForegroundColor Green
```

---

### Option 2: Using NSIS (Alternative)

#### Installation Steps

**1. Install NSIS:**

- Visit: https://nsis.sourceforge.io/
- Download: NSIS 3.x
- Run installer
- Default location: `C:\Program Files (x86)\NSIS\`

**2. Build the Installer:**

```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
"C:\Program Files (x86)\NSIS\makensis.exe" MGK_Analyzer.nsi
```

**3. Output:**

```
MGK_Analyzer-1.0.0.0-Setup.exe
```

#### Advantages of NSIS

- ? Simpler script syntax
- ? Smaller file size
- ? No external dependencies
- ? Faster build time
- ?? Less professional appearance
- ?? Less integration with Windows

---

## ?? User Installation Guide

### For End Users: Installing MGK Analyzer

#### Method 1: Double-Click (Easiest)

1. Download `MGK_Analyzer.msi`
2. Double-click the file
3. Click "Next" through installer wizard
4. Choose installation location (default: `C:\Program Files\MGK Analyzer\`)
5. Click "Install"
6. Wait for installation to complete
7. Application shortcuts appear on Start Menu

#### Method 2: Command Line (For IT Administrators)

**Standard Installation:**
```cmd
msiexec /i MGK_Analyzer.msi
```

**Silent Installation (No UI):**
```cmd
msiexec /i MGK_Analyzer.msi /quiet /norestart
```

**With Log File:**
```cmd
msiexec /i MGK_Analyzer.msi /l*v install.log
```

**Repair Installation:**
```cmd
msiexec /f MGK_Analyzer.msi
```

#### System Requirements

- Windows 7 SP1 or later (Windows 10/11 recommended)
- 64-bit processor
- .NET 8 Desktop Runtime
- 500 MB free disk space
- Administrator privileges required for installation

---

## ?? Verification

### After Building

Check that these files were created:

```
MGK_Analyzer.Setup\bin\Release\
戍式式 MGK_Analyzer.msi          ∠ Main installer file
戍式式 MGK_Analyzer.wixpdb       ∠ Debug symbols (can be deleted)
戌式式 MGK_Analyzer.cab          ∠ Cabinet file (included in MSI)
```

### After Installation

Verify installation by checking:

1. **Application Folder:**
   ```
   C:\Program Files\MGK Analyzer\
   戍式式 MGK_Analyzer.exe
   戍式式 *.dll (all dependencies)
   戌式式 (other files)
   ```

2. **Start Menu:**
   - Start ⊥ Programs ⊥ MGK Analyzer ⊥ MGK Analyzer

3. **Desktop:**
   - Desktop shortcut created (if selected during install)

4. **Registry:**
   ```
   HKEY_LOCAL_MACHINE\SOFTWARE\MGK Analyzer\MGK_Analyzer
   ```

---

## ?? File Size Information

| Component | Size |
|-----------|------|
| MGK_Analyzer.exe | ~500 KB |
| Syncfusion DLLs | ~50 MB |
| OxyPlot DLLs | ~5 MB |
| .NET 8 dependencies | ~100 MB (may be on system) |
| **Total MSI Size** | **~100-150 MB** |

---

## ?? Troubleshooting

### Problem: "WiX Toolset not found"

**Solution:**
1. Install WiX from https://wixtoolset.org/releases/v3.14/
2. Verify: `dir "C:\Program Files (x86)\WiX Toolset v3.14\bin"`
3. Restart Visual Studio

### Problem: "Cannot find MGK_Analyzer.exe"

**Solution:**
1. Build the main project first:
   ```cmd
   cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER
   dotnet build MGK_Analyzer.csproj -c Release
   ```
2. Check build output folder exists

### Problem: "Installer fails to start"

**Solution:**
1. Run Command Prompt as Administrator
2. Run: `msiexec /i MGK_Analyzer.msi /l*v install_log.txt`
3. Check `install_log.txt` for error details

### Problem: ".NET 8 is not installed"

**Solution:**
- Download .NET 8 Desktop Runtime from: https://dotnet.microsoft.com/download
- Install the 64-bit version
- Restart the installation

### Problem: "Access Denied" during build

**Solution:**
1. Run Visual Studio as Administrator
2. Or run PowerShell/Command Prompt as Administrator
3. Close any running instance of the application

---

## ?? Distribution

### For Distribution to Users

1. **Locate the MSI:**
   ```
   C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
   ```

2. **Distribution Methods:**
   - Email to users
   - Host on web server
   - Share via cloud storage (OneDrive, Google Drive, Dropbox)
   - USB drive
   - Internal software repository

3. **Version Tracking:**
   - Rename file with version: `MGK_Analyzer-1.0.0-Setup.msi`
   - Keep archive of all versions
   - Track release notes

---

## ?? Update/Upgrade Process

### Creating an Updated Version

1. Increment version in `Product.wxs`:
   ```xml
   Version="1.0.0.1"  ∠ Change this
   ```

2. Keep `UpgradeCode` the same (this allows upgrades)

3. Rebuild the installer:
   ```cmd
   build.bat
   ```

4. Users can run new MSI to upgrade automatically

---

## ?? Additional Resources

- [WiX Toolset Official Documentation](https://wixtoolset.org/documentation/)
- [WiX Tutorial](https://wixtoolset.org/documentation/manual/v3/index.html)
- [NSIS Official Documentation](https://nsis.sourceforge.io/Docs/)
- [.NET 8 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)

---

## ? Checklist Before Release

- [ ] WiX Toolset installed (v3.14+)
- [ ] Build succeeds without errors
- [ ] MSI file created successfully
- [ ] Tested on clean Windows machine
- [ ] .NET 8 prerequisite verified
- [ ] Version number updated
- [ ] License agreement in place
- [ ] Desktop/Start Menu shortcuts work
- [ ] Uninstall works correctly
- [ ] All dependencies included

---

## ?? Support

For issues or questions:

1. Check this guide for troubleshooting
2. Review the QUICK_START.md for common scenarios
3. Consult WiX/NSIS documentation
4. Check application error logs

---

**Last Updated:** 2024
**WiX Version:** 3.14
**Target Framework:** .NET 8 (Windows 64-bit)
**Installer Type:** MSI (Windows Installer)
