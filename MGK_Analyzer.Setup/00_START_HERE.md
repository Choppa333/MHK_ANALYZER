# ?? MGK Analyzer Installation Setup

Complete, professional Windows installer setup for MGK Analyzer - a 3D Efficiency Surface Visualization Tool.

## ?? What's Inside

This folder contains everything needed to create and distribute a professional Windows installer (.MSI) for MGK Analyzer.

```
MGK_Analyzer.Setup/
弛
戍式式 ?? Build Scripts
弛   戍式式 build.bat                    # Windows batch build script
弛   戍式式 build.ps1                    # PowerShell build script  
弛   戌式式 build.sh                     # Bash build script (WSL)
弛
戍式式 ?? WiX Installer (Main - Recommended)
弛   戍式式 MGK_Analyzer.Setup.wixproj   # WiX project file
弛   戍式式 Product.wxs                  # Installer configuration
弛   戌式式 License.rtf                  # License agreement
弛
戍式式 ?? Alternative Installer (Optional)
弛   戌式式 MGK_Analyzer.nsi             # NSIS installer script
弛
戍式式 ?? Documentation
弛   戍式式 SETUP_COMPLETE.md            # ? START HERE! Overview & next steps
弛   戍式式 QUICK_START.md               # 5-minute quick reference
弛   戍式式 README.md                    # Technical documentation
弛   戍式式 INSTALLATION_GUIDE.md        # Comprehensive guide
弛   戌式式 setup.config                 # Configuration file
弛
戌式式 ?? Configuration
    戌式式 .gitignore                   # Git ignore patterns
```

---

## ? START HERE

### For New Users: READ THIS FIRST

1. **?? Read:** `SETUP_COMPLETE.md` (Overview, ~2 min read)
2. **? Quick Start:** `QUICK_START.md` (Get building in 5 minutes)
3. **?? Details:** `INSTALLATION_GUIDE.md` (Complete reference)

### Quick Command

```cmd
# Step 1: Download & Install WiX (one time only)
# Visit: https://wixtoolset.org/releases/v3.14/

# Step 2: Build the installer
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
build.bat

# Step 3: Find your installer
# Output: bin\Release\MGK_Analyzer.msi
```

---

## ?? Quick Links

| Need | File | Time |
|------|------|------|
| **Overview** | `SETUP_COMPLETE.md` | 2 min |
| **Quick Build** | `QUICK_START.md` | 5 min |
| **Full Guide** | `INSTALLATION_GUIDE.md` | 15 min |
| **Technical Docs** | `README.md` | 10 min |
| **Configuration** | `setup.config` | Reference |

---

## ?? Two Installer Options

### ? Option 1: WiX Toolset (Recommended)

**Pros:**
- Professional, enterprise-grade installer
- Deep Windows integration
- Automatic updates support
- Repair/reinstall options
- Better security features

**Files:** `MGK_Analyzer.Setup.wixproj`, `Product.wxs`

**Build Command:**
```cmd
build.bat
```

**Output:** `MGK_Analyzer.msi` (~100-150 MB)

---

### ?? Option 2: NSIS (Alternative)

**Pros:**
- Simple to understand
- Smaller file size
- Fast builds
- Works on any Windows version

**Files:** `MGK_Analyzer.nsi`

**Build Command:**
```cmd
"C:\Program Files (x86)\NSIS\makensis.exe" MGK_Analyzer.nsi
```

**Output:** `MGK_Analyzer-1.0.0.0-Setup.exe`

---

## ?? Getting Started

### Prerequisites

- **Windows 10/11** (or Windows 7 SP1+)
- **Visual Studio 2022** or **Build Tools**
- **WiX Toolset v3.14** (for MSI builds)
  - Download: https://wixtoolset.org/releases/v3.14/

### Installation Steps

**Step 1: Install WiX Toolset** (One-time setup)
```cmd
# Download: https://wixtoolset.org/releases/v3.14/
# Run: WiX314.exe
# Install to default location
```

**Step 2: Build the Installer**
```cmd
cd MGK_Analyzer.Setup
build.bat
```

**Step 3: Find Your Installer**
```
MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
```

---

## ?? File Contents

### Build Scripts

**`build.bat`** - Windows Command Prompt
- Recommended for most users
- Simple one-click build
- Shows build progress
- Pauses on error for viewing

**`build.ps1`** - PowerShell
- Advanced options available
- Better error handling
- Requires execution policy change
- `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`

**`build.sh`** - Bash (WSL)
- For developers using Windows Subsystem for Linux
- Requires msbuild in WSL
- Alternative to native build

### Installer Files

**`MGK_Analyzer.Setup.wixproj`**
- WiX project configuration
- Defines build process
- References main application project

**`Product.wxs`**
- WiX source file (XML-based)
- Defines installer structure
- Includes file lists, shortcuts, registry entries
- Configurable: version, manufacturer, paths

**`License.rtf`**
- End-User License Agreement
- Displayed during installation
- Editable for your terms

**`MGK_Analyzer.nsi`**
- NSIS installer script
- Alternative to WiX
- Self-contained installer builder

### Documentation

