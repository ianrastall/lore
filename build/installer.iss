; Inno Setup script for Lore. Compile through build/build.ps1 so the version and the
; verified publish folder are supplied together.
;
; A per-user install needs no administrator rights: files under
; %LOCALAPPDATA%\Programs\Lore, a Start entry under the app's name and an uninstaller in
; Settings > Apps. An existing install stays where it is and keeps its install mode: the
; AppId is the one every release has used, so 4.0 upgrades 3.x in place, and an
; all-users install asks for elevation as it did before. Saved charts, answers and
; settings live in %LOCALAPPDATA%\Lore and are left alone by install, upgrade and
; uninstall.

#define AppName "Lore"
#define AppExe "Lore.exe"
#ifndef AppVersion
  #error AppVersion must be supplied by build.ps1
#endif

[Setup]
AppId={{59B8D0CF-4B1F-4DA0-A826-1887F7E2A142}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Ian Rastall
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 24H2, as the app and Jupiter.UI require.
MinVersion=10.0.26100
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Lore.App\Assets\AppIcon.ico
WizardStyle=modern
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
; Tell Explorer and Start to refresh icons after install and uninstall, so a reinstall
; does not leave a blank Start menu icon.
ChangesAssociations=yes
OutputDir=..\dist
OutputBaseFilename=Lore-{#AppVersion}-win-x64-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; 3.x kept its shortcuts in a Lore folder; Start now lists the app by name.
Type: files; Name: "{autoprograms}\{#AppName}\{#AppName}.lnk"
Type: files; Name: "{autoprograms}\{#AppName}\Uninstall {#AppName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#AppName}"

[Files]
Source: "..\dist\Lore\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
