#ifndef AppVersion
  #error AppVersion must be passed with /DAppVersion=...
#endif

#define AppName "GamePivot"
#define AppExeName "GamePivot.exe"

[Setup]
AppId={{29F56DA0-6269-41B2-A1A8-2F74F726802F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=The Nexus Pivot
DefaultDirName={localappdata}\Programs\GamePivot
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputDir=..\dist
OutputBaseFilename=GamePivot-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\GamePivot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\rules.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\GamePivot"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\GamePivot"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
