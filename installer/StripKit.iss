; ============================================================================
;  StripKit — Inno Setup script
;  Built by scripts/Invoke-Release.ps1 (which bumps MyAppVersion in lockstep
;  with the .csproj and CHANGELOG). Packages the self-contained publish in
;  ..\publish into a per-user installer with a desktop-icon option, a Start-Menu
;  group, a chooseable install directory, and a registry-wiping uninstaller.
; ============================================================================

#define MyAppName "StripKit"
#define MyAppVersion "1.6.0"
#define MyAppPublisher "VybeCode Software"
#define MyAppURL "https://stripkit.pro"
#define MyAppExeName "StripKit.exe"

[Setup]
; A fixed AppId keeps upgrades/uninstalls tied to the same product across versions.
AppId={{B2E9B0A1-5C3D-4E7A-9F12-6A4D8C0E1F23}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableWelcomePage=no
OutputDir=Output
OutputBaseFilename=StripKit-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\StripKit\Assets\stripkit.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; ── Code signing note ────────────────────────────────────────────────────────
; StripKit.exe is signed BEFORE packaging (in Invoke-Release.ps1).
; The produced installer exe is signed AFTER packaging (also in the script).
; We do not use Inno's SignTool= directive because Azure Trusted Signing
; requires signtool.exe + a dlib + a metadata JSON, which Inno cannot invoke
; directly. The uninstaller embedded inside is NOT separately signed.
; ─────────────────────────────────────────────────────────────────────────────
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; The only registry footprint, removed in full on uninstall (the uninstaller also
; removes Inno's own uninstall key automatically — so nothing is left behind).
Root: HKA; Subkey: "Software\VybeCode\StripKit"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\VybeCode"; Flags: uninsdeletekeyifempty

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
