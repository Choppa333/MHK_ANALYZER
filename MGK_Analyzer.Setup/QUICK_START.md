# MGK Analyzer Installer - Quick Start Guide

## ?? Quick Start

### Step 1: Install WiX Toolset (ONE TIME ONLY)

**Windows 10/11:**
1. Download WiX Toolset v3.14 from: https://wixtoolset.org/releases/
2. Run the installer: `WiX314.exe`
3. Follow the installation wizard (accept default options)
4. Restart Visual Studio if it's open

**Verify Installation:**
- Check that folder exists: `C:\Program Files (x86)\WiX Toolset v3.14\bin`

---

## ?? Building the Installer

### Option 1: Using the Build Script (Easiest)

**PowerShell (Windows 10/11):**
```powershell
# Run as Administrator
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
.\build.ps1
```

**Command Prompt (Windows):**
```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup
build.bat
```

### Option 2: Using Visual Studio

1. Open the solution in Visual Studio
2. Right-click on **MGK_Analyzer.Setup** project
3. Select **Build**
4. Output will be in: `MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi`

### Option 3: Using Command Line (Manual)

```cmd
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER
dotnet build MGK_Analyzer.csproj -c Release
msbuild MGK_Analyzer.Setup\MGK_Analyzer.Setup.wixproj /p:Configuration=Release /p:Platform=x64
```

---

## ?? Installer Output

After successful build, you'll find:

```
MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
```

This is your installer file!

---

## ?? Installing the Application

### For End Users

**Method 1: Double-Click**
- Just double-click `MGK_Analyzer.msi`
- Follow the installer wizard

**Method 2: Command Line**
```cmd
msiexec /i MGK_Analyzer.msi
```

**Method 3: Silent Install (Automated)**
```cmd
msiexec /i MGK_Analyzer.msi /quiet /norestart
```

### Default Installation Location
- `C:\Program Files\MGK Analyzer\`
- Program shortcuts added to Start Menu
- Optional Desktop shortcut

---

## ??? Uninstalling

Users can uninstall through:
1. **Control Panel** ⊥ Programs ⊥ Programs and Features ⊥ Uninstall
2. **Command Line:**
   ```cmd
   msiexec /x MGK_Analyzer.msi
   ```

---

## ? Prerequisites for End Users

Your installer requires:
- **Windows 7 SP1 or later** (Windows 10/11 recommended)
- **.NET 8 Desktop Runtime** (automatically verified by Windows)
- **Administrator privileges** for installation

---

## ?? Project Structure

```
MGK_Analyzer.Setup/
戍式式 MGK_Analyzer.Setup.wixproj     # WiX project configuration
戍式式 Product.wxs                     # Installer definition (main file)
戍式式 License.rtf                     # License agreement
戍式式 build.ps1                       # PowerShell build script
戍式式 build.bat                       # Batch build script
戍式式 README.md                       # Detailed documentation
戌式式 bin/
    戌式式 Release/
        戌式式 MGK_Analyzer.msi        # Generated installer (after build)
```

---

## ?? Customization

### Update Version Number
Edit `Product.wxs`, line ~5:
```xml
<Product Id="*" 
         Name="MGK Analyzer" 
         Language="1033" 
         Version="1.0.0.0"    ∠ Change this
         ...
```

### Change Manufacturer Name
Edit `Product.wxs`, line ~6:
```xml
Manufacturer="MGK Analyzer"    ∠ Change this
```

### Add Custom Images

Replace or add images for installer branding:
- `Banner.bmp` (493▼58 pixels)
- `Dialog.bmp` (493▼312 pixels)

Update references in `Product.wxs`:
```xml
<WixVariable Id="WixUIBannerBmp" Value="Banner.bmp" />
<WixVariable Id="WixUIDialogBmp" Value="Dialog.bmp" />
```

---

## ?? Troubleshooting

### "WiX Toolset not found"
- Ensure WiX v3.14 is installed from https://wixtoolset.org/releases/
- Check path: `C:\Program Files (x86)\WiX Toolset v3.14`
- Restart Visual Studio

### "Cannot find MGK_Analyzer.exe"
- Build MGK_Analyzer project first: `dotnet build MGK_Analyzer.csproj -c Release`
- Check bin folder for executable

### Installer won't start
- Run as Administrator
- Ensure .NET 8 is installed on target machine
- Check Event Viewer for MSI error logs

### "Access Denied" error
- Run PowerShell/Command Prompt as Administrator
- Check file permissions in output directory

---

## ?? Resources

- [WiX Toolset Documentation](https://wixtoolset.org/documentation/)
- [WiX Tutorial](https://wixtoolset.org/documentation/manual/v3/index.html)
- [MSI Installation Guide](https://docs.microsoft.com/en-us/windows/win32/msi/installation-package)

---

## ? Next Steps

1. ? Install WiX Toolset
2. ? Run build script: `.\build.ps1` or `build.bat`
3. ? Locate MSI file in `bin\Release\`
4. ? Test installer on a clean machine (optional but recommended)
5. ? Distribute to users

---

## ?? Example: Complete Build & Install Process

```powershell
# 1. Open PowerShell as Administrator
# 2. Navigate to project directory
cd C:\Users\ttcho\source\repos\Choppa333\MHK_ANALYZER\MGK_Analyzer.Setup

# 3. Run build script
.\build.ps1

# 4. After successful build, install the application
msiexec /i .\bin\Release\MGK_Analyzer.msi

# 5. Follow the installer wizard
```

---

## ?? Distribution

To distribute your application to users:

1. **Copy the MSI file**
   ```
   MGK_Analyzer.Setup\bin\Release\MGK_Analyzer.msi
   ```

2. **Send to users via:**
   - Email
   - Cloud storage (OneDrive, Google Drive)
   - Web server
   - USB drive

3. **Users run:** Double-click the .msi file

4. **Automatic features:**
   - Dependency checking
   - Installation folder selection
   - Start Menu shortcuts
   - Uninstall support

---

**Built with WiX Toolset v3.14**
**Target: .NET 8 Windows 64-bit**
