; Freezia installer (Inno Setup 6.3+ / 7)
; Build with build.ps1, or:
;   ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\artifacts\publish installer\Freezia.iss

#define AppName "Freezia"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#define AppExe "Freezia.exe"
; Must match AppInfo.MutexName in src/Freezia/Core/AppInfo.cs
#define AppMutexName "Freezia-6F1C2B7A-3D4E-4C2B-9A51-8E2D7B1F0C63"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
; Fixed AppId: newer versions upgrade the existing installation in place.
AppId={{8E3F1A6C-2B9D-4E57-A1C4-7D0B3F9E2A58}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; Let the user pick the folder, and "all users" vs "just me".
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Fallback only: [Code] below asks a running Freezia to quit before (un)installing.
AppMutex={#AppMutexName}
CloseApplications=no

OutputDir=..\artifacts
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\Freezia\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
; Tell the shell to refresh icons after (un)install, so an upgrade with a new icon
; does not keep showing the old one from the icon cache (taskbar, Start, shortcuts).
ChangesAssociations=yes
ShowLanguageDialog=auto

[Languages]
Name: "thai"; MessagesFile: "compiler:Languages\Thai.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
thai.StartWithWindows=เปิด Freezia อัตโนมัติเมื่อเข้าสู่ Windows
english.StartWithWindows=Start Freezia when I sign in to Windows
thai.OtherOptions=ตัวเลือกเพิ่มเติม:
english.OtherOptions=Other options:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:OtherOptions}"; Flags: unchecked

[Files]
; Self-contained single-file build: no .NET runtime needed on the target PC.
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's "start with Windows" setting writes (per user).
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue; Tasks: startup

; Language picked in Setup becomes the app's language on first run (removed on uninstall).
Root: HKCU; Subkey: "Software\{#AppName}"; ValueType: string; ValueName: "InstallerLanguage"; ValueData: "{language}";     Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; settings.json, logs, and the native libraries .NET extracts from the single-file exe
Type: filesandordirs; Name: "{userappdata}\{#AppName}"
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
Type: filesandordirs; Name: "{%TEMP|{localappdata}\Temp}\.net\{#AppName}"
Type: dirifempty; Name: "{app}"

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ Ask a running Freezia (same user session) to exit cleanly, so its tray icon is removed.
  Falls back to taskkill if it does not quit within a few seconds. }
procedure CloseRunningApp();
var
  H: THandle;
  I, ResultCode: Integer;
begin
  if not CheckForMutexes('{#AppMutexName}') then
    Exit;

  H := OpenEvent(EVENT_MODIFY_STATE, False, '{#AppMutexName}-exit');
  if H <> 0 then
  begin
    SetEvent(H);
    CloseHandle(H);
  end;

  for I := 1 to 50 do
  begin
    if not CheckForMutexes('{#AppMutexName}') then
      Exit;
    Sleep(100);
  end;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function InitializeSetup(): Boolean;
begin
  CloseRunningApp();
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The Run value may have been created later by the app's own setting. }
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#AppName}');
  end;
end;
