; Installer for Zarpazo, built with Inno Setup 6 by tools\build-installer.ps1 (it passes /DAppVersion).
; Per-user install: no admin rights, and everything lands in the person's own profile.
;
; tools\test-update.ps1 builds throw-away installers with /DTestBuild=1 and its own id, folder and mutex, so the whole update can be
; tried without touching the real installation (no shortcuts, no Run entry, a different uninstall key). Normal builds pass none of it.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
; Never change the default id: it is how Windows knows later versions replace this one. The doubled brace is Inno's escape for "{".
#ifndef AppIdValue
  #define AppIdValue "{{6F0B6A52-3C1D-4E8A-9B47-5D2A0C7E91F4}"
#endif
#ifndef AppDisplayName
  #define AppDisplayName "Zarpazo"
#endif
; The same mutex the app takes for its single instance: setup asks to close a running Zarpazo before replacing it.
#ifndef AppMutexName
  #define AppMutexName "Local\Zarpazo.SingleInstance"
#endif
#ifndef InstallDirDefault
  #define InstallDirDefault "{autopf}\Zarpazo"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\Zarpazo.exe"
#endif
#ifndef OutDir
  #define OutDir "..\publish"
#endif

[Setup]
AppId={#AppIdValue}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppPublisher=provisorio43
DefaultDirName={#InstallDirDefault}
DefaultGroupName=Zarpazo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Zarpazo\Assets\Zarpazo.ico
UninstallDisplayIcon={app}\Zarpazo.exe
OutputDir={#OutDir}
OutputBaseFilename=Zarpazo-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex={#AppMutexName}
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

#ifndef TestBuild
[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el &escritorio"; Flags: unchecked
Name: "startup"; Description: "Iniciar Zarpazo con &Windows"
#endif

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Zarpazo.exe"; Flags: ignoreversion

#ifndef TestBuild
[Icons]
; The Start menu entry is what makes Zarpazo show up when searching from the taskbar.
Name: "{autoprograms}\Zarpazo"; Filename: "{app}\Zarpazo.exe"
Name: "{autodesktop}\Zarpazo"; Filename: "{app}\Zarpazo.exe"; Tasks: desktopicon

[Registry]
; The same Run entry the app's own "Iniciar con Windows" setting writes, so that setting shows as on after installing.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Zarpazo"; \
  ValueData: """{app}\Zarpazo.exe"""; Flags: uninsdeletevalue; Tasks: startup
; Also removed when the person turned it on from inside the app instead.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Zarpazo"; \
  Flags: dontcreatekey uninsdeletevalue
#endif

[Run]
Filename: "{app}\Zarpazo.exe"; Description: "Abrir Zarpazo"; Flags: nowait postinstall skipifsilent
; An update started from inside the app runs this setup silently with /RELAUNCH=1 and expects Zarpazo to come back.
Filename: "{app}\Zarpazo.exe"; Flags: nowait skipifnotsilent; Check: ShouldRelaunch

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;
