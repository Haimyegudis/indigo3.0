; IndiLogs Suite Installer Script
; Clean version - no bundled sub-installers, no EXE extraction to temp
; Per-user install (no UAC / admin required) - minimizes CrowdStrike EDR threat surface
;
; INSTRUCTIONS:
; 1. Run PrepareFiles.ps1 first to prepare all files
; 2. Then compile this script with Inno Setup

#define MyAppName "IndiLogs Suite"
#define MyAppVersion "1.0.0.6"
#define MyAppPublisher "HP Inc"
#define MyAppExeName "IndiLogs 3.0.exe"

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
OutputBaseFilename=IndiLogsSuite_Setup
SetupIconFile={#IndiLogsIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
; zip compression is less aggressive than lzma - reduces "packed binary" EDR heuristic
Compression=zip
SolidCompression=no
WizardStyle=modern
; lowest = no UAC prompt, no admin escalation needed
; Inno Setup 6 auto-redirects {autopf} to {localappdata}\Programs for non-admin users
PrivilegesRequired=lowest
; dialog = allows IT admins to optionally install for all users (elevates then)
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; IndiLogs application files (already include vcruntime140.dll etc. from PrepareFiles.ps1)
Source: "InstallerFiles\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; libSkiaSharp.dll - explicitly ensure it's in app root
Source: "InstallerFiles\runtimes\win-x64\native\libSkiaSharp.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; SQLite.Interop.dll
Source: "InstallerFiles\SQLite.Interop.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "InstallerFiles\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion skipifsourcedoesntexist
; App icon
Source: "{#IndiLogsIcon}"; DestDir: "{app}"; DestName: "indilogs.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"; Tasks: desktopicon

[Run]
; Launch IndiLogs after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,IndiLogs 3.0}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet481Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 528040); // 528040 = .NET 4.8, 533320 = .NET 4.8.1
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  // Warn if .NET 4.8 is missing - user must install manually
  // We do NOT bundle or run the .NET installer to avoid EDR false positives
  if not IsDotNet481Installed then
  begin
    MsgBox(
      'This application requires .NET Framework 4.8 or later.' + #13#10 + #13#10 +
      'Please download and install it from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net48' + #13#10 + #13#10 +
      'After installing .NET, run this setup again.',
      mbInformation, MB_OK);
    Result := False;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S: String;
begin
  S := '';
  if MemoDirInfo <> '' then S := S + MemoDirInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then S := S + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then S := S + MemoTasksInfo + NewLine + NewLine;
  S := S + 'Components to install:' + NewLine;
  S := S + Space + 'IndiLogs 3.0' + NewLine;
  S := S + Space + '.NET Framework 4.8 (already verified)' + NewLine;
  S := S + Space + 'Visual C++ Runtime DLLs (bundled with app)' + NewLine;
  Result := S;
end;
