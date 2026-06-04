#define MyAppName "Music Widget"
#define MyAppPublisher "Wayan123"
#define MyAppURL "https://github.com/Wayan123/Simple-Music-Widget"
#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\\artifacts\\publish\\win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\\dist"
#endif

[Setup]
AppId={{7EBC8D8C-3F21-4B36-9F5B-6D0B6F2D1484}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases/latest
DefaultDirName={localappdata}\Programs\MusicWidget
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=MusicWidgetSetup-{#MyAppVersion}-win-x64
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\MusicWidget.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Music Widget when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Music Widget"; Filename: "{app}\MusicWidget.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Music Widget"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Music Widget"; Filename: "{app}\MusicWidget.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Music Widget"; Filename: "{app}\MusicWidget.exe"; Parameters: "--autostart"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\MusicWidget.exe"; Description: "Launch Music Widget"; Flags: nowait postinstall skipifsilent
