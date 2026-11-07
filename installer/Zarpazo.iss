; Installer for Zarpazo, built with Inno Setup 6 by tools\build-installer.ps1 (it passes /DAppVersion).
; Per-user install: no admin rights, and everything lands in the person's own profile.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
; Never change this id: it is how Windows knows later versions replace this one.
AppId={{6F0B6A52-3C1D-4E8A-9B47-5D2A0C7E91F4}
AppName=Zarpazo
AppVersion={#AppVersion}
AppPublisher=provisorio43
DefaultDirName={autopf}\Zarpazo
DefaultGroupName=Zarpazo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Zarpazo\Assets\Zarpazo.ico
UninstallDisplayIcon={app}\Zarpazo.exe
OutputDir=..\publish
OutputBaseFilename=Zarpazo-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The same mutex the app takes for its single instance: setup asks to close a running Zarpazo before replacing it.
AppMutex=Local\Zarpazo.SingleInstance
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el &escritorio"; Flags: unchecked
Name: "startup"; Description: "Iniciar Zarpazo con &Windows"; Flags: unchecked

[Files]
Source: "..\publish\Zarpazo.exe"; DestDir: "{app}"; Flags: ignoreversion

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

[Run]
Filename: "{app}\Zarpazo.exe"; Description: "Abrir Zarpazo"; Flags: nowait postinstall skipifsilent
