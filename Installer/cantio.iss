#define MyAppName "Cantio"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Cantio"
#define MyAppExeName "Cantio.exe"
#define MyAppDir "..\Cantio\bin\Publish\win-x64"
#define MyDbFile "..\cantio.db"

[Setup]
AppId={{F3A7B2C1-4D5E-4F6A-8B9C-0D1E2F3A4B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Cantio-{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe ikony:"

[Files]
; Pliki aplikacji
Source: "{#MyAppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Baza danych z pieśniami — instalowana warunkowo (jeśli user wybrał pełną)
Source: "{#MyDbFile}"; DestDir: "{app}"; DestName: "cantio.db"; Flags: ignoreversion; Check: InstallFullDb

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Uruchom {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DbPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  DbPage := CreateInputOptionPage(
    wpSelectDir,
    'Baza danych pieśni',
    'Wybierz, z jaką bazą danych chcesz zainstalować program',
    'Program przechowuje bazę danych w folderze użytkownika (%LOCALAPPDATA%\Cantio).' + #13#10 +
    'Przy pierwszym uruchomieniu zostanie ona zainicjowana automatycznie.' + #13#10#13#10 +
    'Wybierz opcję:',
    True,
    False
  );
  DbPage.Add('Zainstaluj z gotową bazą pieśni (zalecane — zawiera przykładowy repertuar)');
  DbPage.Add('Zainstaluj z pustą bazą danych (zacznij od zera)');
  DbPage.Values[0] := True;
end;

function InstallFullDb: Boolean;
begin
  Result := DbPage.Values[0];
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
end;
