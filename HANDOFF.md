# Lore handoff

Version 5.2.0 · 6 November 2026 · Repository `D:/jupiter/lore`, branch `main`, remote `ianrastall/lore`

## Where things live

| What | Where |
| --- | --- |
| Shell | `src/Lore.App` (WinUI 3, Jupiter.UI, Win2D). Builds `Lore.exe`. |
| Domain core | `src/Lore.Core` (C#; see [exceptions](docs/jupiter-exceptions.md)) |
| Content | `content/` in the repository, `Data\` in the installed app |
| Swiss Ephemeris | `third_party/swisseph`, compiled to `Native/sweph.dll` (not committed) |
| Release | `build/build.ps1`, `build/installer.iss` |
| Installed app | Ian's machine: `D:\Lore`, all-users install, 5.1.0 (admin needed to upgrade) |
| Other apps | Jupiter HUD 0.4.1 runs `Lore.exe --daily-reading`; keep `lore.daily.v1` stable |
| Reader's data | `%LOCALAPPDATA%\Lore`. Never in the repository. |

## Commands

```powershell
pwsh build\build.ps1                # tests, publish, self-check, personal-data gate, installer
pwsh build\build.ps1 -SkipTests     # packaging only
dotnet build src\Lore.App -c Debug
dotnet test tests\Lore.Tests
```

A debug build honors `LORE_HOME`, so it can be run with made-up charts. Use it for every screenshot and UI check, so that nobody's real charts or answers are shown. A release build ignores it. Ask Ian before opening any window or installer on his machine: he plays online chess on it.

## Toolchain

.NET SDK 10.0.113 or later 10.0 (`global.json`), Windows App SDK 2.5.1, Win2D 1.4.0, Inno Setup 6, Visual Studio Build Tools (MSVC, for `sweph.dll`). Package versions are in `Directory.Packages.props`. Jupiter.UI must sit beside this repository at `..\jupiter-ui`.

When the XAML compiler reports unknown types with `WMC9999` or `WMC1509`, the cause is usually a C# error hidden in the pre-compile. Run `dotnet build -v d` and look for `error CS`.

## Delivered

See [reading expansion validation](docs/validation-5.1.0.md), [Writers validation](docs/validation-5.0.0.md), [4.5.1](docs/validation-4.5.1.md) for the startup-close fix, [4.5](docs/validation-4.5.0.md), [4.4](docs/validation-4.4.0.md), [4.3](docs/validation-4.3.0.md) and [4.2](docs/validation-4.2.0.md) before it, [migration](docs/jupiter-migration.md) for what moved, and [exceptions](docs/jupiter-exceptions.md) for recorded departures.

## Sky (5.2)

Ian asked on 6 November 2026 for "planetary mood": the day itself run through Lore,
for the world in general. The **Sky** view reads a day with no chart, from the
aspects the planets make to one another. See the [plan](docs/planetary-mood-plan.md).

- Scan: `TransitService.ScanSky` (`TransitService.Sky.cs`), sharing the Daily scan's
  hourly sampling (`SampleDay`, `DayOf`). The Moon's contacts use 1°, the others 2°
  (`SkyOrb`). The Sun–Moon pair is left out: its aspects are the lunar phases.
- Reading: `SkyInterpreter`, from `content/sky.json` (127 pair lines, one for every
  pair and tone that can occur; `SkyInterpreter.PossibleKeys`) plus the Moon, phase,
  sky-event and planetary-hour lines of `daily.json`, through public helpers on
  `DailyInterpreter` (`MoonItem`, `SkyEventItem`, `SkyNotes`, `HoursSection`, `Varied`).
- Tone: the Daily view's steps and thresholds. Aspects among Uranus, Neptune and Pluto
  (`SkyAspect.IsGenerational`) do not count: the decades-long sextiles otherwise made
  most days Flowing.
- View: `SkyView`, full workspace like Writers, Ctrl+Shift+4. Exports are handled by
  `MainWindow.ExportSkyAsync`, outside `ExportRequest`, which requires a chart.

## Next

Writers is delivered in 5.0. `WriterInterpreter` delegates to the existing natal,
synastry and Trine readers. **No writing prompts or invented interaction types.**
Historical groups require the same field (library category), nearby birthplaces,
a ten-year age span and every member deceased for fifty years. Private snapshots
are in `writer-briefs.json`, never in the release. Ian authorized autonomous work,
necessary desktop checks, installation and delivery of Writers and the following
corpus work; do not stop to ask him to supervise this work.

1. **Broader reading corpus delivered in 5.1.** 431 authored alternatives across 325 existing prose entries in all five shared readers; see [composition](docs/reading-alternatives.md). The ordinary scores, geometry and relationship categories are unchanged. All 650 tests pass. An upgrade and a headless self-check through the Start Menu shortcut passed with all five saved-data checksums preserved. Ian stopped Computer Use with Escape during the attempted isolated desktop check; no further UI input was sent in that turn. Fresh visual QA is incomplete. Audit outputs are in ignored artifacts/corpus-audit; no private data belongs in a release.
2. **Rust core decision.** Open; see the exceptions record.
3. **Theme setting.** Needs a light wheel palette.

## The splash

`SplashWindow` is the portrait of Lauren, for whom Lore is named and made. It is the app's signature: keep it. See [exceptions](docs/jupiter-exceptions.md).

To photograph it on a 200% display, make the script DPI-aware and use `CopyFromScreen`: a `PrintWindow` from an unaware process crops it to the top left quarter, which looks like a bug and is not.

## Self-report (4.2)

The Inventory view has three parts: Personality, Character and Life areas. All three are in `inventories.json`, under `SelfReportService.StoreKey`. `MirrorInterpreter.Compose` reads whatever there is. The answers now choose and score as well as add wording: `MirrorReading.Lift` for what a day and a comparison spend their space on, and the four constants above `SynastryScoring.Assess` for the balance as lived, with the charts' own scores kept beside it. See [next steps](docs/next-steps.md), section 3j, and [validation](docs/validation-4.2.0.md).

Figures: do not script Astro-Databank. It has a browser check, and its robots file names `anthropic-ai`. See `scripts/celebrity-expansion-2026-10-31.md`.

## Trine (4.1)

Ian's "synastry trines" means a synastry of three people, with a golden metric he could not name precisely, and astrology has no standard one. The measure is Lore's own, in `src/Lore.Core/Services/TrioService.cs`: Golden needs no pair below level 0, a positive sum of pair levels, and a strongest grand trine of weight 0.5 or more. `TrioServiceTests` holds the proportions against the library. If Ian wants it stricter or looser, `GoldenWeight` is the one number to change. The finder (`TrioService.FindTrios`, top 100 connections in pairs) is opened by choosing Trine with one person selected. 4.2 added strong T-squares, yods and meeting points: `FigureWeight` is their one number.
