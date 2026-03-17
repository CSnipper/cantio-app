# Installer (Inno Setup) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce `Installer/Output/CantioSetup-x.y.z.exe` by running a single PowerShell script that publishes the app and compiles the installer.

**Architecture:** `dotnet publish` outputs self-contained win-x64 binaries to `Installer/publish/`. Inno Setup reads that directory plus the seed `Cantio.db` from the repo root, and produces a per-machine installer with a custom wizard page for database selection (shown only on fresh install). Version flows from `Cantio.csproj` through the build script to `iscc.exe`.

**Tech Stack:** .NET 10 publish, Inno Setup 6, PowerShell 5+

---

## Prerequisites (not automated — human must verify)

- Inno Setup 6 installed: `C:\Program Files (x86)\Inno Setup 6\iscc.exe`
- `Cantio.db` exists at repo root (seed database)
- .NET 10 SDK installed

---

## File Structure

| File | Action | Purpose |
|---|---|---|
| `Cantio/Cantio.csproj` | Modify | Add `<Version>1.0.0</Version>` |
| `Installer/cantio.iss` | Create | Inno Setup script (full) |
| `Installer/build-installer.ps1` | Create | Build script: publish → iscc |

---

### Task 1: Add version to csproj

**Files:**
- Modify: `Cantio/Cantio.csproj`

- [ ] **Step 1: Open `Cantio/Cantio.csproj` and add `<Version>` to the first `<PropertyGroup>`**

The first `<PropertyGroup>` currently contains `<OutputType>`, `<TargetFramework>`, etc.
Add `<Version>1.0.0</Version>` as the last line inside that block:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <UseWPF>true</UseWPF>
  <ApplicationIcon>cantio.ico</ApplicationIcon>
  <Version>1.0.0</Version>
</PropertyGroup>
```

- [ ] **Step 2: Verify build still passes**

```bash
dotnet build Cantio/Cantio.csproj
```

Expected: `Kompilacja powiodła się` / `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Cantio/Cantio.csproj
git commit -m "chore: dodaj wersję 1.0.0 do Cantio.csproj"
```

---

### Task 2: Create Inno Setup script

**Files:**
- Create: `Installer/cantio.iss`

- [ ] **Step 1: Create `Installer/cantio.iss` with the following content**

```iss
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
Name: "{group}\{#AppName}";    Filename: "{app}\{#AppExeName}"
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
```

- [ ] **Step 2: Verify Inno Setup can parse the script (syntax check)**

Run from `Installer/` directory (requires Inno Setup installed):

```powershell
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" cantio.iss /DAppVersion=0.0.0-test
```

Expected: script compiles but will fail finding `publish\*` because we haven't run publish yet. The error should say something like `Source file not found: publish\*` — that means parsing passed. If it says `Compile error` or `Unknown directive`, fix syntax first.

- [ ] **Step 3: Commit**

```bash
git add Installer/cantio.iss
git commit -m "feat: dodaj skrypt Inno Setup (cantio.iss)"
```

---

### Task 3: Create build script

**Files:**
- Create: `Installer/build-installer.ps1`

- [ ] **Step 1: Create `Installer/build-installer.ps1` with the following content**

```powershell
<#
.SYNOPSIS
    Builds the Cantio installer.
.DESCRIPTION
    1. Reads <Version> from Cantio/Cantio.csproj
    2. Verifies Cantio.db exists at repo root
    3. Runs dotnet publish (self-contained, win-x64)
    4. Runs Inno Setup compiler (iscc.exe)
    5. Prints path to the produced installer .exe
.EXAMPLE
    .\build-installer.ps1
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Paths ────────────────────────────────────────────────────────────────────

$RepoRoot    = Resolve-Path "$PSScriptRoot\.."
$CsprojPath  = Join-Path $RepoRoot "Cantio\Cantio.csproj"
$SeedDbPath  = Join-Path $RepoRoot "Cantio.db"
$PublishDir  = Join-Path $PSScriptRoot "publish"
$IssPath     = Join-Path $PSScriptRoot "cantio.iss"
$IsccExe     = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"

# ── Step 1: Read version ──────────────────────────────────────────────────────

[xml]$csproj = Get-Content $CsprojPath
$Version = $csproj.Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -First 1 -ExpandProperty Version

if (-not $Version) {
    Write-Error "Nie znaleziono elementu <Version> w $CsprojPath"
}

Write-Host "Wersja: $Version" -ForegroundColor Cyan

# ── Step 2: Verify seed database ─────────────────────────────────────────────

if (-not (Test-Path $SeedDbPath)) {
    Write-Error "Brak pliku bazy danych: $SeedDbPath`nUmieść Cantio.db w katalogu głównym repozytorium."
}

Write-Host "Baza seed: OK ($SeedDbPath)" -ForegroundColor Cyan

# ── Step 3: dotnet publish ────────────────────────────────────────────────────

Write-Host "`nPublikowanie aplikacji..." -ForegroundColor Yellow

dotnet publish $CsprojPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish nie powiodło się (kod: $LASTEXITCODE)"
}

Write-Host "Publikowanie: OK" -ForegroundColor Green

# ── Step 4: Inno Setup ───────────────────────────────────────────────────────

if (-not (Test-Path $IsccExe)) {
    Write-Error "Nie znaleziono Inno Setup: $IsccExe`nZainstaluj Inno Setup 6 ze strony https://jrsoftware.org/isinfo.php"
}

Write-Host "`nKompilowanie instalatora..." -ForegroundColor Yellow

& $IsccExe $IssPath /DAppVersion=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Error "iscc.exe nie powiodło się (kod: $LASTEXITCODE)"
}

# ── Step 5: Report output ─────────────────────────────────────────────────────

$OutputExe = Join-Path $PSScriptRoot "Output\CantioSetup-$Version.exe"
Write-Host "`nInstalator gotowy:" -ForegroundColor Green
Write-Host "  $OutputExe" -ForegroundColor White
```

- [ ] **Step 2: Run the build script end-to-end**

From repo root:

```powershell
cd Installer
.\build-installer.ps1
```

Expected output (roughly):
```
Wersja: 1.0.0
Baza seed: OK (...)
Publikowanie aplikacji...
Build succeeded.
Publikowanie: OK
Kompilowanie instalatora...
Successful compile (...)
Instalator gotowy:
  ...\Installer\Output\CantioSetup-1.0.0.exe
```

- [ ] **Step 3: Verify installer exists**

```powershell
Test-Path "Installer\Output\CantioSetup-1.0.0.exe"
```

Expected: `True`

- [ ] **Step 4: Run the installer and verify**

Double-click `Installer/Output/CantioSetup-1.0.0.exe` and confirm:
- UAC prompt appears (admin required)
- Database selection page shows (if Cantio.db not yet in LocalAppData)
- App installs to `C:\Program Files\Cantio\`
- Start Menu shortcut created
- App launches correctly after install

- [ ] **Step 5: Commit**

```bash
git add Installer/build-installer.ps1
git commit -m "feat: dodaj skrypt budowania instalatora (build-installer.ps1)"
```
