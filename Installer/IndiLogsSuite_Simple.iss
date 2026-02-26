; IndiLogs Suite Installer Script - Simple Version
; Without automatic downloads - should avoid antivirus false positives

#define MyAppName "IndiLogs Suite"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HP Inc"
#define MyAppExeName "IndiLogs 3.0.exe"
#define FlowCsvViewerInstaller "Flow CSV Viewer Installer.exe"

; Icon paths
#define IndiLogsIcon "..\Indilogs 3.0\Resources\indilogs.ico"

[Setup]
AppId={{8F4E6A32-9B5C-4D7E-A8C1-3F6B2E9D1A4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=IndiLogsSuite_Setup_Simple
SetupIconFile={#IndiLogsIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All prepared files (IndiLogs only)
Source: "InstallerFiles\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Copy libSkiaSharp.dll to root folder
Source: "InstallerFiles\runtimes\win-x64\native\libSkiaSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
; Copy SQLite.Interop.dll
Source: "InstallerFiles\SQLite.Interop.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "InstallerFiles\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion skipifsourcedoesntexist
; Copy icon file for shortcuts
Source: "{#IndiLogsIcon}"; DestDir: "{app}"; DestName: "indilogs.ico"; Flags: ignoreversion
; Flow CSV Viewer installer - copied to temp for silent execution during install
Source: "{#FlowCsvViewerInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"; Tasks: desktopicon

[Run]
; Install Flow CSV Viewer silently first
Filename: "{tmp}\{#FlowCsvViewerInstaller}"; Parameters: "/SILENT"; StatusMsg: "Installing Flow CSV Viewer..."; Flags: waituntilterminated
; Then launch IndiLogs
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,IndiLogs 3.0}"; Flags: nowait postinstall skipifsilent
