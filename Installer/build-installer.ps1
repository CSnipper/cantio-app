<#
.SYNOPSIS
    Builds the Cantio installer.
.DESCRIPTION
    1. Reads <Version> from Cantio/Cantio.csproj
    2. Verifies Cantio.db exists at repo root
    3. Runs dotnet publish (self-contained)
    4. Runs Inno Setup compiler (iscc.exe)
    5. Prints path to the produced installer .exe
.PARAMETER Arch
    Target architecture: x64 (default) or x86. Use "both" to build both variants.
.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Arch x86
    .\build-installer.ps1 -Arch both
#>

param(
    [ValidateSet('x64', 'x86', 'both')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Paths ─────────────────────────────────────────────────────────────────────

$RepoRoot   = Resolve-Path "$PSScriptRoot\.."
$CsprojPath = Join-Path $RepoRoot "Cantio\Cantio.csproj"
$SeedDbPath = Join-Path $RepoRoot "Cantio.db"
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

# ── Step 3: Inno Setup present? ───────────────────────────────────────────────

if (-not (Test-Path $IsccExe)) {
    Write-Error "Nie znaleziono Inno Setup: $IsccExe`nZainstaluj Inno Setup 6 ze strony https://jrsoftware.org/isinfo.php"
}

# ── Helper: build one architecture ───────────────────────────────────────────

function Build-Arch([string]$TargetArch) {
    $PublishDir = Join-Path $PSScriptRoot "publish$(if ($TargetArch -eq 'x86') { '-x86' })"
    $Rid        = "win-$TargetArch"
    $Suffix     = if ($TargetArch -eq 'x86') { '-x86' } else { '' }

    Write-Host "`nPublikowanie aplikacji ($TargetArch)..." -ForegroundColor Yellow

    dotnet publish $CsprojPath `
        -c Release `
        -r $Rid `
        --self-contained true `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish nie powiodło się dla $TargetArch (kod: $LASTEXITCODE)"
    }

    Write-Host "Publikowanie $TargetArch`: OK" -ForegroundColor Green

    Write-Host "`nKompilowanie instalatora ($TargetArch)..." -ForegroundColor Yellow

    & $IsccExe $IssPath /DAppVersion=$Version /DArch=$TargetArch

    if ($LASTEXITCODE -ne 0) {
        Write-Error "iscc.exe nie powiodło się dla $TargetArch (kod: $LASTEXITCODE)"
    }

    $OutputExe = Join-Path $PSScriptRoot "Output\CantioSetup-$Version$Suffix.exe"
    Write-Host "Instalator gotowy: $OutputExe" -ForegroundColor Green
}

# ── Step 4: Build selected architecture(s) ───────────────────────────────────

if ($Arch -eq 'both') {
    Build-Arch 'x64'
    Build-Arch 'x86'
} else {
    Build-Arch $Arch
}
