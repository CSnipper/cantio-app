# Installer (Inno Setup) — Design Document

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a single `CantioSetup-x.y.z.exe` file ready for direct download distribution, built from a PowerShell script with one command.

**Architecture:** `dotnet publish` (self-contained, win-x64, directory output) → Inno Setup compiler reads the publish directory and produces a signed-ready `.exe` installer. Version is defined once in `Cantio.csproj` and flows through to the installer filename and Add/Remove Programs entry.

**Tech Stack:** .NET 10 publish, Inno Setup 6, PowerShell 5+

---

## Scope

- Build script: `Installer/build-installer.ps1`
- Inno Setup script: `Installer/cantio.iss`
- Version property added to `Cantio/Cantio.csproj`
- No changes to application code

Out of scope: code signing, auto-update, CI/CD pipeline.

---

## Publish Configuration

| Property | Value |
|---|---|
| Configuration | Release |
| Runtime Identifier | win-x64 |
| Self-contained | true |
| PublishSingleFile | false (directory output) |
| Output (explicit `-o`) | `Installer/publish/` |

Self-contained ensures users need no .NET runtime installed. Directory output (not single-file) is required for reliable native SQLite extraction and WPF resource loading. The output path is pinned via `-o Installer/publish` in the build script so it is independent of MSBuild defaults and publish profiles.

---

## Installer Behaviour

| Property | Value |
|---|---|
| Install mode | Per-machine (requires admin) |
| Default install dir | `{autopf}\Cantio` (`C:\Program Files\Cantio`) |
| User data dir | `%LocalAppData%\Cantio\` — **not touched** by installer or uninstaller |
| Start Menu shortcut | Yes, always |
| Desktop shortcut | Optional checkbox during install (default: checked) |
| Uninstaller | Registered in Add/Remove Programs |
| App icon | `cantio.ico` (already in project) |

The database lives in `%LocalAppData%\Cantio\cantio.db` (set in `App.xaml.cs`). Reinstalling or upgrading does not affect user data.

---

## Database Selection Page

Inno Setup adds a custom wizard page (Pascal Script, radio buttons) that asks:

> **Baza danych**
> ○ Pusta baza — zacznij od zera
> ● Przykładowa baza — gotowe pieśni dołączone do instalatora *(domyślnie)*

This page only appears when `%LocalAppData%\Cantio\cantio.db` does **not** exist (fresh install). On reinstall/upgrade the page is skipped automatically.

**Seed database source:** `Cantio.db` at the repository root. The build script verifies the file exists before calling `iscc` and aborts with a clear error if not. Included as a separate `[Files]` entry (NOT via publish wildcard), installed conditionally to `{app}\cantio.db`.

- If user picks **sample**: `cantio.db` installed to `{app}` → `App.xaml.cs` copies it to `%LocalAppData%\Cantio\` on first launch (existing logic, unchanged).
- If user picks **empty**: `cantio.db` NOT installed → `App.xaml.cs` runs EF migrations on fresh DB (existing logic, unchanged).

**Full Pascal Script (`[Code]` section):**

```pascal
var
  DbPage: TWizardPage;
  RbSample: TRadioButton;
  RbEmpty: TRadioButton;
  ShouldInstallSampleDb: Boolean;

function DbAlreadyExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Cantio\cantio.db'));
end;

procedure InitializeWizard;
var
  Lbl: TLabel;
begin
  ShouldInstallSampleDb := True;
  if DbAlreadyExists then Exit;

  DbPage := CreateCustomPage(wpSelectDir,
    'Baza danych', 'Wybierz, od czego chcesz zacząć');

  Lbl := TLabel.Create(DbPage);
  Lbl.Caption := 'Baza danych:';
  Lbl.Parent := DbPage.Surface;
  Lbl.Top := 8; Lbl.Left := 0;

  RbSample := TRadioButton.Create(DbPage);
  RbSample.Caption := 'Przykładowa baza — gotowe pieśni dołączone do instalatora';
  RbSample.Parent := DbPage.Surface;
  RbSample.Top := 32; RbSample.Left := 0; RbSample.Width := 450;
  RbSample.Checked := True;

  RbEmpty := TRadioButton.Create(DbPage);
  RbEmpty.Caption := 'Pusta baza — zacznij od zera';
  RbEmpty.Parent := DbPage.Surface;
  RbEmpty.Top := 60; RbEmpty.Left := 0; RbEmpty.Width := 450;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpInstalling) and (DbPage <> nil) then
    ShouldInstallSampleDb := RbSample.Checked;
end;

function ShouldInstallDb: Boolean;
begin
  Result := ShouldInstallSampleDb and not DbAlreadyExists;
end;
```

`DbPage = nil` guard in `CurPageChanged` prevents nil-reference when DB already exists and the page was never created.

The `[Files]` entry for `cantio.db` uses `Check: ShouldInstallDb`.

No changes to application code required. App shortcut icons served from EXE embedded resources, not standalone `.ico`.

---

## Version Management

Add to the main (unconditional) `<PropertyGroup>` in `Cantio/Cantio.csproj`:

```xml
<Version>1.0.0</Version>
```

This also sets `AssemblyVersion` and `FileVersion` automatically (desired for distribution). `build-installer.ps1` reads this value with an XPath query and passes it to `iscc.exe` via `/DAppVersion=x.y.z`, so the installer filename and Add/Remove Programs entry always match the csproj version. No manual version maintenance in two places.

---

## File Layout

```
Installer/
├── cantio.iss            # Inno Setup script
├── build-installer.ps1   # Build script (publish + iscc)
├── publish/              # dotnet publish output (gitignored)
└── Output/               # Generated installer EXEs (gitignored)
    └── CantioSetup-1.0.0.exe
```

`Installer/Output/` was already in `.gitignore`. `Installer/publish/` is added to `.gitignore` as part of this task (the existing top-level `publish/` rule does not cover nested paths).

---

## Build Script Logic (`build-installer.ps1`)

```
1. Read <Version> from Cantio/Cantio.csproj via [xml]
2. Verify Cantio.db exists at repo root — abort with error if not
3. dotnet publish Cantio/Cantio.csproj -c Release -r win-x64 --self-contained true -o Installer/publish
4. iscc.exe Installer/cantio.iss /DAppVersion=$version
5. Print path to output file: Installer/Output/CantioSetup-$version.exe
```

Fails fast (exit code propagation) if any step fails.

---

## Inno Setup Script (`cantio.iss`) — Key Sections

```iss
#define AppVersion GetEnv("AppVersion")   ; passed via /D flag
#define AppName "Cantio"
#define AppPublisher "CSnipper"
#define AppExeName "Cantio.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=CantioSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
SetupIconFile=..\Cantio\cantio.ico

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\Cantio.db"; DestDir: "{app}"; DestName: "cantio.db"; \
        Flags: ignoreversion; Check: ShouldInstallDb

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
      Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na Pulpicie"; \
      GroupDescription: "Dodatkowe skróty:"; Flags: checkedonce

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Uruchom Cantio"; \
          Flags: nowait postinstall skipifsilent
```
