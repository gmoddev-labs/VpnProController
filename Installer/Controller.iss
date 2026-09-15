#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef AppSource
  #define AppSource "..\dist\App"
#endif

[Setup]
AppId={{22E21C69-E596-4923-8A82-678DB8573318}
AppName=VPN Pro Controller
AppVersion={#AppVersion}
AppPublisher=gmoddev
AppPublisherURL=https://github.com/gmoddev/VpnProController
AppUpdatesURL=https://github.com/gmoddev/VpnProController/releases
DefaultDirName={localappdata}\Programs\VpnProController
DefaultGroupName=VPN Pro Controller
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=VpnProController-{#AppVersion}-Setup-x64
Compression=lzma2/fast
LZMANumBlockThreads=2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\VpnPro.Windows.exe
CloseApplications=yes
CloseApplicationsFilter=VpnPro.Windows.exe,VpnPro.Recovery.exe
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: startup; Description: "Launch minimized when I sign in to Windows"; Flags: unchecked
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VPN Pro Controller"; Filename: "{app}\VpnPro.Windows.exe"
Name: "{group}\Uninstall VPN Pro Controller"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VPN Pro Controller"; Filename: "{app}\VpnPro.Windows.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VpnProController"; ValueData: """{app}\VpnPro.Windows.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "VpnProController"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\VpnPro.Windows.exe"; Description: "Launch VPN Pro Controller"; Flags: nowait postinstall skipifsilent unchecked
