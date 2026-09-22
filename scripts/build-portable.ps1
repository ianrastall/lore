#Requires -Version 5.1
<#
.SYNOPSIS
    Produces the portable Lore build: a self-contained folder (bundled .NET + Windows
    App SDK runtime + Swiss Ephemeris) that runs by double-clicking Lore.exe -- no
    install, no VC++ redistributable, no separately installed runtime.

    Output: artifacts\Lore-<version>-portable\  (version read from Lore.csproj)
#>
$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$csproj  = Join-Path $root 'Lore.csproj'

# Name the output folder after the csproj <Version> so it always matches the build.
$version = '1.1.0'
if ((Get-Content -LiteralPath $csproj -Raw) -match '<Version>([^<]+)</Version>') {
    $version = $Matches[1]
}
$outDir  = Join-Path $root "artifacts\Lore-$version-portable"

# sweph.dll must exist before publishing (built once by Build-SwephDll.ps1).
if (-not (Test-Path -LiteralPath (Join-Path $root 'Native\sweph.dll'))) {
    throw 'Native\sweph.dll not found. Run .\Build-SwephDll.ps1 first.'
}

# Always start from a clean layout.
if (Test-Path -LiteralPath $outDir) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

& dotnet publish $csproj -c Release -r win-x64 --self-contained true --nologo -o $outDir
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

# Verify the layout carries everything the app needs at runtime.
$required = @(
    'Lore.exe', 'Lore.dll', 'Lore.pri',
    'App.xbf', 'MainWindow.xbf', 'Views\ChartView.xbf',
    'coreclr.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.Graphics.Canvas.dll',
    'sweph.dll',
    'Data\celebrities.json',
    'Assets\Ephemeris\sepl_18.se1', 'Assets\Ephemeris\semo_18.se1', 'Assets\Ephemeris\seas_18.se1',
    'Assets\Ephemeris\sepl_12.se1'
)
foreach ($r in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $outDir $r))) {
        throw "Portable build is missing: $r"
    }
}

Write-Host ""
Write-Host "Portable build ready: $outDir" -ForegroundColor Green
Write-Host "Zip that folder and hand it to anyone -- they run Lore.exe." -ForegroundColor Green
