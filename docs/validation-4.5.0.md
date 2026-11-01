# Validation: Lore 4.5.0

1 November 2026. Windows 11, x64. Every synastry search can be read whole and queried, and the library is read through again.

## Passed

| Check | Result |
| --- | --- |
| Tests | 608 of 608 passed |
| App build | 0 warnings, 0 errors; style check at Error severity with 0 violations |
| Personal-data gate | Passed: 11,163 figures, 0 personal charts |
| `--self-check` on the published build | OK |

## Seen on screen

In a debug build with an empty chart folder, driven through UI Automation:

| Check | Result |
| --- | --- |
| Matches for Albert Einstein | All 11,162 listed, 316 of them Soulmates, with the count beside the search |
| Level, Look among, Sort by, Contemporaries only and the search together | Narrowed to 4 poets, Least known first, and Clear put all of them back |
| Header at a 1500 by 1000 window | First left the list two rows, so the note went behind a button and the filters into one row. Five rows now |
| Right-click, Start a trio | Moved to the Trine view with that person as the second, 11,216 tried as the third, 100 of them Golden |
| Finder | All 4,950 pairs listed, and rebuilt among the people a search allows |

## Installed

4.5.0 over 4.4.0 on Ian's machine, at his word.

| Check | Result |
| --- | --- |
| Upgrade | Exit 0, Apps entry **Lore** 4.5.0 at `D:\Lore`, uninstaller present, Start entry kept, 11,163 figures in `Data` |
| User data | All 3 files matched their checksums after the upgrade and the launch; the daily reading caches and window preferences were not compared |
| `--self-check` on the installed copy | OK |
| Launch from the Start shortcut | Splash at 0.6 s, main window at 4.7 s, responding, closed cleanly. 11,163 figures and 4 charts loaded. No screenshot: it shows Ian's own charts |

## Not verified

- **The list with your own charts.** The test copy held only the library.
- **A smaller window.** The filters share the width, and were not tried below 1500 wide.

## Known

Closing the main window while the splash is still up logs an unhandled exception from `App.OnLaunched`, as noted for 4.4.0. Nothing is lost.
