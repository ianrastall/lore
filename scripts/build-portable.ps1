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
if ((Get-Content -LiteralPath $csproj -Raw) -notmatch '<Version>([^<]+)</Version>') {
    throw 'No <Version> found in Lore.csproj.'
}
$version = $Matches[1].Trim()
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

# Verify the layout carries everything the app needs at runtime. The list is
# required-files.txt, the same one Lore reads when it starts (Services\InstalledFiles.cs);
# "publish:" marks a file only a published build has, and all of them are wanted here.
$manifest = Join-Path $root 'required-files.txt'
$required = Get-Content -LiteralPath $manifest |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { ($_ -replace '^publish:\s*', '').Trim() }
if (@($required).Count -lt 20) { throw "required-files.txt lists too few files to be right: $manifest" }

$missing = @()
foreach ($r in @($required) + 'required-files.txt') {
    $staged = Join-Path $outDir $r
    if (-not (Test-Path -LiteralPath $staged -PathType Leaf)) { $missing += $r }
    elseif ((Get-Item -LiteralPath $staged).Length -eq 0) { $missing += "$r (empty)" }
}
if ($missing.Count -gt 0) {
    throw "Portable build is missing: $($missing -join ', ')"
}

# Each of the files Lore's readings are written from must also be whole: a file cut
# short would pass the check above and leave a reading empty. (The two large lists of
# places are left to the tests, which read them in full.)
foreach ($corpus in 'interpretations', 'daily', 'synastry', 'davison', 'inventory', 'mirror', 'legend') {
    $staged = Join-Path $outDir "Data\$corpus.json"
    try {
        $null = Get-Content -LiteralPath $staged -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        throw "Portable build: Data\$corpus.json is not valid JSON ($($_.Exception.Message))"
    }
    # And the same bytes as the reviewed file in the repository.
    $source = Join-Path $root "Data\$corpus.json"
    if ((Get-FileHash -LiteralPath $staged).Hash -ne (Get-FileHash -LiteralPath $source).Hash) {
        throw "Portable build: Data\$corpus.json differs from the one in the repository."
    }
}

# Last, the packaged app is run: "Lore.exe --self-check" loads every corpus, the figure
# library and the places from the staged folder, calls the Swiss Ephemeris library and
# calculates and reads a chart, then exits with no window (Services\SelfCheck.cs). It
# does not open %LOCALAPPDATA%\Lore, so nobody's own charts or answers are touched.
$report = Join-Path ([System.IO.Path]::GetTempPath()) "lore-self-check-$([guid]::NewGuid().ToString('N')).txt"
$check = Start-Process -FilePath (Join-Path $outDir 'Lore.exe') -ArgumentList '--self-check', "`"$report`"" -PassThru
if (-not $check.WaitForExit(180000)) {
    $check.Kill()
    throw 'Portable build: Lore.exe --self-check did not finish within three minutes.'
}
$found = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report } else { @('(no report was written)') }
Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
if ($check.ExitCode -ne 0 -or ($found -join '') -ne 'OK') {
    throw "Portable build failed its self-check (exit code $($check.ExitCode)):`n  $($found -join "`n  ")"
}
Write-Host 'Self-check of the packaged app: OK' -ForegroundColor Green

# Safety gate: never package personal (user-entered) charts. Throws to abort the build.
& (Join-Path $PSScriptRoot 'assert-clean-release.ps1') -StageDir $outDir

Write-Host ""
Write-Host "Portable build ready: $outDir" -ForegroundColor Green
Write-Host "Zip that folder and hand it to anyone -- they run Lore.exe." -ForegroundColor Green
