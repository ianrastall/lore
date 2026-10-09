# Lore: where it stands and what to do next

Written 6 October 2026 against commit `4b6bc7f` (2.3.0 plus two rounds of fixes that
have not been released). This is a working note to come back to, not a promise. Each
item says what it is, why it is worth doing, where in the code it lives, and roughly
how big it is: **small** is an afternoon, **medium** a few days, **large** a release
of its own.

Tick items off or strike them out here as they are done or decided against, so the
note stays true.

## 1. Where Lore stands

- Seven views: Chart, Report, Worksheet, Daily, Forecast, Timing, Synastry, plus the
  Legend. About 10,600 lines of C# and XAML; no file over 560 lines.
- 330 tests (as of 2.8.0), all passing, covering the calculation and data layer and the view models. They run on
  GitHub on every push, followed by a build of the app itself (about 3 minutes).
- The views and the three export services are **not** tested: they
  depend on WinUI or Win2D and the test project leaves them out.
- 1,314 bundled figures (212 until 7 October 2026), all rated AA or A. How the
  1,102 were found and checked, and the long list of who was looked up and left
  out, is in `scripts/celebrity-expansion-2026-10-07.md`. Three corpora: 307 natal
  aspect lines, 585 daily transit lines (none empty), 144 synastry lines.
- Most of stages 1 and 2 of `natal-chart-metrics-research.md` shipped in 2.2.0.
  That document describes the code as it was before 2.2.0; its "correctness issues"
  section (third house, thresholds of 27 and −1, declination defaulting to zero) is
  out of date and those are fixed.

## 2. ~~Do first: release what is already on main~~ (done)

