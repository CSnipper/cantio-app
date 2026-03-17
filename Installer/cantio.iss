; Cantio — Inno Setup installer script
; Build with: iscc cantio.iss /DAppVersion=1.0.0
; Or use build-installer.ps1 which passes the version automatically.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#define AppName      "Cantio"
#define AppPublisher "CSnipper"
#define AppExeName   "Cantio.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/CSnipper/cantio-app
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=CantioSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
SetupIconFile=..\Cantio\cantio.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
; Application binaries (self-contained publish output)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Seed database — installed only when user picks "sample" on fresh install
Source: "..\Cantio.db"; DestDir: "{app}"; DestName: "cantio.db"; \
        Flags: ignoreversion; Check: ShouldInstallDb

[Icons]
Name: "{group}\{#AppName}";       Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; \
      Description: "Utwórz skrót na &Pulpicie"; \
      GroupDescription: "Dodatkowe ikony:"; \
      Flags: checkedonce

[Run]
Filename: "{app}\{#AppExeName}"; \
          Description: "Uruchom {#AppName}"; \
          Flags: nowait postinstall skipifsilent

[Code]

var
  DbPage: TWizardPage;
  RbSample: TRadioButton;
  RbEmpty:  TRadioButton;
  ShouldInstallSampleDb: Boolean;

{ Returns True when the user's database already exists — skip DB page on reinstall }
function DbAlreadyExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Cantio\cantio.db'));
end;

procedure InitializeWizard;
var
  Lbl: TLabel;
begin
  ShouldInstallSampleDb := True;

  { Skip page creation on reinstall / upgrade }
  if DbAlreadyExists then Exit;

  DbPage := CreateCustomPage(
    wpSelectDir,
    'Baza danych',
    'Wybierz, od czego chcesz zacząć');

  Lbl := TLabel.Create(DbPage);
  Lbl.Caption := 'Zawartość bazy danych po instalacji:';
  Lbl.Parent  := DbPage.Surface;
  Lbl.Top := 8; Lbl.Left := 0;
  Lbl.Width := 450;

  RbSample := TRadioButton.Create(DbPage);
  RbSample.Caption := 'Przykładowa baza — gotowe pieśni dołączone do instalatora (zalecane)';
  RbSample.Parent  := DbPage.Surface;
  RbSample.Top     := 36; RbSample.Left := 0; RbSample.Width := 450;
  RbSample.Checked := True;

  RbEmpty := TRadioButton.Create(DbPage);
  RbEmpty.Caption := 'Pusta baza — zacznij od zera';
  RbEmpty.Parent  := DbPage.Surface;
  RbEmpty.Top     := 64; RbEmpty.Left := 0; RbEmpty.Width := 450;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Read radio selection just before files are installed }
  if (CurPageID = wpInstalling) and (DbPage <> nil) then
    ShouldInstallSampleDb := RbSample.Checked;
end;

{ Called by Inno Setup for the cantio.db [Files] entry }
function ShouldInstallDb: Boolean;
begin
  Result := ShouldInstallSampleDb and not DbAlreadyExists;
end;
