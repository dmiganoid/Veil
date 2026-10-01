; Veil installer (Inno Setup 6.6 or later).
; Build it with scripts\build_release.ps1, which publishes the app and passes the defines below.

#if VER < EncodeVer(6, 6, 0)
  #error Inno Setup 6.6 or later is required (WizardStyle=modern dynamic).
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the published Veil folder.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "Veil"
#define AppExeName "Veil.exe"
#define AppPublisher "Veil contributors"
#define AppUrl "https://github.com/dmiganoid/Veil"
#define AutostartTaskFolder "\Veil\"
#define AutostartTaskName "Start Veil at sign-in"

[Setup]
; AppId identifies the installation for upgrades and uninstall. Never change it.
AppId={{8C6E2F3A-5B1D-4E7A-9C42-6F0D3B8A1E57}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (C) Veil contributors
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; Veil runs elevated to create its network adapter, so it is installed per machine into Program Files
; where only administrators can modify the executable.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DefaultDirName={autopf}\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

; Refuse to update files while Veil runs; the app holds this mutex.
AppMutex=Global\Veil.SingleInstance
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=Veil-Setup-{#AppVersion}-win-x64
SetupIconFile=..\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern dynamic
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ShowLanguageDialog=auto
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.AutostartTask=Start Veil in the tray when I sign in to Windows
russian.AutostartTask=Запускать Veil в трее при входе в Windows
english.OtherTasks=Other options:
russian.OtherTasks=Дополнительно:
english.RemoveSettingsPrompt=Also delete your Veil settings?%n%nThis removes the server connection, routing rules and cached GeoIP lists stored in:%n%1
russian.RemoveSettingsPrompt=Удалить также настройки Veil?%n%nБудут удалены параметры подключения к серверу, правила маршрутизации и кэш GeoIP из папки:%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:OtherTasks}"; Flags: unchecked

[InstallDelete]
; Files written by versions before 0.2: the engine config with the VPN password, and the per-user
; copy installed by the old self-extracting archive.
Type: files; Name: "{app}\client\trusttunnel_client.toml"
Type: filesandordirs; Name: "{localappdata}\Programs\Veil"
Type: filesandordirs; Name: "{userprograms}\Veil"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Windows does not start elevated programs from the Run key, so sign-in start uses a scheduled task
; with the highest privileges for the current user. The settings keep it running on battery power,
; without the default 72-hour time limit and at normal priority (the default is below normal, which the
; VPN engine would inherit).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""$u = [Security.Principal.WindowsIdentity]::GetCurrent().Name; $a = New-ScheduledTaskAction -Execute '{app}\{#AppExeName}' -Argument '--tray'; $t = New-ScheduledTaskTrigger -AtLogOn -User $u; $p = New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest; $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -Priority 4; Register-ScheduledTask -TaskPath '{#AutostartTaskFolder}' -TaskName '{#AutostartTaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null"""; \
    Flags: runhidden; Tasks: autostart; StatusMsg: "{cm:AutostartTask}"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AutostartTaskFolder}{#AutostartTaskName}"""; \
    Flags: runhidden; Tasks: not autostart; Check: AutostartTaskExists
; Veil needs elevation; shellexec lets Windows ask for it when Setup started from a standard account.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AutostartTaskFolder}{#AutostartTaskName}"""; \
    Flags: runhidden; RunOnceId: "RemoveAutostartTask"

[UninstallDelete]
Type: files; Name: "{app}\client\trusttunnel_client.toml"
Type: dirifempty; Name: "{app}\client"
Type: dirifempty; Name: "{app}"

[Code]
function AutostartTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "{#AutostartTaskFolder}{#AutostartTaskName}"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Settings are kept by default so that reinstalling Veil keeps the configuration.
  SettingsDir := ExpandConstant('{userappdata}\Veil');
  if UninstallSilent or not DirExists(SettingsDir) then
    Exit;

  if SuppressibleMsgBox(FmtMessage(CustomMessage('RemoveSettingsPrompt'), [SettingsDir]),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(SettingsDir, True, True, True);
end;
