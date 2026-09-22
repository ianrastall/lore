#Requires -Version 5.1
<#
.SYNOPSIS
    Release safety gate. Refuses to package a build that carries personal data, so a
    release can never accidentally ship user-entered charts (e.g. family members added
    through Add Chart, which live in %LOCALAPPDATA%\Lore\mycharts.json and are tagged
    with the "My Charts" category).

    The build scripts call this on the staged output *before* it is zipped or handed to
    the installer. Any finding throws, which aborts the build.

.DESCRIPTION
    Three checks, all hard failures:
      1. No mycharts.json anywhere in the staged folder (the personal-chart store must
         never be bundled).
      2. No entry in the shipped Data\celebrities.json uses the "My Charts" category
         (that category is only ever assigned to user-entered charts).
      3. Data\celebrities.json has no uncommitted changes vs git HEAD -- so a release
         ships exactly the figure library that is committed and reviewed, catching
         personal rows added by any means (skipped only if git is unavailable).

.PARAMETER StageDir
    The staged build output to inspect (e.g. artifacts\Lore-1.1.0-portable).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StageDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # repo root (this script lives in scripts\)

if (-not (Test-Path -LiteralPath $StageDir)) {
    throw "assert-clean-release: staged folder not found: $StageDir"
}

$problems = [System.Collections.Generic.List[string]]::new()

# 1. The personal-chart store must never ride along in a release.
$stray = Get-ChildItem -LiteralPath $StageDir -Recurse -File -Filter 'mycharts.json' -ErrorAction SilentlyContinue
foreach ($f in $stray) { $problems.Add("personal chart store bundled: $($f.FullName)") }

# 2. The shipped figure library must contain no user-entered ("My Charts") charts.
$celebPath = Join-Path $StageDir 'Data\celebrities.json'
if (-not (Test-Path -LiteralPath $celebPath)) {
    throw "assert-clean-release: Data\celebrities.json missing from build: $celebPath"
}
$people = Get-Content -LiteralPath $celebPath -Raw | ConvertFrom-Json
foreach ($p in @($people | Where-Object { $_.category -eq 'My Charts' })) {
    $problems.Add("personal chart in celebrities.json: '$($p.name)' (id=$($p.id))")
}

# 3. celebrities.json must match git HEAD -- ship only what is committed and reviewed.
$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    & git -C $root rev-parse --is-inside-work-tree 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & git -C $root diff --quiet HEAD -- 'Data/celebrities.json' 2>$null
        if ($LASTEXITCODE -ne 0) {
            $problems.Add("Data\celebrities.json has uncommitted changes vs git HEAD -- commit the intended library, or 'git checkout -- Data/celebrities.json' to discard local edits, before releasing.")
        }
    }
} else {
    Write-Host "assert-clean-release: git not found -- skipping the committed-state check." -ForegroundColor DarkYellow
}

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "RELEASE BLOCKED - personal data found in the build:" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    Write-Host ""
    throw "assert-clean-release: $($problems.Count) personal-data issue(s); refusing to package."
}

Write-Host "Release safety check passed: $(@($people).Count) figures, 0 personal charts." -ForegroundColor Green
