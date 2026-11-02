; Instalador do Audial. Compile o app em Release x64 antes de gerar:
; .tools\innosetup\ISCC.exe installer\Audial.iss

#define AppName "Audial"
#define AppVersion "8.7.6"
#define AppExe "YtMusicDesktop.exe"
#define BuildDir "..\src\YtMusic.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

[Setup]
AppId={{6F1D3A52-8C47-4E0B-9B7E-2D5A41C7E913}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=AudialSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\YtMusic.App\Assets\AppIcon.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
; A atualização automática instala em silêncio e pede para abrir o app no final.
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait; Check: ShouldRestartApp

[Code]
function ShouldRestartApp: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RESTARTAPP') = 0 then
      Result := True;
end;
