#Requires -Version 7
<#
.SYNOPSIS
    Builds Lore for Android: the core's tests, Swiss Ephemeris for Android if it is not
    built yet, and a signed release APK in dist\, with its SHA-256.

.DESCRIPTION
    The release signing key is made on first use and kept outside the repository, under
    %LOCALAPPDATA%\Jupiter\Lore\AndroidSigning, with its password protected for this
    Windows account by DPAPI. Every later release must be signed with the same key, or
    it will not install over the one before: keep that folder.

    Needs a .NET 10 SDK with the maui-android workload. On Ian's machine that is the one
    in C:\Program Files\dotnet (see docs/android.md); -Dotnet picks another.
#>
param(
    [switch]$SkipTests,
    [string]$Dotnet = $(if (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe") { "$env:ProgramFiles\dotnet\dotnet.exe" } else { 'dotnet' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Lore.Mobile\Lore.Mobile.csproj'

if (-not $SkipTests) {
    & $Dotnet test (Join-Path $root 'tests\Lore.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

& (Join-Path $PSScriptRoot 'build-sweph-android.ps1')

# ── Signing key ───────────────────────────────────────────────────────────────
$signing = Join-Path $env:LOCALAPPDATA 'Jupiter\Lore\AndroidSigning'
New-Item -ItemType Directory -Force $signing | Out-Null
$credentialPath = Join-Path $signing 'signing.credential.xml'
if (-not (Test-Path -LiteralPath $credentialPath)) {
    $password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    [PSCredential]::new('lore', (ConvertTo-SecureString $password -AsPlainText -Force)) | Export-Clixml -LiteralPath $credentialPath
}
$credential = Import-Clixml -LiteralPath $credentialPath
$keystore = Join-Path $signing 'lore.keystore'
$env:LORE_KEYSTORE_PASSWORD = $credential.GetNetworkCredential().Password
try {
    if (-not (Test-Path -LiteralPath $keystore)) {
        $keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'
        if (-not (Test-Path $keytool)) { $keytool = 'keytool' }
        & $keytool -genkeypair -keystore $keystore -storetype PKCS12 -alias lore -keyalg RSA -keysize 4096 -validity 10000 `
            -storepass:env LORE_KEYSTORE_PASSWORD -keypass:env LORE_KEYSTORE_PASSWORD -dname 'CN=Lore, O=Ian Rastall'
        if ($LASTEXITCODE -ne 0) { throw 'Creating the signing key failed.' }
    }

    # ── Build ─────────────────────────────────────────────────────────────────
    $out = Join-Path $root 'artifacts\android'
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    & $Dotnet publish $project -c Release -f net10.0-android -o $out "-p:LoreKeystore=$keystore"
    if ($LASTEXITCODE -ne 0) { throw 'The Android build failed.' }
} finally {
    Remove-Item Env:LORE_KEYSTORE_PASSWORD -ErrorAction SilentlyContinue
}

[xml]$csproj = Get-Content -LiteralPath $project -Raw
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.ApplicationDisplayVersion } | Select-Object -First 1).ApplicationDisplayVersion
$apk = Get-ChildItem $out -Filter '*-Signed.apk' | Select-Object -First 1
if (-not $apk) { throw "No signed APK in $out." }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$target = Join-Path $dist "Lore-android-$version.apk"
Copy-Item -LiteralPath $apk.FullName -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $target -Leaf)" | Set-Content -LiteralPath "$target.sha256" -Encoding ascii
Write-Host "$target ($((Get-Item $target).Length) bytes)"
Write-Host "SHA-256 $hash"
