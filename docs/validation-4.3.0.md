# Validation: Lore 4.3.0

31 October 2026. Windows 11, x64. Brings back the splash screen, and lets a person be chosen from the Charts list.

## Passed

| Check | Result |
| --- | --- |
| Tests | 549 of 549 passed |
| App build | 0 warnings, 0 errors; style check at Error severity with 0 violations, the splash's departures declared |
| Personal-data gate | Passed: 1,327 figures, 0 personal charts |
| `--self-check` on the published build | OK |

## Seen on screen

In a debug build with made-up people, on a 3840 x 2160 display at 200%:

| Check | Result |
| --- | --- |
| The splash, taken from the screen | Whole portrait, the lettering and "4.3" in place, 1135 pixels square and centred |
| Timing | Up 0.5 s after launch; the main window takes over at 2.7 s and has the focus |
| **+** in the Charts list | Shown only in the Synastry view. The first filled Compare with and the next, in Trine, the third, with the selected chart left selected |
| The right-click menu, from the Chart view | Both items worked and opened the Synastry view |

## Installed

4.3.0 over 4.2.1 on Ian's machine, at his word.

| Check | Result |
| --- | --- |
| Upgrade | Exit 0, Apps entry **Lore** 4.3.0 at `D:\Lore`, uninstaller present, Start entry kept, `Assets\splash.png` installed |
| User data | All 5 files matched their checksums; the daily reading caches were not compared, because the HUD rewrites them |
| `--self-check` on the installed copy | OK |
| Launch from the Start shortcut | Splash at 0.5 s, main window at 2.4 s, responding, closed cleanly, and the charts and answers unchanged afterwards. No screenshot: it shows Ian's own charts |

## Not verified

- **A splash on a smaller or second display.** It takes 55% of the shorter side of the display Lore opens on.
- **The **+** with your own charts.** Made-up people and the library were used.
