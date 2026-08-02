; Cantio — Inno Setup installer script
; Build with: iscc cantio.iss /DAppVersion=1.0
; x86 build:  iscc cantio.iss /DAppVersion=1.0 /DArch=x86
; Or use build-installer.ps1 which passes the version automatically.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#ifndef Arch
  #define Arch "x64"
#endif

#if Arch == "x86"
  #define PublishDir "publish-x86"
  #define ArchSuffix "-x86"
#else
  #define PublishDir "publish"
  #define ArchSuffix ""
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
OutputBaseFilename=CantioSetup-{#AppVersion}{#ArchSuffix}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
SetupIconFile=..\Cantio\cantio.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "polish";  MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
; Strona wyboru trybu pracy (dual / serwer dla CantioPilota)
polish.ModePageTitle=Tryb pracy
polish.ModePageSubtitle=Wybierz, jak będzie używana ta instalacja
polish.ModePageLabel=Sposób obsługi Cantio na tym komputerze:
polish.ModeDual=Dwa ekrany (komputer operatora) — zalecane
polish.ModeDualDesc=Operator siedzi przy komputerze, projekcja idzie na drugi ekran. Tak działa Cantio dotychczas.
polish.ModeServer=Serwer dla CantioPilota (mini PC, jeden ekran)
polish.ModeServerDesc=Ekran pokazuje wyłącznie projekcję, wszystkim steruje tablet z CantioPilotem.
polish.ModeServerNote=W trybie serwerowym Cantio uruchamia się razem z systemem, okno programu jest ukryte, a serwer pilota startuje automatycznie. Powrót do okna: Ctrl+Alt+Shift+C.
english.ModePageTitle=Operating mode
english.ModePageSubtitle=Choose how this installation will be used
english.ModePageLabel=How Cantio will be used on this computer:
english.ModeDual=Two screens (operator computer) — recommended
english.ModeDualDesc=The operator sits at the computer and the projection goes to the second screen. This is how Cantio has worked so far.
english.ModeServer=Server for CantioPilot (mini PC, one screen)
english.ModeServerDesc=The screen shows the projection only; everything is controlled from a tablet running CantioPilot.
english.ModeServerNote=In server mode Cantio starts with Windows, its window stays hidden and the remote server starts automatically. To bring the window back: Ctrl+Alt+Shift+C.
spanish.ModePageTitle=Modo de funcionamiento
spanish.ModePageSubtitle=Elija cómo se usará esta instalación
spanish.ModePageLabel=Cómo se usará Cantio en este equipo:
spanish.ModeDual=Dos pantallas (equipo del operador) — recomendado
spanish.ModeDualDesc=El operador está frente al equipo y la proyección va a la segunda pantalla. Así ha funcionado Cantio hasta ahora.
spanish.ModeServer=Servidor para CantioPilot (mini PC, una pantalla)
spanish.ModeServerDesc=La pantalla muestra solo la proyección; todo se controla desde una tableta con CantioPilot.
spanish.ModeServerNote=En modo servidor Cantio se inicia con Windows, su ventana queda oculta y el servidor del mando se inicia automáticamente. Para recuperar la ventana: Ctrl+Alt+Shift+C.

[Files]
; Application binaries (self-contained publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Seed database — installed only when user picks "sample" on fresh install
Source: "..\Cantio.db"; DestDir: "{app}"; DestName: "cantio.db"; \
        Flags: ignoreversion; Check: ShouldInstallDb

[Icons]
Name: "{group}\{#AppName}";       Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Dirs]
Name: "{localappdata}\Cantio"

[Tasks]
Name: "desktopicon"; \
      Description: "Utwórz skrót na &Pulpicie"; \
      GroupDescription: "Dodatkowe ikony:"; \
      Flags: checkedonce
Name: "autostart"; \
      Description: "Uruchamiaj Cantio przy starcie systemu"; \
      GroupDescription: "Autostart:"; \
      Flags: unchecked

[Registry]
; Autostart: zaznaczony task ALBO tryb serwerowy (mini PC musi wstać sam po zaniku prądu)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
      ValueType: string; ValueName: "Cantio"; \
      ValueData: """{app}\{#AppExeName}"""; \
      Check: ShouldAutostart; Flags: uninsdeletevalue

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
  ModePage: TWizardPage;
  RbModeDual:   TRadioButton;
  RbModeServer: TRadioButton;

{ Returns True when the user's database already exists — skip DB page on reinstall }
function DbAlreadyExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Cantio\cantio.db'));
end;

{ /MODE=server|dual command-line parameter (used by silent installs, and as the
  default radiobutton selection when the wizard page is actually shown) }
function ModeParamIsServer: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:MODE|dual}'), 'server') = 0;
end;

{ Single source of truth for the effective mode: on silent installs (/SILENT,
  /VERYSILENT) the wizard page is never visited, so CurPageChanged never runs —
  the /MODE parameter must be read directly. On interactive installs, the
  radiobutton (pre-set from /MODE, but overridable by the user) wins. }
function EffectiveServerMode: Boolean;
begin
  if WizardSilent then
    Result := ModeParamIsServer
  else if (RbModeServer <> nil) then
    Result := RbModeServer.Checked
  else
    Result := ModeParamIsServer;
end;

procedure InitializeWizard;
var
  Lbl: TLabel;
begin
  ShouldInstallSampleDb := True;

  { Tryb pracy — strona PRZED wyborem bazy danych, pokazywana także przy aktualizacji,
    bo mini PC bywa przekładany z roli operatorskiej i odwrotnie }
  ModePage := CreateCustomPage(
    wpSelectDir,
    ExpandConstant('{cm:ModePageTitle}'),
    ExpandConstant('{cm:ModePageSubtitle}'));

  Lbl := TLabel.Create(ModePage);
  Lbl.Caption := ExpandConstant('{cm:ModePageLabel}');
  Lbl.Parent  := ModePage.Surface;
  Lbl.Top := 8; Lbl.Left := 0;
  Lbl.Width := 450;

  RbModeDual := TRadioButton.Create(ModePage);
  RbModeDual.Caption := ExpandConstant('{cm:ModeDual}');
  RbModeDual.Parent  := ModePage.Surface;
  RbModeDual.Top     := 36; RbModeDual.Left := 0; RbModeDual.Width := 450;
  RbModeDual.Checked := not ModeParamIsServer;

  Lbl := TLabel.Create(ModePage);
  Lbl.Caption := ExpandConstant('{cm:ModeDualDesc}');
  Lbl.Parent  := ModePage.Surface;
  Lbl.Top := 56; Lbl.Left := 20;
  Lbl.Width := 430; Lbl.Height := 32;
  Lbl.WordWrap := True;

  RbModeServer := TRadioButton.Create(ModePage);
  RbModeServer.Caption := ExpandConstant('{cm:ModeServer}');
  RbModeServer.Parent  := ModePage.Surface;
  RbModeServer.Top     := 96; RbModeServer.Left := 0; RbModeServer.Width := 450;
  RbModeServer.Checked := ModeParamIsServer;

  Lbl := TLabel.Create(ModePage);
  Lbl.Caption := ExpandConstant('{cm:ModeServerDesc}');
  Lbl.Parent  := ModePage.Surface;
  Lbl.Top := 116; Lbl.Left := 20;
  Lbl.Width := 430; Lbl.Height := 32;
  Lbl.WordWrap := True;

  Lbl := TLabel.Create(ModePage);
  Lbl.Caption := ExpandConstant('{cm:ModeServerNote}');
  Lbl.Parent  := ModePage.Surface;
  Lbl.Top := 164; Lbl.Left := 0;
  Lbl.Width := 450; Lbl.Height := 48;
  Lbl.WordWrap := True;

  { Skip page creation on reinstall / upgrade }
  if DbAlreadyExists then Exit;

  DbPage := CreateCustomPage(
    ModePage.ID,
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
  { Read radio selections just before files are installed }
  if CurPageID = wpInstalling then
  begin
    if DbPage <> nil then
      ShouldInstallSampleDb := RbSample.Checked;
  end;
end;

{ Autostart: zaznaczony przez użytkownika ALBO wymuszony trybem serwerowym.
  Używa EffectiveServerMode (nie samego CurPageChanged), bo przy /SILENT i
  /VERYSILENT strona kreatora nigdy nie jest odwiedzana. }
function ShouldAutostart: Boolean;
begin
  Result := EffectiveServerMode or WizardIsTaskSelected('autostart');
end;

{ Called by Inno Setup for the cantio.db [Files] entry }
function ShouldInstallDb: Boolean;
begin
  Result := ShouldInstallSampleDb and not DbAlreadyExists;
end;

{ Write initial_lang.cfg / initial_mode.cfg so the app uses the installer choices on first run }
procedure DeinitializeSetup;
var
  LangCode: string;
  LangFile: string;
  ModeCode: string;
  ModeFile: string;
begin
  case ActiveLanguage of
    'english': LangCode := 'en';
    'spanish': LangCode := 'es';
  else LangCode := 'pl';
  end;
  LangFile := ExpandConstant('{localappdata}\Cantio\initial_lang.cfg');
  SaveStringToFile(LangFile, LangCode, False);

  if EffectiveServerMode then ModeCode := 'server' else ModeCode := 'dual';
  ModeFile := ExpandConstant('{localappdata}\Cantio\initial_mode.cfg');
  SaveStringToFile(ModeFile, ModeCode, False);
end;
