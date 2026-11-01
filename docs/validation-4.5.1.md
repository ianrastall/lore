# Validation: Lore 4.5.1

1 November 2026. Fix for closing the main window while the splash is still showing.

## Change

The main window records when it has closed. Closing it dismisses the splash, which
also releases its startup wait. The launch continuation checks the window before
activating it, and the initial data-load continuation checks before restoring the
view or scrolling the Charts list. A cancelled close still leaves startup running.

## Automated checks

| Check | Result |
| --- | --- |
| Tests (`Release`) | 608 passed, 0 failed, 0 skipped |
| App build (`Debug`) | 0 warnings, 0 errors; Jupiter style check enforced at Error severity |
| Published app (`Release`) | Built successfully; Jupiter style check enforced at Error severity |
| `--self-check` on the published copy | OK |
| Personal-data gate | Passed: 11,163 figures, 0 personal charts |
| Packaging | Installer and portable zip built successfully |

The test suite exercises the core, not WinUI window lifetime. It does not establish
that the startup-close regression is fixed on screen.

## Seen on screen

Ian authorized the desktop checks and upgrade in this chat. The UI regression
checks used the debug build with isolated `LORE_HOME` folders.

| Check | Result |
| --- | --- |
| Normal startup | Splash closed, main window displayed 11,163 figures with no personal charts |
| Close after normal startup | Process exited, no new unhandled exceptions |
| Close while the splash is open | Passed with a synthetic workload of 60,000 charts to keep initialization in progress. Both splash and main were identified through UI Automation, the splash was confirmed present immediately before the close action, and the process exited promptly |
| Error log across all checks | No new unhandled, background-unhandled or unobserved-task exceptions |

The first two attempts at the early-close check finished loading before the close
action. They passed as ordinary closes; only the synthetic slow-start run is
evidence for the splash-close regression.

## Installed

4.5.1 over 4.5.0 at `D:\Lore`, keeping the existing all-users installation.

| Check | Result |
| --- | --- |
| Installer | Exit 0; no restart needed |
| Apps registration | Lore 4.5.1 at `D:\Lore`, uninstaller present |
| Start Menu shortcut | Top-level Lore entry still targets `D:\Lore\Lore.exe` |
| Installed `--self-check` | Exit 0, OK |
| Start Menu launch | Launched through the actual shortcut; one main window, responding, 11,163 figures and 4 personal charts loaded; closed cleanly |
| Saved data | All 5 files matched SHA-256 checksums after the isolated tests, upgrade and installed launch: `mycharts.json`, its backup, `inventories.json`, `shell.json` and `ui.json` |

Daily-reading caches were excluded because the HUD updates them. No screenshot or
accessibility-tree capture was taken of the installed copy's personal charts.

Private baseline hashes, logs and synthetic test files are under the ignored
`artifacts/qa-4.5.1` folder and are not release assets.
