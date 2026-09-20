; ============================================================================
;  Lore -- Natal Charts   ·   Inno Setup script
; ----------------------------------------------------------------------------
;  Turns the self-contained portable build (artifacts\Lore-<ver>-portable\)
;  into a single double-click installer:  LoreSetup-<ver>.exe
;
;  - Per-user install (no admin / no UAC prompt): lands in
;    %LOCALAPPDATA%\Programs\Lore
;  - Creates a Start-menu shortcut and a proper Add/Remove Programs entry.
;  - Nothing to pre-install on the target machine: the build already bundles
;    .NET, the Windows App SDK runtime, sweph.dll, and the ephemeris files.
;
;  Do not run this file directly. Build via:  .\scripts\build-installer.ps1
;  (that script publishes the app and passes the /D defines below).
; ============================================================================

; --- Defines (overridable from the command line via ISCC /D...) -------------
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  ; Path (relative to this .iss) to the published self-contained folder.
  #define SourceDir "..\artifacts\Lore-0.2-portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName    "Lore"
#define AppExeName "Lore.exe"
#define AppPublisher "Lore"

[Setup]
; AppId uniquely identifies the app for upgrades and uninstall -- keep it stable
; across versions. (Generated once; do not change it.)
AppId={{59B8D0CF-4B1F-4DA0-A826-1887F7E2A142}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; Per-user install: no administrator rights, no UAC prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Only makes sense on 64-bit Windows (the app is win-x64).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Wizard / output.
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=LoreSetup-{#AppVersion}
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy the entire published, self-contained folder.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}";                Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";          Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch the app when setup finishes.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
