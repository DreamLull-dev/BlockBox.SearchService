; Block Box desktop installer (Inno Setup 6+) template.
;
; One UAC installs the app and the global search service; uninstalling the app stops and
; removes the service automatically.
;
; Note: a Microsoft Store MSIX cannot run scripts when it is uninstalled, so Store users
; must remove "Block Box Search Service" from Settings > Apps separately (the install
; script registers that uninstall entry).
;
; Before compiling:
;   1. dotnet publish BlockBox\BlockBox.csproj -c Release -r win-x64 --self-contained true -o publish\app
;   2. .\BlockBox.SearchService\Publish-SearchService.ps1   (outputs to BlockBox.SearchService\publish\win-x64)
;   3. Compile this file with Inno Setup (ISCC.exe BlockBox.iss).

#define AppName "Block Box"
#define AppVersion "1.0.0"
#define AppPublisher "BlockBox"
#define AppExeName "BlockBox.exe"

[Setup]
AppId={{8E3B0B3A-6C7D-4C3B-9A11-BB0000000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\BlockBox
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=BlockBox-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; App (self-contained publish; users do not need the .NET or Windows App SDK runtime)
Source: "..\..\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; Search service (self-contained publish) plus its install/uninstall scripts
Source: "..\..\BlockBox.SearchService\publish\win-x64\*"; DestDir: "{app}\SearchService"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\..\BlockBox.SearchService\Install-SearchService.ps1"; DestDir: "{app}\SearchService"; Flags: ignoreversion
Source: "..\..\BlockBox.SearchService\Uninstall-SearchService.ps1"; DestDir: "{app}\SearchService"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Install the service. The installer already runs elevated, so the script does not prompt again.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\SearchService\Install-SearchService.ps1"" -ServiceExecutable ""{app}\SearchService\BlockBox.SearchService.exe"""; \
  StatusMsg: "Installing the global search service..."; Flags: runhidden waituntilterminated

; Launch the app after installation.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop and remove the search service when the app is uninstalled; the script also removes
; the service's own "Apps & features" entry.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\SearchService\Uninstall-SearchService.ps1"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "BlockBoxSearchService"

[UninstallDelete]
; Fallback cleanup of the install directory (the service script may have removed files already).
Type: filesandordirs; Name: "{app}"