**`SETUP_COMPLETE.md`** ? **START HERE**
- Overview of what was created
- Immediate next steps
- Quick verification checklist

**`QUICK_START.md`**
- 5-minute quick reference
- Common build scenarios
- Troubleshooting tips

**`README.md`**
- Detailed technical documentation
- Advanced configuration
- WiX-specific details

**`INSTALLATION_GUIDE.md`**
- Comprehensive setup guide
- User installation instructions
- Advanced troubleshooting
- Distribution methods

**`setup.config`**
- Configuration parameters
- Version information
- System requirements

---

## ?? Build Options

### Command Prompt (Easiest)
```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
build.bat
```

### Visual Studio IDE
1. Right-click `MGK_Analyzer.Setup` project
2. Click **Build**
3. Check Output window

### PowerShell
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
.\build.ps1
```

### Manual Command Line
```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER
dotnet build MGK_Analyzer.csproj -c Release
cd MGK_Analyzer.Setup
msbuild MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
```

---

## ?? What Gets Installed

Your installer includes:

? **Application**
- MGK_Analyzer.exe
- All .NET 8 assemblies

? **Libraries**
- Syncfusion SfChart, SfGrid, SfHeatMap
- OxyPlot visualization
- EPPlus (Excel support)

? **Windows Integration**
- Start Menu shortcuts
- Desktop shortcut option
- Uninstall via Add/Remove Programs
- Registry entries

? **Documentation**
- License agreement
- User-friendly UI

---

## ?? Installation for Users

### For End Users

**Method 1: Double-Click**
- Simply double-click `MGK_Analyzer.msi`
- Follow installer wizard
- Takes ~2-3 minutes

**Method 2: Command Line**
```cmd
msiexec /i MGK_Analyzer.msi
```

**Method 3: Silent Install**
```cmd
msiexec /i MGK_Analyzer.msi /quiet /norestart
```

### System Requirements

- Windows 7 SP1 or later
- .NET 8 Desktop Runtime
- 500 MB disk space
- Administrator privileges

---

## ? Verification

After building, verify:

```
MGK_Analyzer.Setup\bin\Release\
戍式式 MGK_Analyzer.msi         ? Main installer
戍式式 MGK_Analyzer.wixpdb      ? Debug symbols
戌式式 MGK_Analyzer.cab         ? Cabinet archive
```

File size should be: **~100-150 MB**

---

## ?? Troubleshooting

### Build Failed: "WiX not found"
- Install WiX v3.14 from https://wixtoolset.org/releases/v3.14/
- Verify: `dir "C:\Program Files (x86)\WiX Toolset v3.14\bin"`

### Build Failed: "Cannot find MGK_Analyzer.exe"
- Build main project first: `dotnet build MGK_Analyzer.csproj -c Release`
- Check output folder exists

### Installation Failed: "Access Denied"
- Run as Administrator
- Check file permissions
- Disable antivirus temporarily

### More Issues?
- See `QUICK_START.md` for common problems
- See `INSTALLATION_GUIDE.md` for advanced troubleshooting

---

## ?? Documentation Map

```
START HERE
    ⊿
SETUP_COMPLETE.md (Overview)
    ⊿
Choose your path:
    戍⊥ QUICK_START.md (Need to build now? Start here)
    戍⊥ README.md (Technical details about WiX)
    戌⊥ INSTALLATION_GUIDE.md (Comprehensive reference)
```

---

## ?? Next Steps

1. ? Install WiX Toolset (5 min)
2. ? Run `build.bat` (2-5 min)
3. ? Find `MGK_Analyzer.msi` in `bin\Release\`
4. ? Test installer on clean machine (optional)
5. ? Distribute to users

**Total Time:** ~15 minutes

---

## ?? Creating Updates

To release version 1.0.0.1:

1. Edit `Product.wxs`
2. Change: `Version="1.0.0.0"` ⊥ `Version="1.0.0.1"`
3. Keep `UpgradeCode` same (allows upgrades)
4. Run `build.bat`
5. Distribute new MSI

Users can upgrade directly - old version is replaced.

---

## ?? Support Resources

| Topic | Resource |
|-------|----------|
| **WiX Help** | https://wixtoolset.org/documentation/ |
| **.NET 8** | https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8 |
| **Windows Installer** | https://docs.microsoft.com/en-us/windows/win32/msi/ |
| **NSIS Help** | https://nsis.sourceforge.io/Docs/ |

---

## ? Key Features

| Feature | Status |
|---------|--------|
| 64-bit support | ? Enabled |
| Silent installation | ? Supported |
| Repair mode | ? Supported |
| Uninstall | ? Full removal |
| Update to newer version | ? Automatic |
| Start Menu shortcuts | ? Created |
| Desktop shortcuts | ? Optional |
| Registry entries | ? Included |

---

## ?? You're All Set!

Everything is ready to build your professional Windows installer.

### One Command to Get Started:

```cmd
cd MGK_Analyzer.Setup && build.bat
```

**Happy Installing! ??**

---

**Version:** 1.0  
**Target:** Windows 64-bit | .NET 8  
**Installer Type:** MSI  
**Created:** 2024
