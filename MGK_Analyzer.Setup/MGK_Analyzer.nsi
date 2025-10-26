; NSIS installer script for MGK Analyzer
; This file defines the installer for MGK Analyzer application
; Requirements: NSIS 3.x (https://nsis.sourceforge.io/)

; Include Modern UI
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"

; Application Details
!define APPNAME "MGK Analyzer"
!define APPVERSION "1.0.0.0"
!define COMPANYNAME "MGK Analyzer"
!define DESCRIPTION "3D Efficiency Surface Visualization Tool"

; Installer Details
Name "${APPNAME} ${APPVERSION}"
OutFile "MGK_Analyzer-${APPVERSION}-Setup.exe"
InstallDir "$PROGRAMFILES64\${APPNAME}"

; Request admin privileges
RequestExecutionLevel admin

; MUI Settings
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; MUI Language
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Korean"

; Installer sections
Section "Install"
    ; Create install directory
    SetOutPath "$INSTDIR"
    
    ; Copy application files
    ; Note: Adjust these paths based on your build output
    File "..\MGK_Analyzer\bin\Release\net8.0-windows\MGK_Analyzer.exe"
    File "..\MGK_Analyzer\bin\Release\net8.0-windows\*.dll"
    
    ; Create shortcuts
    SetOutPath "$SMPROGRAMS\${APPNAME}"
    CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\MGK_Analyzer.exe"
    CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\uninstall.exe"
    
    ; Desktop shortcut
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\MGK_Analyzer.exe"
    
    ; Create uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"
    
    ; Registry entries
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${APPVERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${COMPANYNAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\MGK_Analyzer.exe"
    
    SetRegView 64
    WriteRegStr HKLM "Software\${COMPANYNAME}\${APPNAME}" "InstallPath" "$INSTDIR"
    WriteRegStr HKLM "Software\${COMPANYNAME}\${APPNAME}" "Version" "${APPVERSION}"
    
SectionEnd

; Uninstaller section
Section "Uninstall"
    ; Remove shortcuts
    RMDir /r "$SMPROGRAMS\${APPNAME}"
    Delete "$DESKTOP\${APPNAME}.lnk"
    
    ; Remove application directory and files
    RMDir /r "$INSTDIR"
    
    ; Remove registry entries
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
    DeleteRegKey HKLM "Software\${COMPANYNAME}\${APPNAME}"
    
SectionEnd

; Functions
Function .onInit
    ; Check if running on 64-bit Windows
    ${If} ${RunningX64}
        SetRegView 64
    ${Else}
        MessageBox MB_OK "This application requires Windows 64-bit"
        Abort
    ${EndIf}
    
    ; Check for .NET 8 (optional - can be added based on requirements)
    ; If .NET is required, add checks here
FunctionEnd

; Installer file compression
SetCompress auto
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUninstDetails show
