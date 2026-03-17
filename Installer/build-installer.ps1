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

# ── Paths ─────────────────────────────────────────────────────────────────────

$RepoRoot   = Resolve-Path "$PSScriptRoot\.."
$CsprojPath = Join-Path $RepoRoot "Cantio\Cantio.csproj"
$SeedDbPath = Join-Path $RepoRoot "Cantio.db"
$PublishDir = Join-Path $PSScriptRoot "publish"
$IssPath    = Join-Path $PSScriptRoot "cantio.iss"
$IsccExe    = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"

# ── Step 1: Read version ──────────────────────────────────────────────────────

[xml]$csproj = Get-Content $CsprojPath
$Version = $csproj.Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -First 1 -ExpandProperty Version

if (-not $Version) {
    Write-Error "Nie znaleziono elementu <Version> w $CsprojPath"
}

Write-Host "Wersja: $Version" -ForegroundColor Cyan

# ── Step 2: Verify seed database ──────────────────────────────────────────────

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

# ── Step 4: Inno Setup ────────────────────────────────────────────────────────

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
