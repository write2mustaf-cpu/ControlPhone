; ControlPhone — Windows installer (Inno Setup 6)
; Build: ISCC.exe /DAppVersion=3.0.1 packaging\ControlPhone.iss
; Input: dist\ControlPhone (dotnet publish -r win-x64 --self-contained, docs already copied)
; Output: dist\ControlPhone-Setup-v<version>-win64.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "ControlPhone"
#define AppUrl "https://github.com/write2mustaf-cpu/ControlPhone"
#define AppExe "ControlPhone.exe"

[Setup]
; AppId must stay stable across versions so upgrades replace the old install
AppId={{6F1B9A52-3C7E-4E2A-9C1D-2B7A55E0C4D1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ControlPhone
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/blob/main/docs/SUPPORT.md
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\ControlPhone
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=ControlPhone-Setup-v{#AppVersion}-win64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} {#AppVersion} — multi-phone control center

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\ControlPhone\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} — Windows install guide"; Filename: "{app}\docs\WINDOWS-INSTALL.md"
Name: "{group}\{#AppName} — AI assistant guide"; Filename: "{app}\docs\AI-ASSISTANT.md"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; data\ holds the user's settings, device names, groups, AI provider entries and chats: never deleted here
Type: filesandordirs; Name: "{app}\logs"
