#Requires -Version 5.1
<#
.SYNOPSIS
    Generates Lore\'s app icon in the shared Adobe-suite style of the sibling apps
    (Board Meeting / CTML Workspace / Minerva): a dark square field, a thin bright
    inset frame, and a bold centered letter -- here an "L" in gold on deep indigo.

    Produces Assets\LoreIconMaster.png (1024x1024) and Assets\AppIcon.ico.
    Requires ImageMagick (`magick`) on PATH.
#>
$ErrorActionPreference = 'Stop'

$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets'
$master = Join-Path $assets 'LoreIconMaster.png'
$ico    = Join-Path $assets 'AppIcon.ico'

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw 'ImageMagick (magick) is required to build the icon.'
}

New-Item -ItemType Directory -Force -Path $assets | Out-Null

# Palette: deep night-sky indigo field, warm gold frame + letter.
$field = '#161233'
$gold  = '#E8C36B'

# 1024x1024 master. Frame inset ~9% with a thin stroke; the "L" is drawn as two
# rectangles (stem + foot) so the geometry stays crisp at every icon size, with a
# centered bounding box (x/y 372..652 / 262..762 -> centre 512,512).
& magick -size 1024x1024 "xc:$field" `
    -stroke none -fill $gold `
    -draw 'rectangle 372,262 492,762' `
    -draw 'rectangle 372,642 652,762' `
    -fill none -stroke $gold -strokewidth 14 `
    -draw 'rectangle 96,96 928,928' `
    $master
if ($LASTEXITCODE -ne 0) { throw 'Could not generate LoreIconMaster.png' }

& magick $master -define icon:auto-resize=256,128,64,48,32,24,16 $ico
if ($LASTEXITCODE -ne 0) { throw 'Could not generate AppIcon.ico' }

Write-Host "Icon written: $ico" -ForegroundColor Green
