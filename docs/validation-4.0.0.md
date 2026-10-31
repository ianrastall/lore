# Validation: Lore 4.0.0

31 October 2026. Windows 11, x64. **Delivery is not complete:** the installer is built but has not been installed.

## Passed

| Check | Result |
| --- | --- |
| Baseline, 3.4.0 before any change | 484 tests passed |
| Tests on the new layout, with the reading command added | 488 of 488 passed |
| App build, Debug and Release | 0 warnings, 0 errors |
| Style check at Error severity | 0 violations (3.4 XAML: 289, including 12 NumberBox) |
| `dist\Lore\Lore.exe --self-check` on the published build | OK: every required file, every corpus, a chart calculated and read |
| Personal-data gate | Passed: 1,314 figures, 0 personal charts |
| `content/celebrities.json` against 3.4 | Byte-identical (same git blob) |
| Installer | `dist\Lore-4.0.0-win-x64-setup.exe`, 88.8 MB |
| Portable zip | `dist\Lore-4.0.0-win-x64-portable.zip`, 133.8 MB |

## Checked in a running debug build

With made-up data in a `LORE_HOME` folder, driven through UI Automation and inspected from screenshots:

- All nine views rendered with a figure's chart: Chart, Report, Worksheet, Daily, Forecast, Timing, Synastry, Inventory, Legend.
- A synastry comparison with its bi-wheel, and best and worst matches.
- The Settings window as far as Calculation; its lower sections were below the fold and were not exercised.
- New chart: an out-of-range latitude was refused, a valid chart was saved to `mycharts.json`, and Edit, Delete and Inventory became available for it.
- `--daily-reading` for that chart: exit 0, a valid `lore.daily.v1` file with 3 highlights, 3 themes and 3 sky notes.

Those checks found and fixed: Worksheet values drifting right and clipped, faint verdict marks, a refusal message out of sight at the foot of the New chart form, the search box not following a cleared search, and the list not scrolling to the last chart.

The fixes to the refusal message, the search box and the list were built and tested but not seen on screen afterwards.

## Interrupted on 31 October

The first install attempt was stopped at Setup's exit prompt two seconds in, and Setup rolled back. `D:\Lore` stayed at 3.2.0 and every file in `%LOCALAPPDATA%\Lore` matched its checksum afterwards. Ian was playing online chess at the time and windows were appearing on his screen, so further UI work was stopped. Three test windows that closed early were most likely closed by hand: four later runs each stayed up for 14 seconds and exited with code 0 on request, and the event log recorded no crash.

The backup and checksums are in `artifacts/` (`upgrade-backup-3.2.0-20261031`, `upgrade-hashes-before.json`), which git ignores.

## Not yet verified

- Install, upgrade of the all-users 3.2.0 at `D:\Lore`, and launch from the Start Menu. This needs administrator approval on Ian's screen.
- Removal of the old `Lore` Start Menu folder and the new top-level **Lore** entry.
- Settings and data preservation through upgrade, uninstall and reinstall.
- The release build running as a window.
- Exports through the save picker, Import and Export My Charts, and the keyboard shortcuts.
- Jupiter HUD 0.4.1: its source is changed and it builds, but it has no release build or live check yet. It needs Lore 4 installed first. The installed HUD is unaffected and keeps using its existing route.
- High contrast, text scaling and mixed DPI.
