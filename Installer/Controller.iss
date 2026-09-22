#ifndef AppVersion
  #define AppVersion "0.5.1"
#endif
#ifndef AppSource
  #define AppSource "..\dist\FrameworkApp"
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
SetupIconFile=..\Windows\Assets\Connected.ico
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

[InstallDelete]
; Old self-contained hostfxr forces app-local framework lookup even with shared runtimeconfig.
Type: files; Name: "{app}\hostfxr.dll"
Type: files; Name: "{app}\hostpolicy.dll"
Type: files; Name: "{app}\Recovery\hostfxr.dll"
Type: files; Name: "{app}\Recovery\hostpolicy.dll"

[Icons]
Name: "{group}\VPN Pro Controller"; Filename: "{app}\VpnPro.Windows.exe"; IconFilename: "{app}\Assets\Connected.ico"
Name: "{group}\Uninstall VPN Pro Controller"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VPN Pro Controller"; Filename: "{app}\VpnPro.Windows.exe"; IconFilename: "{app}\Assets\Connected.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VpnProController"; ValueData: """{app}\VpnPro.Windows.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "VpnProController"; Tasks: not startup; Flags: deletevalue

[Run]
Filename: "{app}\VpnPro.Windows.exe"; Description: "Launch VPN Pro Controller"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function HasNetRuntimeInView(RootKey: Integer): Boolean;
var
  Versions: TArrayOfString;
  Index, Patch: Integer;
begin
  Result := False;
  if not RegGetValueNames(RootKey, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App', Versions) then Exit;
  for Index := 0 to GetArrayLength(Versions) - 1 do
    if Copy(Versions[Index], 1, 5) = '10.0.' then
    begin
      Patch := StrToIntDef(Copy(Versions[Index], 6, MaxInt), -1);
      if Patch >= 0 then begin Result := True; Exit; end;
    end;
end;

function HasNetRuntime: Boolean;
begin
  { The x64 runtime is commonly registered in the 32-bit registry view. }
  Result := HasNetRuntimeInView(HKLM32) or HasNetRuntimeInView(HKLM64);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasNetRuntime then
  begin
    Log('[VPNPro:Installer] Compatible x64 .NET 10 runtime found; skipping download.');
    Exit;
  end;
  try
    Log('[VPNPro:Installer] Downloading Microsoft x64 .NET 10 runtime.');
    DownloadTemporaryFile(
      'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.12/dotnet-runtime-10.0.12-win-x64.exe',
      'dotnet-runtime.exe', '02ea072c08f890f9dc0d3ac71b5fd4c2bfcdced4e1133e2d917ce5422b025585', nil);
    if not ShellExec('runas', ExpandConstant('{tmp}\dotnet-runtime.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'The .NET runtime installation was cancelled or could not start. Run Setup again to retry.'
    else if (ExitCode <> 0) and (ExitCode <> 3010) then
      Result := Format('Microsoft .NET runtime installation failed (code %d). Run Setup again to retry.', [ExitCode])
    else if not HasNetRuntime then
      Result := 'The required x64 .NET 10 runtime was not detected after installation. Restart Windows if requested, then run Setup again.'
    else if ExitCode = 3010 then NeedsRestart := True;
  except
    Result := 'Could not download or install the required .NET runtime. Check your internet connection and retry. ' + GetExceptionMessage;
  end;
end;

procedure SHChangeNotify(EventId: Integer; Flags: Cardinal; Item1: string; Item2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
var
  Shell, Link: Variant;
  PinPath: string;
begin
  if CurStep <> ssPostInstall then Exit;
  { Repair only this application's existing pin. Do not unpin or rebuild Explorer caches. }
  PinPath := ExpandConstant('{userappdata}\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\VPN Pro Controller.lnk');
  if not FileExists(PinPath) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Link := Shell.CreateShortcut(PinPath);
    if CompareText(Link.TargetPath, ExpandConstant('{app}\VpnPro.Windows.exe')) = 0 then
    begin
      Link.IconLocation := ExpandConstant('{app}\Assets\Connected.ico,0');
      Link.Save;
      SHChangeNotify($2000, $1005, PinPath, 0);
      Log('[VPNPro:Installer] Updated existing taskbar pin icon.');
    end;
  except
    Log('[VPNPro:Installer] Could not update taskbar pin: ' + GetExceptionMessage);
  end;
end;
