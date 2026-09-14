#define MyAppName "LitWeave"
#ifndef MyAppVersion
#define MyAppVersion "0.2.1-beta.1"
#endif
#ifndef MyBuildRoot
#define MyBuildRoot "..\artifacts\releases\v0.2.1-beta.1"
#endif
#define MyAppPublisher "Frank Lai"
#define MyAppExeName "LitWeave.exe"

[Setup]
AppId={{B1E1C17E-983D-4F70-8B62-4B9CCB4BDB11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LitWeave
DefaultGroupName=LitWeave
OutputDir={#MyBuildRoot}
OutputBaseFilename=LitWeave-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
; The first release is unsigned. The README and release notes call out SmartScreen.

[Files]
Source: "{#MyBuildRoot}\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LitWeave"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\LitWeave"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch LitWeave"; Flags: nowait postinstall skipifsilent
