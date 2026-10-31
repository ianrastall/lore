# Jupiter migration

31 October 2026, Lore 3.4.0 to 4.0.0.

## Repository

The active repository is `D:/jupiter/lore`, a clone of `D:/dev/proj/lore` with its full history and the same GitHub remote. The original checkout remains intact, including its uncommitted test results and `artifacts`. The planning scaffold that stood at `D:/jupiter/lore` is kept in `docs/` (`project-brief.md`, `source-map.json`, `planning-readme.md`). The untracked `docs/Lore-codebase-review.md` came across with it.

| 3.4 | 4.0 |
| --- | --- |
| `Lore.csproj`, `Views/`, `Services/`, root XAML | `src/Lore.App/` |
| `Lore.Core/` | `src/Lore.Core/` |
| `Data/*.json`, `Data/hospitals_wikidata.csv` | `content/` (still `Data\` in the installed app) |
| `Data/swisseph-2.10.3bfinal/swisseph-2.10.3bfinal/` | `third_party/swisseph/` |
| `Build-SwephDll.ps1`, `installer/Lore.iss`, `scripts/assert-clean-release.ps1` | `build/` |
| `release.bat`, `scripts/build-portable.ps1`, `scripts/build-installer.ps1` | `build/build.ps1` |
| `Assets/` screenshots and artwork | `docs/images/` |
| `SETUP.txt` | `README.md`, `HANDOFF.md` |

## Toolchain

.NET SDK pinned in `global.json`, C# 14, central package versions, lock files, x64 only. Windows App SDK moved from 1.8 to 2.5.1, so the minimum is now Windows 11 24H2 (build 26100), up from Windows 10 1809. The app has a custom `Program.Main`, so `--self-check` and `--daily-reading` run with no window.

## Behavior

Calculations, corpora, stores and exports are unchanged; `content/celebrities.json` is byte-identical to 3.4. The changes in `Lore.Core` are confined to these:

- `MainViewModel.Notice` carries problems that need reading; the status bar keeps the short line.
- The verdict color for the browse list is now opaque, because it is a mark and no longer a row wash.
- Wording that named old controls (Add chart, bring in).
- `DailyReadingExport` is new.

## Removed

| Removed | In its place |
| --- | --- |
| Splash window | Restored in 4.3.0: removing it was a mistake. See [exceptions](jupiter-exceptions.md) |
| Settings flyout | Settings window (File > Settings) |
| Toolbar of toggle buttons, Export and Legend buttons | Menu bar and view selector |
| NumberBox (rule JUP004) | `NumberField`, a typed number checked when focus leaves |
| Whole-row verdict wash | A 12-pixel mark on the row |
| Color pills | A 12-pixel mark beside bold text |
| Legend Back button | The view selector; choosing a person also returns to the chart |
| Maximized launch | First run at 1200 × 800, then the last size and place |

## Jupiter HUD

HUD 0.4.0 loaded `Lore.Core.dll` into PowerShell from a Lore build folder under `D:/jupiter/lore` or `D:/dev/proj/lore`. It looked in `D:/jupiter/lore` first, and would have stopped finding a build there once the repository moved. HUD 0.4.1 runs the installed `Lore.exe --daily-reading <name> <file>` instead and reads the `lore.daily.v1` file. It finds Lore through the installer's registry record and needs Lore 4 or later. The cache files, their names and the JSON fields it reads are unchanged, and saved charts are only read.

`lore.daily.v1` holds `kind`, `name`, `date`, `dateText`, `tone`, `toneLabel`, `toneColorHex`, `summary`, `highlights`, `themes` and `skyNotes`, each list of `title`, `text`, `meta`. The reading uses the reader's calculation settings, which the 0.4.0 script did not. Inventory answers and the planetary hours are not part of it, as before.

## Reviewed and left

- The 3.3 and 3.4 repairs to the findings in `docs/Lore-codebase-review.md` carried over with `Lore.Core`.
- The inventory screenshot in the README was dropped. It showed the 3.x window, and a new one needs a made-up completed sitting.
