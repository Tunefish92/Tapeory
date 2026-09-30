; Tapeory's Windows setup (Inno Setup 6): installs for the current user only, so it needs no
; administrator rights. The app and engine come from the folder given as SourceDir; the data in
; %LOCALAPPDATA%\Tapeory stays when Tapeory is updated or uninstalled.
;
;   iscc /DAppVersion=1.2.3 /DSourceDir=C:\build\Tapeory /DOutputDir=C:\dist tapeory.iss
;
; Updating from Settings → About runs this setup with /SILENT; it then starts Tapeory again.

#ifndef AppVersion
  #error AppVersion is required (/DAppVersion=1.2.3)
#endif
#ifndef SourceDir
  #error SourceDir is required (/DSourceDir=...)
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6E0C4B7A-3F58-4E7B-9A2D-5B1E8C3F7D21}
AppName=Tapeory
AppVersion={#AppVersion}
AppVerName=Tapeory {#AppVersion}
AppPublisher=Tunefish
; The setup's own version details (Explorer → Properties → Details).
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Tapeory
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=Tunefish
VersionInfoCopyright=Copyright (c) 2026 Tunefish, MIT License
AppPublisherURL=https://github.com/Tunefish92/Tapeory
AppSupportURL=https://github.com/Tunefish92/Tapeory/issues
AppUpdatesURL=https://github.com/Tunefish92/Tapeory/releases
DefaultDirName={localappdata}\Programs\Tapeory
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=Tapeory-{#AppVersion}-windows-x64-setup
SetupIconFile=tapeory.ico
UninstallDisplayIcon={app}\tapeory.exe
UninstallDisplayName=Tapeory
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An update brings a complete engine: files the new version no longer has must go.
Type: filesandordirs; Name: "{app}\engine"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\Tapeory"; Filename: "{app}\tapeory.exe"
Name: "{userdesktop}\Tapeory"; Filename: "{app}\tapeory.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\tapeory.exe"; Description: "{cm:LaunchProgram,Tapeory}"; Flags: nowait postinstall
