#ifndef SourceDir
  #error SourceDir must be provided. Example: ISCC.exe /DSourceDir=publish\installer-input LoxTools.iss
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "LoxTools"
#define AppPublisher "Xusr404"
#define AppExeName "LoxTools.exe"
#define InstallDirectoryName "LoxTools"

[Setup]
AppId={{5F63C818-1F89-4FD7-9F63-23D5FD07BE58}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#InstallDirectoryName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\LoxTools\Resources\LoxTools.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.LaunchApp=Launch LoxTools
german.LaunchApp=LoxTools starten

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Flags: nowait skipifnotsilent
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  SynchronizeAccess = $00100000;

function OpenProcess(DesiredAccess: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

procedure WaitForUpdaterExit;
var
  ProcessId: Integer;
  ProcessHandle: THandle;
begin
  ProcessId := StrToIntDef(ExpandConstant('{param:LoxToolsPid|0}'), 0);
  if ProcessId <= 0 then
    Exit;

  ProcessHandle := OpenProcess(SynchronizeAccess, False, ProcessId);
  if ProcessHandle <> 0 then
  begin
    WaitForSingleObject(ProcessHandle, 30000);
    CloseHandle(ProcessHandle);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  WaitForUpdaterExit;
  Result := '';
end;
