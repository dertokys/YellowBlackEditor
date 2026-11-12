#ifndef MyAppVersion
  #define MyAppVersion "1.4.2"
#endif
#ifndef SourceDir
  #define SourceDir "..\bin\publish\win-x64"
#endif

#define MyAppName "Yellow Black Notes"
#define MyAppExeName "YellowBlackEditor.exe"

[Setup]
AppId={{E8D01E3E-1E31-4C2A-9AAB-6F6B2E6E0A9F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={commonpf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=YellowBlackNotes-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\App.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
AppMutex=YellowBlackNotes.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"

[InstallDelete]
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
