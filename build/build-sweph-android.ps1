#Requires -Version 7
<#
.SYNOPSIS
    Compiles libsweph.so (Swiss Ephemeris) for Android from the bundled source with the
    Android NDK: arm64-v8a for phones and x86_64 for the emulator. The Android app's
    project (src/Lore.Mobile) packs them from Native\android\<abi>\.
    Finds the NDK under ANDROID_HOME (or ANDROID_NDK_HOME), newest version first.
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'third_party\swisseph'
$outDir = Join-Path $root 'Native\android'

$sources = 'swedate.c', 'swehouse.c', 'swejpl.c', 'swemmoon.c', 'swemplan.c',
           'sweph.c', 'swephlib.c', 'swecl.c', 'swehel.c' | ForEach-Object { Join-Path $srcDir $_ }

$ndk = $env:ANDROID_NDK_HOME
if (-not $ndk -and $env:ANDROID_HOME) {
    $ndk = Get-ChildItem (Join-Path $env:ANDROID_HOME 'ndk') -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ndk -or -not (Test-Path $ndk)) { throw 'No Android NDK found. Set ANDROID_HOME or ANDROID_NDK_HOME.' }
$clang = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\clang.exe'
if (-not (Test-Path $clang)) { throw "clang not found at $clang" }
Write-Host "NDK: $ndk"

# The API level matches the app's minimum (SupportedOSPlatformVersion in Lore.Mobile).
$abis = @{ 'arm64-v8a' = 'aarch64-linux-android26'; 'x86_64' = 'x86_64-linux-android26' }

foreach ($abi in $abis.Keys) {
    $dir = Join-Path $outDir $abi
    $so  = Join-Path $dir 'libsweph.so'
    if ((Test-Path $so) -and -not $Force) { Write-Host "$abi`: $so exists (use -Force to rebuild)"; continue }
    New-Item -ItemType Directory -Force $dir | Out-Null
    # 16 KB pages: Android 15 and later phones may use them, and Google Play requires it.
    & $clang --target=$($abis[$abi]) -O2 -fPIC -shared -w `
        '-Wl,-z,max-page-size=16384' '-Wl,--build-id=sha1' '-Wl,-soname,libsweph.so' `
        -o $so @sources -lm
    if ($LASTEXITCODE -ne 0) { throw "clang failed for $abi" }
    Write-Host "$abi`: $so ($((Get-Item $so).Length) bytes)"
}
