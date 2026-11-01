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

The test suite exercises the core, not WinUI window lifetime. It does not establish
that the startup-close regression is fixed on screen.

## Desktop verification and installation

Pending Ian's permission to open windows, as required by `HANDOFF.md`. The installed
copy remains 4.5.0. No desktop launch or installer has been run for this change.

Use a debug build with a fresh `LORE_HOME` for the regression checks:

- Close the main window during the splash: both windows should close promptly,
  the process should exit, and no new unhandled startup exception should be logged.
- Let startup finish: the splash should close, the main window should take focus,
  and the Charts list should populate normally.
- Close after normal startup: the process should exit cleanly.

Then upgrade the installed copy, compare saved-data checksums, verify the Start
Menu shortcut and Apps entry, and launch from Start. Do not photograph personal
charts or inventory answers.
