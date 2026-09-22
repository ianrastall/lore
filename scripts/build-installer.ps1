#Requires -Version 5.1
<#
.SYNOPSIS
    Builds LoreSetup-<version>.exe -- a single double-click installer that puts
    Lore in the Start menu, with a clean Add/Remove Programs uninstall entry.

.DESCRIPTION
    1. Produces the self-contained build via build-portable.ps1 (unless -SkipPublish).
    2. Locates the Inno Setup compiler (ISCC.exe).
    3. Compiles installer\Lore.iss into artifacts\LoreSetup-<version>.exe.

    Prerequisite: Inno Setup 6.3+ must be installed. If it is not found, this
    script prints the one-line winget command to install it and stops.

.PARAMETER SkipPublish
    Reuse the existing artifacts\Lore-<ver>-portable\ folder instead of
    re-running the self-contained publish.

.EXAMPLE
    .\scripts\build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $PSScriptRoot
$csproj    = Join-Path $root 'Lore.csproj'
$iss       = Join-Path $root 'installer\Lore.iss'
$artifacts = Join-Path $root 'artifacts'

# --- Version (read from the .csproj so everything stays in sync) ------------
$version = '1.1.0'
$csprojText = Get-Content -LiteralPath $csproj -Raw
if ($csprojText -match '<Version>([^<]+)</Version>') {
    $version = $Matches[1].Trim()
}

# build-portable.ps1 emits this exact folder name.
$portableDir = Join-Path $artifacts "Lore-$version-portable"

# --- 1. Publish the self-contained build ------------------------------------
if (-not $SkipPublish) {
    Write-Host "Publishing self-contained build..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'build-portable.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'build-portable.ps1 failed.' }
}

if (-not (Test-Path -LiteralPath (Join-Path $portableDir 'Lore.exe'))) {
    throw "Published build not found at: $portableDir`nRun without -SkipPublish, or run .\scripts\build-portable.ps1 first."
}

# Safety gate: never package personal (user-entered) charts. Re-checked here so the
# -SkipPublish path (reusing an existing portable folder) is also verified. Throws to abort.
& (Join-Path $PSScriptRoot 'assert-clean-release.ps1') -StageDir $portableDir

# --- 2. Locate ISCC.exe (Inno Setup compiler) -------------------------------
function Find-Iscc {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        # winget installs per-user here by default.
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }

    # Fall back to the App Paths registry entry, if present.
    try {
        $reg = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Compil32.exe' -ErrorAction Stop
        $dir = Split-Path -Parent $reg.'(default)'
        $exe = Join-Path $dir 'ISCC.exe'
        if (Test-Path -LiteralPath $exe) { return $exe }
    } catch { }

    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup compiler (ISCC.exe) not found." -ForegroundColor Yellow
    Write-Host "Install it once, then re-run this script:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    winget install --id JRSoftware.InnoSetup -e" -ForegroundColor White
    Write-Host ""
    Write-Host "(or download from https://jrsoftware.org/isdl.php )" -ForegroundColor Yellow
    throw 'Inno Setup is not installed.'
}
Write-Host "Using Inno Setup: $iscc" -ForegroundColor DarkGray

# --- 3. Compile the installer ----------------------------------------------
Write-Host "Compiling installer (v$version)..." -ForegroundColor Cyan
& $iscc `
    "/DAppVersion=$version" `
    "/DSourceDir=$portableDir" `
    "/DOutputDir=$artifacts" `
    $iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC compilation failed.' }

$setupExe = Join-Path $artifacts "LoreSetup-$version.exe"
Write-Host ""
if (Test-Path -LiteralPath $setupExe) {
    $sizeMb = [math]::Round((Get-Item -LiteralPath $setupExe).Length / 1MB, 1)
    Write-Host "Installer ready: $setupExe  (${sizeMb} MB)" -ForegroundColor Green
    Write-Host "Attach it to a GitHub Release; the recipient double-clicks to install." -ForegroundColor Green
} else {
    throw "ISCC reported success but $setupExe was not produced."
}
