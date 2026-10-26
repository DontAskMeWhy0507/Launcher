[Setup]
AppId={{A1A5B86E-0F7D-4B56-8F3F-2F60E8F28B7A}
AppName=Launcher
AppVersion=4.1.1
AppPublisher=Launcher
DefaultDirName={autopf}\Launcher
DefaultGroupName=Launcher
OutputBaseFilename=LauncherSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Launcher.exe
SetupIconFile=Assets\Icons\apps.ico
CloseApplications=yes
RestartApplications=no

[Files]
Source: "dist\Launcher\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Launcher"; Filename: "{app}\Launcher.exe"
Name: "{autodesktop}\Launcher"; Filename: "{app}\Launcher.exe"

[Run]
Filename: "{app}\Launcher.exe"; Description: "Launch Launcher"; Flags: nowait postinstall
