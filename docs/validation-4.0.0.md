# Validation: Lore 4.0.0

31 October 2026. Windows 11, x64. Installed on Ian's machine from `Lore-4.0.0-win-x64-setup.exe`, built at commit `0965dd9`. Later commits change documents only.

## Build and tests

| Check | Result |
| --- | --- |
| Baseline, 3.4.0 before any change | 484 tests passed |
| Tests on the new layout, with the reading command added | 488 of 488 passed |
| App build, Debug and Release | 0 warnings, 0 errors |
| Style check at Error severity | 0 violations (3.4 XAML: 289, including 12 NumberBox) |
| `Lore.exe --self-check`, published and installed copies | OK: every required file, every corpus, a chart calculated and read |
| Personal-data gate | Passed: 1,314 figures, 0 personal charts |
| `content/celebrities.json` against 3.4 | Byte-identical (same git blob) |
| Installer, portable zip | 88.8 MB, 133.8 MB |

## Installed delivery

| Check | Result |
| --- | --- |
| Upgrade of the all-users 3.2.0 at `D:\Lore` | Exit 0; version 4.0.0.0, Apps entry **Lore** 4.0.0, same folder and install mode |
| Start Menu | Top-level `Lore.lnk`; the old `Lore` folder and its shortcuts removed |
| Leftovers from 3.x | Splash files removed |
| Launch from the Start shortcut | Window in 0.7 s, responding, 1,314 figures and 4 saved charts loaded, last chart restored, closed cleanly |
| User data through upgrade | All 7 files in `%LOCALAPPDATA%\Lore` matched their SHA-256 checksums |
| User data after running 4.0 | Saved charts, answers and their backup unchanged; `shell.json` added |
| Install, reinstall and uninstall of a per-user test copy | All exit 0; files, shortcut and registry entry removed; 8 of 8 data files unchanged |

The backup and checksums are in `artifacts/` (`upgrade-backup-3.2.0-20261031`, `upgrade-hashes-before.json`), which git ignores.

## Jupiter HUD 0.4.1

Verify build, Rust tests and managed/native smoke checks passed. Installed silently over 0.4.0 with all three settings files unchanged. Fresh `--live-lore` readings passed for all three people through the installed Lore 4 with no error files, and the saved charts were unchanged. See the [HUD verification](../../jupiter-hud/docs/verification-0.4.1.md).

## Exercised in a running build

With made-up data in a debug build's `LORE_HOME` folder, driven through UI Automation and inspected from screenshots:

- All nine views with a figure's chart, a synastry comparison with its bi-wheel, and best and worst matches.
- The Settings window as far as Calculation; its lower sections were below the fold and were not exercised.
- New chart: an out-of-range latitude was refused, a valid chart was saved, and Edit, Delete and Inventory became available for it.
- `--daily-reading`: exit 0 and a valid `lore.daily.v1` file.
- Ctrl+1 and Ctrl+2 switched views.
- File > Export reached the save dialog for five kinds. The test script could not fill the dialog, so nothing was saved that way.

Those checks found and fixed: Worksheet values drifting right and clipped, faint verdict marks, a refusal message out of sight at the foot of the New chart form, the search box not following a cleared search, and the list not scrolling to the last chart. The fixes to the refusal message, the search box and the list passed the build and tests but have not been seen on screen.

The export drawing and document code was run off screen in a small harness compiling the app's `ChartRenderer`, `ChartSheetRenderer` and `ExportService` against the same Windows App SDK and Win2D versions: chart wheel, chart with tables, transit wheel and bi-wheel PNGs, and the full reading, daily and synastry PDFs were all produced, and the wheel was inspected.

## Interrupted test run

The first install attempt was stopped at Setup's exit prompt and rolled back with nothing changed. Three test windows also closed early. Ian was playing online chess while they appeared, so these were closed by hand; four later runs each stayed up and exited with code 0 on request, and the event log recorded no crash. The app now has a rule to ask before opening windows on his machine.

## Not verified

- A complete export written through the save dialog, and Import and Export My Charts through their dialogs.
- Uninstall of the all-users install. The round trip used a per-user test copy to avoid further administrator prompts; the same installer and uninstaller logic was used.
- Settings changes: orbs, the minor aspect toggle, Where you are and a custom accent.
- Shortcuts other than Ctrl+1 and Ctrl+2.
- A clean standard-user machine, and SmartScreen behavior of the unsigned installer.
- High contrast, text scaling and mixed DPI.
