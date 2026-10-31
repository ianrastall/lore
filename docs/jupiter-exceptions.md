# Jupiter conformance and exceptions

31 October 2026. Lore 4.0.0 follows Jupiter style standard 1.1 and the WinUI 3 stack reference, except as recorded here. The style check runs as an error on every build of the app and reports no violations (the 3.4 XAML had 289). Departures are listed with their reasons.

## Architecture

**The domain core stays in C#.** The stack reference and AGENTS.md make a Rust core the default. Lore 4.0 keeps `Lore.Core`, its existing C# library, and has no Rust crate, C ABI or protobuf schema.

- Reason: `Lore.Core` is about 11,700 lines of tested domain code resting on NodaTime and GeoTimeZone for historical time, QuestPDF for documents and the Swiss Ephemeris through P/Invoke. Its 484 tests compare it with Astro-Databank and swetest. This release moves the interface, and a rewrite of the calculations in the same step would risk the one thing the project brief says to preserve: existing chart and calculation behavior. Nothing in Lore's own use is limited by C#.
- Consequence: the layered ownership of the stack reference holds in shape (`Lore.Core` has no dependency on the shell, and the app holds no domain rules), but the boundary is a .NET project reference and not a C ABI. View models live in `Lore.Core`, which the tests use. No CLI crate exists; the one headless need is met by `Lore.exe --daily-reading`.
- Validation: the full test suite runs against the assembly that ships; `--self-check` runs against the published build.
- A Rust port is a separate decision for Ian, not part of this exception's scope. If it is taken up, the parity oracle is the current C# core: golden outputs for the 1,314 figures and the existing reference files.

**Swiss Ephemeris as a C DLL.** `sweph.dll` is compiled from `third_party/swisseph` with a static C runtime and called through P/Invoke, serialized by `ChartService`. It runs in the app's process.

**No Python module.** Python is used only for data-building scripts in `scripts/`.

## Continuity with 3.x

These keep existing installs and data working, and are not the Jupiter defaults for new apps:

- Data folder `%LOCALAPPDATA%\Lore`, with `mycharts.json`, `inventories.json`, `settings.json`, `ui.json` and `home.json` unchanged in format. New: `shell.json`, owned by the app.
- Installer AppId `{59B8D0CF-4B1F-4DA0-A826-1887F7E2A142}`. An existing install stays in its folder and install mode, so Ian's all-users install at `D:\Lore` upgrades there. `PrivilegesRequiredOverridesAllowed=dialog` is kept for that reason.
- Several Lore windows may run at once; the stores take a cross-process lock. There is no single-instance mutex.
- No application user model ID is set, as in 3.x.

## Interface

**Document workspace with a view selector.** The open chart is the document (standard section 7), with the Charts pane as its left pane on `JupiterSurfaceBrush`. The eight views of a chart and the Legend are switched with a `SelectorBar` under the menu bar. They are views of one document, so `TabView` (documents only) does not apply, and `NavigationView` would add a second left rail beside the Charts pane. Chess.com Crawler and Lichess Puzzles use `SelectorBar` in the same way.

**View headings.** Each view opens with `JupiterPageHeadingTextStyle` naming whose chart it is. It is the view's single heading, not an app title. Sections below it use `JupiterSectionHeadingTextStyle`.

**Dark only.** The optional Theme setting is not offered in 4.0: the wheels, the "chart with tables" image and the exported PNGs have one palette. `RequestedTheme="Dark"` is set on each window and the title bar follows.

**Content palettes (section 3.4).** These are set in C#, so the style check does not see them; they are reviewed by hand:

- The wheel: element, aspect and angle colors in `ChartRenderer` and `ChartSheetRenderer`. Backgrounds and rings were moved onto the warm Jupiter neutrals in 4.0; the wheel background is `JupiterWindowColor`.
- The aspect grid in `WorksheetView`, which matches the wheel.
- The chart verdict, day tone and synastry tone colors, from `Lore.Core`. They are shared with the PDF exports and appear as 12-pixel marks beside their text, which always states the meaning. Green and red here are the content's own scale and not the status tokens.

**Tri-wheel.** The Trine view's wheel has no houses, since it would have to choose one person's; it is turned to the first chart's Ascendant. Its grand trine triangles are part of the wheel's content palette, with gold for a Golden trio's strongest.

**Reading text size (section 4).** Long-form text defaults to 16 / 26 and has four steps, set from the View menu and bound through `ReadingText`. `FontSize` and `LineHeight` are therefore bindings and not style values.

**Menu names (section 9).** File has New chart, Import My Charts and Export My Charts, and an Export submenu grouped by view. Help > Legend (F1) stands in for Documentation. Edit holds only Find, which moves focus to the Charts search.

**Splitter.** The Charts pane uses the toolkit `GridSplitter` at 8 pixels with the window background, as in Audio Bookshelf.

**Status bar progress.** A `ProgressRing` at the end of the status bar marks calculation, which is usually under five seconds but can run longer for a large library rescoring.