**2.3.1, a fixes release. Small.** Built and committed 6 October 2026; the installer, portable zip and release notes are in `artifacts\`. Still to do by hand: push, and publish the GitHub release. It carried:

- the forecast finds both exact dates when a planet turns round on a natal point;
- two open Lore windows no longer overwrite each other's saved charts;
- the solar return changes over on the reader's own calendar day;
- hospital search matches words across name, town and country;
- an overtaken forecast or match search stops instead of running on.

To do: bump the three version fields in `Lore.csproj`, the version and file names
at the top of `README.md`, add a 2.3.1 entry to the Version History, run
`release.bat`.

## 3. Small things worth doing soon

Each is independent and low risk. Items 1 to 6 were done on 6 October 2026 and are
in 2.4.0; item 7 is still open.

1. ~~**City search by several words.**~~ Done; `CityServiceTests` added.
   **Was:** `CityService.Search` still treats the whole
   query as one string, so "Paris France" or "Springfield Illinois" finds nothing.
   Copy the word-by-word matching now in `HospitalService.Search`, keep the
   population ranking, add a test (there is no `CityServiceTests` yet, and
   `CityService.cs` is not in the test project's file list).
2. ~~**Run the tests in `release.bat`.**~~ Done. **Was:** A release can currently be built from code
   that fails its tests. One `dotnet test` line before the installer step.
3. ~~**One place for the version number.**~~ Done: `<Version>` in `Lore.csproj` only (the README's file names are still typed by hand). **Was:** It is written three times in `Lore.csproj`
   and by hand in the README; the scripts and `installer\Lore.iss` carry a stale
   `1.1.0` fallback. Keep `<Version>` only and derive the other two; make the
   scripts fail rather than fall back.
4. ~~**Load the hospital list when it is first needed.**~~ Done (`MainViewModel.EnsureHospitalsLoadedAsync`). **Was:** `MainViewModel.InitializeAsync`
   reads all 25 MB (235,000 entries) at every start, though it is only used inside
   the Add Chart dialog. Loading it on the dialog's first open shortens start-up and
   saves memory for everyone who never adds a chart. `HospitalService.LoadAsync`
   is already safe to call twice.
5. ~~**Export and import My Charts.**~~ Done: the ⋯ button beside Add chart; `UserChartService.ExportAsync` (was `ExportBytes`) and `ImportAsync`. Only entries in the My Charts category are brought in. **Was:** The only way to move saved charts to another PC
   is to find `%LOCALAPPDATA%\Lore\mycharts.json` by hand. Two menu items: save a
   copy, and merge a file in (by `Id`, through `UserChartService` so the same
   validation and backup apply). This is the most useful missing feature for
   someone with family charts entered.
6. ~~**Forecast and Timing as PDF.**~~ Done, in `DailyExportService`, which is now in the test project. **Was:** Both export as text only, while Daily and
   Synastry have PDFs. `DailyExportService` is a close model for both.
7. ~~**Uneven categories.**~~ Done on 7 October 2026 by the large batch of figures:
   the smallest categories are now Entrepreneur and Spiritual with 37 each and
   Historical with 40. **Was:** Spiritual has 1 figure, Media 2, Director and
   Entrepreneur 4 each, Historical and Royalty 5. Either fold the tiny ones into
   neighbours or fill them in the next batch of figures.
8. **What the larger library leaves to do.** Built as 2.6.5 on 7 October 2026; the
   GitHub release is still to be published by hand. Three things
   follow from having six times as many figures:
   - 38 figures use `utcOffsetFixed` because Astro-Databank's clock standard for
     that town and day differs from the time-zone database (listed in the
     expansion note). The Worksheet calls such an offset "set by hand"
     (`BirthTimeResolver.Explain`), wording meant for a reader's own charts; a
     bundled figure would read better as "as Astro-Databank records it".
   - A third of the additions were born in France, because French records state
     the hour. If a more even spread is wanted, the next batch should look
     outside France and the United States.
   - `SynastryScoringTests.The_bands_keep_their_proportions_across_the_library`
     compares every pair (862,641) and now takes about 50 seconds. A fixed sample
     would bring the test run back to a few seconds.

## 3a. The review behind 2.7.0

On 7 October 2026 a Codex review of the whole program listed 22 findings; 2.7.0
(written the same day; to be committed, built with `release.bat` and published) acts
on most of them. The release notes in the README say what changed. The tests went
from 283 to 294. What was left, and why:

- **Every view is still calculated on every selection** (finding 10). Section 6
  explains why; that reasoning stands. Work overtaken by a newer selection is now
  cancelled in more places (the chart load and the birth-time check as well as the
  forecast and the match search), and adding or editing one chart still rescores the
  whole library (0.4 seconds, in the background).
- **The wheels for the exports are still drawn on the UI thread** (part of finding
  9). Only the PDF layout moved to the background. The drawing code shares Win2D text
  formats with the wheels on screen; moving it needs a look at what Win2D allows off
  the UI thread.
- **`swe_close` is still not called at exit** (part of finding 16). Windows frees
  everything when the process ends, and calling it while a background calculation
  might be running is the riskier choice. Setting the ephemeris path is now under the
  same lock as the calculations.
- **View models are free of WinUI types but still not tested** (finding 17 and
  section 5.2). The brushes are gone (`ScoreColorHex`, `ToneColorHex`; the views make
  the brush) and `FormatAngles` is now `NatalChart.AnglesLine`, so the view models
  could be added to the test project. They have not been: that is the next step.
- **No package lock files or `global.json`** (part of finding 20). The versions are
  now exact in both project files, which gives the same result with less to maintain.
- **The log file is still in `%TEMP%`** (part of finding 21), now capped at two files
  of half a megabyte.
- **No automated test of `assert-clean-release.ps1`** (part of finding 14). It was
  tried by hand against a clean stage, one with a `mycharts.json.bak` and one with a
  substituted library; the last two are refused.

New things worth knowing when working in this code:

- `ChartService.With(settings)` gives a service whose settings cannot change. Use it
  for anything that makes more than one calculation for one result.
- `UserChartService` holds `mycharts.json.lock` for the length of each save.
- `SettingsService.SaveUi` and `SaveHome` write in the background; `Flush` waits for
  them (the main window calls it on closing, and the tests call it).
- `MainViewModel.LoadChartAsync` works out the birth-time check beside the chart and
  hands both to `ChartViewModel.Show`.

## 3b. The review of 2.7.0

On 9 October 2026 a second Codex review (kept in `artifacts\reviews\`, which git
ignores) listed 16 findings against 2.7.0: 12 moderate, 4 low. All 16 were checked
against the code and acted on the same day. They went out with the measurements of
section 3c as **2.8.0** (written 9 October 2026; to be committed, built with
`release.bat` and published). The repairs alone took the tests from 294 to 315.

What changed, by finding:

1. and 12. **Saved charts.** `UserChartService` now tells a file it could not open
   (in use by another program) from one whose contents are damaged. Only a damaged
   file is set aside and the backup restored, and only while holding
   `mycharts.json.lock`. A file that is merely busy is left alone, tried four times,
   and read afresh by the next save.
2. **Export while another chart loads.** `MainViewModel.ChartIsCurrent`; every export
   is refused while it is false. A chart that fails to calculate now empties the views.
3. **Settings changed quickly.** An overtaken `RebuildCoreAsync` returns before
   touching the list; every caller of `RebuildPoolAsync` waits for the latest pass,
   which puts the selection back itself (`Reselect`).
4. **Patterns across a sign boundary.** `AspectPattern.Element` and `Modality` are
   null unless every point shares them; the report then says "across mixed signs" and
   adds no element or modality reading. The figure itself is kept.
5. **Exporting My Charts** reads the file under the lock (`ExportAsync`), so it has
   charts saved in another window.
6. **Day or night near the poles.** `NatalChart.SunAboveHorizon`, from the Sun's
   altitude at the place the chart is cast for (so a relocated chart or a return cast
   elsewhere uses that place).
7. **Solar return at the turn of the year.** `SolarReturnInForce` looks at the returns
   labelled next year to two years back and takes the latest that has happened.
8. **A forecast pass broken by a station** outside the orb is now two passes.
9. **Unknown time with a time still written** is cast for noon (`Celebrity.GetBirthTime`).
10. **Birth-time check across a clock change** reads each time off the instant
    (`BirthTimeResolver.Clock`).
11. **A chart saved under a search that hides it** clears the search (`ShowSaved`).
13. **The Timing view's place** is kept when the same person is recalculated.
14. **True Lilith** is marked retrograde when it is (`PlanetPosition.TruePoint`).
15. **A skipped calendar day** (Samoa, 30 December 2011): `BirthTimeResolver.StartOfDay`,
    used wherever a day's end is worked out.
16. **Solar-return PDF** no longer draws the natal verdict rim.

Decided differently from the review, and why:

- **The chart on screen is not blanked the moment someone else is picked** (finding
  2 suggested it), nor when the selection is dropped. Typing in the search box drops
  the selection on every keystroke, and blanking would empty the window each time.
  The harm was the export, and that is what is blocked.
- **The node is never marked retrograde, true or mean** (finding 14 was about Lilith).
  Going backwards is the node's ordinary motion.
- **An imported record with a time beside "time unknown" is not rewritten**; the time
  is simply ignored. Editing and saving the chart clears it.
- **The birth-time check's window gives clock times only**, with no date or offset
  added when it crosses midnight or a repeated hour.

The view models are now in the test project (`ViewModelTests`, with a one-thread
message queue, `UiThread.Run`, standing in for the window). That closes section 5.2
and the open item in 3a.

Seen in passing and left: "Saved changes to …" in the status bar is overwritten by
the recalculated chart's (usually empty) ephemeris note a moment later.

## 3c. The measurements in 2.8.0

On 9 October 2026 the Worksheet was checked against the inventory in
`natal-chart-metrics-research.md` and everything practical that was missing was added.
The tests stand at 330 (`FurtherMeasurementTests`). What was built, and where:

- **Pure arithmetic on a calculated chart**, in `NatalMetricsService` and `NatalMetrics`:
  right ascension, distance, altitude and azimuth (`Sky`; `Horizon` is the formula);
  swift or slow against Lilly's averages, Sun to Saturn (`Motion`); quadrants, counts
  per house and the spread of the ten planets (`Distribution`, `Spread`); midpoints and
  the points standing on them (1°); antiscia and contra-antiscia (1°); the almutens of
  the Sun, Moon, Ascendant, Midheaven and Fortune (`DignityService.RulershipPoints`);
  triplicity rulers on each `Disposition`; the Anti-Vertex.
- **Looked for in the ephemeris**, in the new `NatalEventsService` and `NatalEvents`:
  the New and Full Moon before birth (and whether either was an eclipse), each planet's
  station before and after birth, and the planetary day and hour. About 10 ms a chart.
  `MainViewModel.LoadChartAsync` works them out in the background and sets
  `NatalChart.Events`; a chart calculated anywhere else has none, and the Worksheet
  then leaves those lines out. (So does a JSON or XML file, though in practice every
  export is of the chart on screen, which has them.)
- **`NatalChart.GeoLatitude` and `GeoLongitude`**: the place the angles were taken for.
- **`NatalPoint.Spirit`**, aspected on the Worksheet like the Vertex and Fortune
  (`WorksheetService.PointAspects`, which the JSON and XML exports now use too).
- **Kite and mystic rectangle** in `AspectPatternService`, with wording in
  `ChartInterpreter`. Across the library at the standard orbs there are 1,043 kites and
  491 mystic rectangles: common, because the orbs are wide.
- **The chart-with-tables PNG** gained Motion, Equator and horizon, Midpoints and
  Almutens (`ChartSheetRenderer` picks sections by title).
- **Eight Legend entries** under *Worksheet measurements*.

Left out on purpose, with reasons:

- **Uranus, Neptune and Pluto are not rated swift or slow.** Seen from the Earth they
  hardly ever move at their average, so every chart would call them swift.
- **Midpoints count the axis only** (conjunction and opposition), not Ebertin's 45°
  and 90° contacts, and there is no midpoint tree.
- **One almuten convention** (sect ruler of the triplicity only). No chart-wide
  almuten figuris: it needs the prenatal lunation's degree and the day and hour
  rulers weighed in, and a stated recipe.
- **No chart-shape names** (bundle, bowl, bucket…): the spread is given as a number.
- **Altitude is geocentric and without refraction**, as the Ascendant is. Topocentric
  positions would be a setting of their own.
- **The dignity score is untouched.** Swift and slow are shown but not scored; scoring
  them would mean redrawing the verdict bands.
- **Not checked by running it:** the JSON and XML exports with the new fields (they are
  built in `ExportService`, which the tests cannot reach), and the Worksheet view and
  PDF on screen. The plain-text worksheet and the chart-with-tables image were looked at.

Still open from the research note: mundane (fractional) house position, quantified
uncertainty ranges, library percentiles, exaltation and mixed receptions, and the
specialist modes listed under 4.8.

## 4. Features, in the order I would build them

### 4.1 ~~A wheel for the Daily view (transit bi-wheel).~~ Done in 2.5.0.

`ChartRenderer.DrawTransitWheel`, drawn from `DailyReading.Midday` and `Events`; in
the daily PDF and as its own PNG export. The Moon's lines are drawn from its midday
place, not from where it was at the exact contact (the Legend says so).

**Was:** The Daily view is text only; Chart, Timing and Synastry all have a wheel. The
bi-wheel drawing already exists for Synastry (`Views/ChartRenderer.cs`,
`SynastryView.xaml`): natal chart inside, a second set of planets in a band
outside, contacts as lines. Feeding it `DaySky.Midday` in place of a partner's
chart gives "today's sky around your chart", with the transits the reading uses
drawn heavier. Mostly wiring, little new astronomy. Add a PNG export to match.

### 4.2 ~~Choose where a solar return is cast for.~~ Done in 2.5.0.

`ReturnPlace` on `TimingService.SolarReturn` and `Compose`; the box is in
`TimingView`, the choice in `TimingViewModel.Place`. It is not saved: it goes back to
the birthplace when another chart is picked or Lore is restarted. Remembering a place
per chart (and per year) would be the next step if it is wanted.

**Was:** `TimingService.SolarReturn` always casts the return for the birthplace. Many
astrologers cast it for where the person was living that year, and the return's
Ascendant and houses (the point of it) change completely with the place. The city
search already exists; the Timing view needs a "cast for" box and
`SolarReturn` a latitude and longitude. Say plainly which place was used.

### 4.2a ~~Parity with Astro-Seek's chart image.~~ Done in 2.5.0 (asked for on 6 October 2026).

Compared with an Astro-Seek PNG (true node, true Lilith, whole sign): every position
agreed to the minute. Added what it had and Lore lacked: the true (osculating) Lilith
as a setting (`LilithType`), quintile and biquintile among the minor aspects, aspects
to the Vertex and Part of Fortune (Worksheet only: `ChartService.AspectsToPoints`),
the stationary mark (`PlanetPosition.IsStationary`, a table of speed thresholds), the
*By degree* and *Elements and modes* Worksheet tables, and the export **PNG — chart
with tables** (`Views/ChartSheetRenderer.cs`).

To look at that image without clicking through a save dialog, a debug build writes it
for the first chart it opens when `LORE_SHEET_OUT` is set to a file path.

Not done, and worth knowing: Astro-Seek also aspects the Vertex, Fortune and the angles
to each other, and counts the Ascendant and Midheaven in its element table; Lore does
neither. Its minor-aspect orbs are a little wider than Lore's 2°. The JSON and XML
exports do not carry the Vertex and Fortune aspects or the alternate node and Lilith.

### 4.3 More house systems. Small.

`HouseSystem` has four. Regiomontanus, Campanus, Porphyry and Alcabitius are one
letter each to the Swiss Ephemeris and the settings, labels and polar fallback
already handle a list. Extend `SwetestReferenceTests` and regenerate
`swetest-reference.json` (needs `swetest64.exe`) so each new system is checked.

### 4.4 Lunar return and the monthly rhythm. Medium.

The Timing view has the yearly return only. A lunar return (the Moon back at its
natal place, every 27.3 days) reuses the same bisection as `SolarReturn` with the
Moon as the body, and gives the Timing view something that changes month to month.
Left out for charts with no birth time, as the solar return is.

### 4.5 Daily horoscope, second pass. Medium.

Ideas set aside when 1.3 was built (the outside research is not in the repo; see
the note at the end):

- **More than one wording per transit**, chosen by date so the same slow transit
  does not read identically for six weeks. Corpus work more than code.
- **Slow transits as one story.** The Forecast already knows a pass can be exact
  three times; the Daily view treats each day alone. Saying "the second of three
  passes" needs the Daily view to ask the Forecast's question for that transit.
- **Simultaneous transits that pull opposite ways** get a linking sentence rather
  than two unrelated paragraphs.
- **A chosen time zone** for the day being read, instead of always the PC's.

### 4.6 Synastry, second pass. Medium to large.

- **Composite chart** (midpoints of the two charts), the other standard
  relationship technique. Needs circular midpoints, which section 4.8 also wants.
  (The Davison chart, its cousin, is in 2.6.0.)
- **Text export** of the reading (PDF and PNG only today).
- **House overlays for more than four planets** (`overlayPlanets` has Sun, Moon,
  Venus, Mars).
- **A partner who is not saved**: today the second person must be in the library
  or in My Charts.

### 4.7 Finding people in the list. Small to medium.

The browse list sorts by name and filters by category and text. Useful additions:
filter by Sun, Moon or Rising sign; filter by verdict; sort by birth date. All the
values are already computed in `MainViewModel.ComputeVerdicts`; keep them on the
`Celebrity` alongside the verdict tint.

### 4.8 The rest of the metrics research. Each small to medium; do on request.

Still open from `natal-chart-metrics-research.md`:

- ~~stations near birth; swift or slow motion; midpoints and antiscia; kite and mystic
  rectangle; altitude and azimuth; the Moon's age and the lunation before birth.~~
  Done in 2.8.0 (section 3c);
- the Lilly factors still left out of the dignity score (swift and slow, partile
  conjunctions, oriental/occidental, waxing Moon). Any change to the score needs the
  verdict bands redrawn and a note in the release, as 2.2.0 did for the third house;
- harmonic aspects beyond the quintile and biquintile added in 2.5.0 (three colour
  tables are indexed by enum order, in `ChartRenderer`, `WorksheetView` and
  `ChartSheetRenderer`: add to all three);
- the asteroids Ceres, Pallas, Juno, Vesta (check the bundled `seas_*.se1` files
  cover them before promising);
- sidereal zodiac, fixed stars, harmonic and draconic charts: each a mode of its
  own, large, and only worth it if asked for.

### 4.9 A personality inventory beside the chart. Large. Undecided.

An idea from 6 October 2026, written up for an outside opinion in
`personality-inventory-proposal.md`: the reader sits a public-domain questionnaire
(IPIP-NEO-120) and a new view compares what the chart describes with what they
report. Not to be started until that opinion is in and Ian has said yes.

### 4.10 ~~Ideas from Kerykeion.~~ Done in 2.6.0.

On 6 October 2026 Lore was compared with Kerykeion (a Python astrology library,
AGPL-3.0 like Lore; local copy at `D:\dev\proj\kerykeion`). Ideas only, no code taken.
Ian made the gaps the list for 2.6.0. What was built, and where:

- **Annual profections.** `TimingService.Profect`; a section of the Timing reading.
  Whole signs from the rising sign, traditional rulers, birthday to birthday (29
  February falls on 1 March in other years). The house topics come from `daily.json`
  through `DailyInterpreter.HouseTopic`, handed to `TimingService` as a function.
- **Solar arc directions.** `TimingService.Direct`; shares `Targets` and `Contacts`
  with the progressions.
- **Relocated chart.** `TimingService.Relocate`; a last section of the Timing reading
  when a place is chosen. It uses the same place as the solar return, so there is one
  box for both. A separate place for each, or a wheel for the relocated chart, would be
  the next step if wanted.
- **Void-of-course Moon.** `TransitService.VoidOfCourse`, in `DaySky.Voids`; written
  under *Also in the sky*. Major aspects to the Sun and Mercury to Pluto, exact, no orb.
- **Sky calendar.** `TransitService.SkyCalendar` and `ChartService.NextEclipse` (two
  new Swiss Ephemeris imports); `ComposeForecast` takes the events and sets them among
  the passes. Wording in the new `calendar` section of `daily.json`. Checked against
  the almanac for 2026 in `SkyCalendarTests`.
- **Dominant planets and weighted balance.** `NatalMetricsService.Dominants`; two
  Worksheet tables and a `Dominants` block in the JSON and XML. The weights are in the
  comment above the method and in the Legend. Across the timed figures the dominant
  comes out as Mercury 41, Jupiter 37, Saturn 28, Mars 27, Moon 25, Sun 22, Venus 22,
  Pluto 6, Uranus 3, Neptune 1: nothing runs away with it. Not yet shown in the Report,
  the Chart view or the chart-with-tables PNG.
- **Davison chart.** `ChartService.Davison`; a last section of the synastry reading.
  Stated, not interpreted, and not drawn as a wheel.

- **Planetary hours, and where the reader is.** Ian said yes to a saved place on
  6 October 2026. `HomePlace`, kept in `home.json` beside `settings.json`
  (`SettingsService.LoadHome` / `SaveHome`) and set in the Settings flyout;
  `MainViewModel.Home` hands it to the Daily view model. `ChartService.NextSunriseOrSet`
  (a `swe_rise_trans` import) and `TransitService.PlanetaryHours`; a last section of
  the daily reading. Wording in `planetaryDays` and `notes.planetaryHours` in
  `daily.json`. Sunrise and sunset checked against London's almanac times. The place
  is used for nothing else yet; it would also serve a chosen time zone for the day
  (4.5), rise and set times of the Moon, or a default place for relocation.
Looked at and not recommended for Lore: sidereal modes, Vedic nakshatras, heliocentric
and other viewpoints, Uranian points, trans-Neptunian bodies, Gauquelin sectors,
astrocartography, primary directions, zodiacal releasing, firdaria, horary, heliacal
events and occultations. Each is a specialist mode with its own audience and, for a
reading, its own corpus. Topocentric positions (the Moon moves by up to a degree) could
be a setting if anyone asks.
## 5. Keeping it sound

1. **Test what the exports write.** `ExportService` builds the JSON and XML
   objects and renders in the same class, so none of it is testable. Splitting the
   plain data-building half from the Win2D and QuestPDF half would let the tests
   check that the worksheet, JSON and XML agree on the same numbers. Medium.
2. **View models without WinUI types.** `ChartViewModel`, `DailyViewModel` and
   `SynastryViewModel` return `SolidColorBrush` and each has its own copy of
   `ParseHex`. Returning the hex string and using the existing `HexBrushConverter`
   in XAML removes the duplication and makes all the view models testable
   (stale-result handling, cancellation, the export-readiness checks). Medium.
3. **Accessibility.** The window is fixed to the dark theme, there are only ten
   `AutomationProperties` names in all the XAML, and the wheels are canvases with
   no text equivalent (the planet list beside the wheel partly covers this). A pass
   with Narrator and with Windows high-contrast on would show what is missing.
   Medium.
4. **A release from GitHub.** CI already builds and tests; a second workflow run on
   a version tag could build the installer and portable zip and attach them to the
   release, so a release no longer depends on one PC's setup. It needs Inno Setup
   on the runner and `assert-clean-release.ps1` to work without local history.
   Medium. Only worth it if releasing by hand becomes a chore.
5. **README counts.** "1,314 figures", "585 lines", "144 lines" and "307 lines" are
   typed by hand in several places and drift each release. A test that reads the
   README and compares would catch it; or say "over 200".

## 6. Known limits, left as they are on purpose

Checked on 6 October 2026 and judged not worth changing. Recorded so they are not
investigated again from scratch.

- **Old Style 29 February** in 1300, 1400, 1500, 1700, 1800 and 1900 cannot be
  entered: `Celebrity.GetBirthDate` parses the recorded date as Gregorian and the
  date picker has no such day. Workaround: enter the modern date with Old Style
  unticked. A fix must change the picker as well as the parsing.
- **Daily and Forecast do not repeat the missing-ephemeris warning.** The status
  bar gives it when the chart loads, and the built-in fallback always supplies the
  Sun and Moon, so the "defaults to New Moon / Aries" branches in
  `TransitService` cannot be reached in practice.
- **Every view is calculated on every selection**, not only the one on screen.
  Measured: daily scan 17 ms, 91-day forecast 117 ms, year forecast 489 ms, match
  search against 212 charts 82 ms, all off the UI thread, and now cancelled when
  overtaken. (With 1,314 figures the match search has not been timed again; by
  proportion it would be about half a second. Scoring every chart at start-up was
  measured at 0.4 seconds.) Calculating only on demand would also break exporting the daily
  horoscope from another view.
- ~~**The birth-time check runs on the UI thread.**~~ Moved to the background in
  2.7.0. (It was measured at 65 ms for the widest margin, 37 ms for a whole-day scan.)
- **UTC is passed to the ephemeris as UT.** The difference is under a second.
- ~~**Package versions float.**~~ Fixed at exact versions in 2.7.0; update them by
  hand in `Lore.csproj` and `tests\Lore.Tests\Lore.Tests.csproj`, then run the tests.
- **`hospitals.json` (25 MB) is in git.** Rebuilding it takes an hour or more
  against busy public servers, so the finished file stays in the repository.
- **AGPL-3.0** is required by the Swiss Ephemeris and is not a choice.
- **No text is generated at run time.** Every sentence comes from the three
  corpora. This is a design decision, not a gap.

## 7. Suggested order

1. ~~Release 2.3.1 (section 2).~~ Built and committed; push and publish by hand.
2. ~~The small items in section 3, as one release.~~ Written as 2.4.0; to be
   committed, built with `release.bat` and published.
3. ~~The Daily wheel (4.1) and the solar return's place (4.2).~~ Written as
   2.5.0 on 6 October 2026; to be committed, built with `release.bat` and published.
4. ~~The Kerykeion list (4.10).~~ Written as 2.6.0 on 6 October 2026; to be committed,
   built with `release.bat` and published.
4a. ~~The review's repairs (3a).~~ Written as 2.7.0 on 7 October 2026; to be committed,
   built with `release.bat` and published.
4b. ~~The second review's repairs (3b) and the missing measurements (3c).~~ Written as
   2.8.0 on 9 October 2026; to be committed, built with `release.bat` and published.
5. House systems (4.3) and the lunar return (4.4).
6. Then whichever of 4.5 to 4.8 is wanted, with section 5's test work done
   alongside the feature that touches the same code.

---

*Outside research behind the daily horoscope (not in the repository):*
`D:\all\cc\events\Programmatic Horoscope Generation Logic.md` *and*
`C:\Users\Ian\Documents\Codex\2026-10-02\i-need-a-serious-deep-dive\outputs\`.
