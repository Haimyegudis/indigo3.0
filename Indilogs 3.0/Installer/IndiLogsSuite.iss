; IndiLogs Suite Installer Script
; Inno Setup Script - installs IndiLogs 3.0 and IndiChart.UI together
;
; INSTRUCTIONS:
; 1. Run PrepareFiles.ps1 first to prepare all files
; 2. Then compile this script with Inno Setup

#define MyAppName "IndiLogs Suite"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HP Inc"
#define MyAppExeName "IndiLogs 3.0.exe"
#define ChartAppExeName "IndiChart.UI.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{8F4E6A32-9B5C-4D7E-A8C1-3F6B2E9D1A4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=IndiLogsSuite_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associateCsv"; Description: "Associate .csv files with IndiChart Viewer"; GroupDescription: "File Associations:"; Flags: unchecked

[Files]
; All prepared files (IndiLogs + IndiChart combined)
; Run PrepareFiles.ps1 first to create the InstallerFiles folder
Source: "InstallerFiles\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\IndiChart Viewer"; Filename: "{app}\{#ChartAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autodesktop}\IndiChart Viewer"; Filename: "{app}\{#ChartAppExeName}"; Tasks: desktopicon

[Registry]
; Optional: Associate .csv files with IndiChart viewer
Root: HKCR; Subkey: ".indichart"; ValueType: string; ValueName: ""; ValueData: "IndiChartFile"; Flags: uninsdeletevalue; Tasks: associateCsv
Root: HKCR; Subkey: "IndiChartFile"; ValueType: string; ValueName: ""; ValueData: "IndiChart Data File"; Flags: uninsdeletekey; Tasks: associateCsv
Root: HKCR; Subkey: "IndiChartFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#ChartAppExeName},0"; Tasks: associateCsv
Root: HKCR; Subkey: "IndiChartFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ChartAppExeName}"" ""%1"""; Tasks: associateCsv

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,IndiLogs 3.0}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Any post-installation steps can go here
  end;
end;
