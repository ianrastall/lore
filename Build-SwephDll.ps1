#Requires -Version 5.1
<#
.SYNOPSIS
    Compiles sweph.dll (Swiss Ephemeris x64) from the bundled source using MSVC.
    Run this once from the project root before building Lore.
    Requires Visual Studio 2019/2022 Build Tools (any edition) to be installed.
#>

$srcDir  = "$PSScriptRoot\Data\swisseph-2.10.3bfinal\swisseph-2.10.3bfinal"
$outDir  = "$PSScriptRoot\Native"
$dllOut  = "$outDir\sweph.dll"

$sources = @(
    "swedate.c",
    "swehouse.c",
    "swejpl.c",
    "swemmoon.c",
    "swemplan.c",
    "sweph.c",
    "swephlib.c",
    "swecl.c",
    "swehel.c"
)

# ── Locate vswhere ──────────────────────────────────────────────────────────────
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Error "vswhere.exe not found. Please install Visual Studio Build Tools."
    exit 1
}

$vsPath = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath 2>$null | Select-Object -First 1

if (-not $vsPath) {
    Write-Error "No Visual Studio installation with C++ tools found."
    exit 1
}

Write-Host "Found Visual Studio at: $vsPath"

# ── Set up MSVC build environment ───────────────────────────────────────────────
$vcvars = "$vsPath\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    Write-Error "vcvars64.bat not found at: $vcvars"
    exit 1
}

Write-Host "Setting up x64 MSVC environment..."

# Capture environment changes from vcvars64.bat
$envLines = cmd /c "`"$vcvars`" && set 2>&1" | Where-Object { $_ -match "^[^=]+=.*" }
foreach ($line in $envLines) {
    if ($line -match "^([^=]+)=(.*)$") {
        [System.Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], "Process")
    }
}

# ── Verify cl.exe is accessible ─────────────────────────────────────────────────
$clPath = (Get-Command cl.exe -ErrorAction SilentlyContinue)?.Source
if (-not $clPath) {
    Write-Error "cl.exe not found after setting up MSVC environment."
    exit 1
}

Write-Host "Using compiler: $clPath"

# ── Create output directory ──────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ── Compile ─────────────────────────────────────────────────────────────────────
Push-Location $srcDir
try {
    $objFiles = $sources | ForEach-Object { [System.IO.Path]::ChangeExtension($_, ".obj") }

    # Compile each .c to .obj
    Write-Host "Compiling source files..."
    $clArgs = @("/nologo", "/O2", "/MD", "/W3",
                "/DWIN32", "/D_WIN32", "/DMAKE_DLL",
                "/c") + $sources
    & cl.exe @clArgs
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)." }

    # Link into DLL
    Write-Host "Linking sweph.dll..."
    $linkArgs = @("/nologo", "/DLL", "/OUT:$dllOut") + $objFiles
    & link.exe @linkArgs
    if ($LASTEXITCODE -ne 0) { throw "Link failed (exit $LASTEXITCODE)." }

    # Clean up .obj and .exp/.lib noise in the source dir
    Remove-Item -Force *.obj, *.exp, *.lib -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

if (Test-Path $dllOut) {
    Write-Host ""
    Write-Host "SUCCESS: $dllOut" -ForegroundColor Green
    $size = (Get-Item $dllOut).Length / 1KB
    Write-Host "  Size: $([int]$size) KB"
} else {
    Write-Error "DLL not found at expected path: $dllOut"
    exit 1
}
