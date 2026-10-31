<#
.SYNOPSIS
    Tests and publishes Lore as a win-x64 installer, with a portable folder and zip as
    supplemental artifacts.

.DESCRIPTION
    The normal release command. It fails if a test, the check of the finished copy, the
    personal-data gate or a packaging prerequisite (Inno Setup) fails. It builds
    Native\sweph.dll first if a fresh checkout has none. Outputs go to dist\.

.EXAMPLE
    pwsh build/build.ps1                 # tests, publish, checks and installer
    pwsh build/build.ps1 -SkipTests      # publish, checks and installer only
#>
param(
    [switch]$SkipTests,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'Lore'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Select-Object -First 1

if (-not $InnoCompiler) {
    $InnoCompiler = @(
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Install Inno Setup 6 or pass -InnoCompiler <ISCC.exe>. Every Jupiter GUI release needs its installer.'
}

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "== $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

Push-Location $root
try {
    if (-not (Test-Path (Join-Path $root 'Native/sweph.dll'))) {
        Invoke-Step 'build sweph.dll' { & (Join-Path $PSScriptRoot 'build-sweph.ps1') }
    }
    if (-not $SkipTests) {
        Invoke-Step 'tests' { dotnet test tests/Lore.Tests -c Release --nologo -v q }
    }

    if ([IO.Path]::GetFullPath($appDir) -ne [IO.Path]::GetFullPath((Join-Path $root 'dist/Lore'))) { throw 'Unexpected publish path.' }
    if (Test-Path $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }
    Invoke-Step 'publish app' { dotnet publish src/Lore.App -c Release -o $appDir --nologo -v q }

    # The published copy checks itself: every file on required-files.txt is there, every
    # corpus loads, and a chart is calculated and read from the bundled data.
    Write-Host '== self-check' -ForegroundColor Cyan
    $report = Join-Path $dist 'self-check.txt'
    $check = Start-Process -FilePath (Join-Path $appDir 'Lore.exe') -ArgumentList '--self-check', "`"$report`"" -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "The published copy failed its self-check:`n$(Get-Content $report -Raw)" }

    Invoke-Step 'personal-data gate' { & (Join-Path $PSScriptRoot 'assert-clean-release.ps1') -StageDir $appDir }

    $zip = Join-Path $dist "Lore-$version-win-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal

    Invoke-Step 'build installer' { & $InnoCompiler /Qp "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer.iss') }
    $setup = Join-Path $dist "Lore-$version-win-x64-setup.exe"
    Get-Item $setup, $zip | ForEach-Object { '{0}  {1:n1} MB' -f $_.FullName, ($_.Length / 1MB) }
}
finally {
    Pop-Location
}
