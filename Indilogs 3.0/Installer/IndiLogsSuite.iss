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

; .NET 8 Desktop Runtime download URL (x64)
#define DotNet8Url "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.23/windowsdesktop-runtime-8.0.23-win-x64.exe"
#define DotNet8InstallerName "windowsdesktop-runtime-8.0.23-win-x64.exe"

; Icon paths (both icons are in Resources folder)
#define IndiLogsIcon "..\Resources\indilogs.ico"
#define IndiChartIcon "..\Resources\indichart.ico"

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
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All prepared files (IndiLogs + IndiChart combined)
Source: "InstallerFiles\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Copy libSkiaSharp.dll to root folder for .NET runtime to find it
Source: "InstallerFiles\runtimes\win-x64\native\libSkiaSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
; Copy SQLite.Interop.dll to root folder for SQLite to work
Source: "InstallerFiles\SQLite.Interop.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "InstallerFiles\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion skipifsourcedoesntexist
; Copy icon files for shortcuts
Source: "{#IndiLogsIcon}"; DestDir: "{app}"; DestName: "indilogs.ico"; Flags: ignoreversion
Source: "{#IndiChartIcon}"; DestDir: "{app}"; DestName: "indichart.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"
Name: "{group}\IndiChart Viewer"; Filename: "{app}\{#ChartAppExeName}"; IconFilename: "{app}\indichart.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\IndiLogs 3.0"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\indilogs.ico"; Tasks: desktopicon
Name: "{autodesktop}\IndiChart Viewer"; Filename: "{app}\{#ChartAppExeName}"; IconFilename: "{app}\indichart.ico"; Tasks: desktopicon

[Registry]
; Associate .csv files with IndiChart Viewer (Open With option)
Root: HKCR; Subkey: ".csv\OpenWithProgids"; ValueType: string; ValueName: "IndiChart.CSV"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "IndiChart.CSV"; ValueType: string; ValueName: ""; ValueData: "IndiChart CSV File"; Flags: uninsdeletekey
Root: HKCR; Subkey: "IndiChart.CSV\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\indichart.ico"
Root: HKCR; Subkey: "IndiChart.CSV\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ChartAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,IndiLogs 3.0}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  DotNetNeeded: Boolean;

function IsDotNet8DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
  RuntimePath: String;
begin
  Result := False;
  // Check for .NET 8 Desktop Runtime in registry
  RuntimePath := ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*');
  if FindFirst(RuntimePath, FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Successfully downloaded file to {tmp}: %s', [FileName]));
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if CurPageID = wpReady then
  begin
    // Check if .NET 8 Desktop Runtime is installed
    DotNetNeeded := not IsDotNet8DesktopInstalled;

    if DotNetNeeded then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('{#DotNet8Url}', '{#DotNet8InstallerName}', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;

          // Install .NET 8 Desktop Runtime silently
          DownloadPage.SetText('Installing .NET 8 Desktop Runtime...', 'Please wait...');
          if not Exec(ExpandConstant('{tmp}\{#DotNet8InstallerName}'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          begin
            MsgBox('Failed to install .NET 8 Desktop Runtime. Error code: ' + IntToStr(ResultCode) + #13#10 +
                   'IndiChart Viewer may not work correctly.', mbError, MB_OK);
          end;

          Result := True;
        except
          if DownloadPage.AbortedByUser then
          begin
            Log('Download aborted by user.');
            Result := False;
          end
          else
          begin
            MsgBox('Failed to download .NET 8 Desktop Runtime: ' + GetExceptionMessage + #13#10 +
                   'IndiChart Viewer may not work correctly.' + #13#10 + #13#10 +
                   'You can manually download it from:' + #13#10 +
                   'https://dotnet.microsoft.com/download/dotnet/8.0', mbError, MB_OK);
            Result := True; // Continue anyway
          end;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S: String;
begin
  S := '';

  if MemoDirInfo <> '' then
    S := S + MemoDirInfo + NewLine + NewLine;

  if MemoGroupInfo <> '' then
    S := S + MemoGroupInfo + NewLine + NewLine;

  if MemoTasksInfo <> '' then
    S := S + MemoTasksInfo + NewLine + NewLine;

  // Check .NET status
  if not IsDotNet8DesktopInstalled then
    S := S + 'Additional downloads:' + NewLine + Space + '.NET 8 Desktop Runtime (required for IndiChart Viewer)' + NewLine + NewLine;

  Result := S;
end;
