# Validation: Lore 4.4.0

1 November 2026. Windows 11, x64. The library grows to 11,218 figures, and the Charts list shows the best known first.

## Passed

| Check | Result |
| --- | --- |
| Tests | 595 of 595 passed |
| Personal-data gate | Passed: 11,218 figures, 0 personal charts |
| `--self-check` on the published build | OK |

## Installed

4.4.0 over 4.3.0 on Ian's machine, at his word.

| Check | Result |
| --- | --- |
| Upgrade | Exit 0, Apps entry **Lore** 4.4.0 at `D:\Lore`, uninstaller present, Start entry kept, 11,218 figures in `Data` |
| User data | All 5 files matched their checksums after the upgrade and after both launches; the daily reading caches were not compared, because the HUD rewrites them |
| `--self-check` on the installed copy | OK |
| Launch from the Start shortcut | Splash at 0.5 s. The log shows 11,218 figures and 4 charts loaded in about 3 s, and the main window up with its chart at 4.7 s, against 2.4 s in 4.3.0 |

## Not verified

- **A full session.** The window closed within seconds on both launches, with no crash in the log or in Windows' event log and the window's placement saved, so it was closed from outside Lore. The Charts list, its order and match ranking at this size have not been used on screen.
- **Ranking at this size.** Timed at about 0.4 s for 1,408 figures, so about 4 s is expected.

## Known

Closing the main window while the splash is still up logs an unhandled exception from `App.OnLaunched`, which calls `Activate` on the closed window. Lore is closing at that point, so nothing is lost.
