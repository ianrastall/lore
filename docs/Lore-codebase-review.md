# Lore codebase review — 11 October 2026

Reviewed revision: `4f63d35` (version 3.2.0). This review covers application structure, calculation and interpretation services, persistence, view models, export orchestration, bundled data, tests, and build/release configuration. No application source files were changed.

**Assessment:** the calculation core has useful boundaries and substantial reference coverage. The most urgent problems are in the newer personality inventory: answer durability, export identity, and uncertainty handling. Refactoring should follow those fixes, not delay them.

**Validation:** all **381 tests passed**, with no skipped tests. The Release application build completed with **zero warnings and zero errors**. Additional checks used synthetic people and separate temporary storage; they did not use personal chart or questionnaire files. [Reproduction results](D:/dev/proj/lore/artifacts/review-20261011/reproductions.txt) and the [test results](D:/dev/proj/lore/artifacts/review-20261011/lore-review-20261011.trx) are retained.

The numbered findings below are ordered by severity and practical repair priority. P1 means high priority because of data loss or unintended disclosure; P2 means a correctness, reliability, or significant structural problem; P3 means a lower-priority presentation or maintenance concern. Findings explicitly marked “structural risk” or “editorial concern” are not claims of a reproduced runtime failure.

## 1. P1 — Personality exports can use another person's current profile

**Evidence:** [MainWindow.xaml.cs:455](D:/dev/proj/lore/MainWindow.xaml.cs:455), [MainWindow.xaml.cs:505](D:/dev/proj/lore/MainWindow.xaml.cs:505), [MainWindow.xaml.cs:542](D:/dev/proj/lore/MainWindow.xaml.cs:542).

Most export inputs are captured before awaiting the file picker. The inventory profile is captured only for validation; the actual `inventorytxt` and `mirrortxt` branches later call methods on the live `InventoryVM`, inside background work. If the chart or profile changes while the dialog is open or before that worker runs, the filename can identify person A while the contents describe person B. A cleared profile can also yield an empty file reported as successfully exported.

The synthetic reproduction captured “New Name,” changed the view to “Other Person,” and obtained a personality export headed “Other Person.” This reproduces the state dependency; the native file-picker interaction itself was not automated.

**Fix:** capture immutable profile/reading snapshots, or their export bytes, before opening the picker. Export only those snapshots, and avoid reading UI-owned state from background workers. Add a test that changes the selected chart while the picker is pending and verifies the exported identity and contents remain unchanged.

## 2. P1 — Simultaneous questionnaire saves overwrite other people's answers

**Evidence:** [InventoryStore.cs:54](D:/dev/proj/lore/Services/InventoryStore.cs:54), [InventoryStore.cs:105](D:/dev/proj/lore/Services/InventoryStore.cs:105), [InventoryTests.cs:224](D:/dev/proj/lore/tests/Lore.Tests/InventoryTests.cs:224).

Each store instance has its own queue and lock. Nothing coordinates the read–modify–replace transaction across separate windows. Two windows can read the same old file and each replace it with its own changes; the last writer loses the other window's answers. Atomic file replacement prevents partial JSON, but does not prevent lost updates.

In 30 stress-test trials with eight independent stores saving distinct synthetic people simultaneously, every trial lost answers. The first five retained only one of eight people. The existing two-window test flushes the first window before saving from the second, so it tests sequential merging rather than concurrency.

**Fix:** reuse the cross-process lock-file transaction already implemented in `UserChartService`. Hold it across re-read, validation, merge, and replacement. Add overlapping writer tests, including edit/delete conflicts and separate processes.

## 3. P2 — Failed answer saves and deletions look successful

**Evidence:** [InventoryStore.cs:54](D:/dev/proj/lore/Services/InventoryStore.cs:54), [InventoryStore.cs:129](D:/dev/proj/lore/Services/InventoryStore.cs:129), [InventoryViewModel.cs:232](D:/dev/proj/lore/ViewModels/InventoryViewModel.cs:232).

`Save` updates the in-memory dictionary immediately. Later I/O failures are logged and swallowed, with no result or error signal to the view. The interface promises that each answer is saved, but can continue displaying answers that have never reached disk. The same problem affects deletion: answers can disappear from the current view and return after restart.

Reproduction: hold the answer file with exclusive access, save a new person's answers, and flush. The current store reports that the person has a sitting; a fresh store after releasing the lock reports none.

**Fix:** expose pending/saved/failed status and awaitable persistence results. Preserve unsaved answers for retry, show a clear failure, and distinguish removal from the current view from confirmed deletion on disk. Avoid silently timing out during shutdown without communicating unsaved work.

## 4. P2 — Damage discovered after startup is overwritten without preservation

**Evidence:** [InventoryStore.cs:86](D:/dev/proj/lore/Services/InventoryStore.cs:86), [InventoryStore.cs:116](D:/dev/proj/lore/Services/InventoryStore.cs:116).

Startup attempts to preserve unreadable data, but the later write path handles a JSON parsing failure by copying the current in-memory dictionary and replacing the damaged file. That destroys potentially recoverable content, including other windows' data absent from this instance's memory. Startup also suppresses failure to preserve the damaged file without setting a save-blocked state.

Reproduction: create a store, replace its file with damaged text after startup, then save another answer. The damaged text disappears and no `inventories.unreadable-*` copy exists.

**Fix:** use the same fail-closed policy as saved birth charts. Preserve the original under a unique name or refuse to save; keep a last-good backup and surface recovery status. Do this under the cross-process lock from finding 2. Test corruption both before and after startup and a failure to move the damaged file.

## 5. P2 — Chart-and-answers readings treat an uncertain Moon sign as definite

**Evidence:** [MirrorInterpreter.cs:159](D:/dev/proj/lore/Services/MirrorInterpreter.cs:159), [MirrorInterpreter.cs:171](D:/dev/proj/lore/Services/MirrorInterpreter.cs:171), [MirrorInterpreter.cs:428](D:/dev/proj/lore/Services/MirrorInterpreter.cs:428), [mirror.json:7](D:/dev/proj/lore/Data/mirror.json:7).

Unknown birth times are calculated at noon. The main Report can receive a list of possible Moon signs, and Daily/Synastry omit uncertain natal Moon contacts. `MirrorInterpreter` receives no equivalent uncertainty information. It assigns the noon Moon's sign, interprets it, compares questionnaire answers against that sign's supposed expectations, and can interpret Moon aspects too. Its untimed note discusses dignity scoring, not this sign uncertainty.

Reproduction: an untimed Chicago birth on 1 January 1990 has Aquarius or Pisces as possible Moon signs. The mirror reading unconditionally says “Moon in Pisces” and compares the person's answers against Pisces.

**Fix:** pass a shared uncertainty model into every interpreter. Omit uncertain Moon sign/aspect comparisons or present explicit alternatives. Apply the same rule to other bodies when the day-wide scan detects a sign change. Add cross-reading tests for the same untimed chart.

## 6. P2 — Essential reading content can be omitted from a release silently

**Evidence:** [Lore.csproj:85](D:/dev/proj/lore/Lore.csproj:85), [build-portable.ps1:35](D:/dev/proj/lore/scripts/build-portable.ps1:35), [ChartInterpreter.cs:58](D:/dev/proj/lore/Services/ChartInterpreter.cs:58).

The natal interpretation corpus is conditionally included only if it exists. The portable layout check does not require it. The interpreter accepts its absence and builds an empty corpus, producing fallback prose without an availability error. An incomplete source or installed directory can therefore yield a functioning application with a broken core reading feature.

Reproduction using a missing corpus produced: “Test carries distinctive in their own way. (Sun in Gemini.)” The normal release test gate helps when the source file is missing, but does not validate all files in the resulting package or a damaged installation.

**Fix:** make required content unconditional, maintain one manifest of required runtime assets, and verify it during publishing and startup. An unavailable corpus should disable the affected reading with an explanation rather than emit placeholder prose. Add a staged-package smoke test.

## 7. P2 — Questionnaire validation can crash startup or accept a malformed instrument

**Evidence:** [InventoryService.cs:40](D:/dev/proj/lore/Services/InventoryService.cs:40), [App.xaml.cs](D:/dev/proj/lore/App.xaml.cs).

Deserialization catches malformed JSON, but structural validation runs afterward and assumes non-null collections and elements. A syntactically valid file containing `{"facets":null}` throws `ArgumentNullException`. Since the service is constructed while creating the main window, a damaged optional questionnaire can prevent the application from opening normally. The availability check also does not enforce the expected 120 items, 30 unique facets, five unique domains, four items per facet, or six facets per domain.

**Fix:** validate the full instrument before exposing it; reject nulls, duplicate keys, invalid cardinalities, and nonfinite reference statistics. Keep a failure local to the questionnaire. Add mutation tests against malformed but valid JSON, rather than testing only invalid JSON and an unknown facet.

## 8. P2 — Renaming a chart leaves its personality profile under the old name

**Evidence:** [InventoryViewModel.cs:232](D:/dev/proj/lore/ViewModels/InventoryViewModel.cs:232), [InventoryViewModel.cs:374](D:/dev/proj/lore/ViewModels/InventoryViewModel.cs:374).

When the chart changes but retains its ID, the fast path updates the title and mirror reading while retaining the old `InventoryProfile`. That profile contains an immutable name. The visible heading and exported profile can consequently disagree.

Reproduction: rename a synthetic chart from “Old Name” to “New Name” with the same ID. The view title changes; the personality export still begins “Old Name — Personality profile.”

**Fix:** rebuild the profile from the completed sitting on metadata changes, or separate scored answers from the current person's display metadata. Test a rename with a completed sitting and with a sitting in progress.

## 9. P2 — Julian dates are parsed as Gregorian before conversion

**Evidence:** [Celebrity.cs:82](D:/dev/proj/lore/Models/Celebrity.cs:82), [BirthTimeResolver.cs:28](D:/dev/proj/lore/Services/BirthTimeResolver.cs:28), [AddChartDialog.xaml.cs:80](D:/dev/proj/lore/Views/AddChartDialog.xaml.cs:80).

The recorded Old Style date must first survive `DateOnly.ParseExact`, which applies Gregorian date validity. Julian leap days in years such as 1700, 1800, and 1900 cannot be represented, even though the application explicitly supports Julian dates. A Gregorian date picker has the same representational limitation.

Reproduction: Lore rejects Julian `1700-02-29` with `FormatException`; the already-installed NodaTime library accepts it and converts it to Gregorian 11 March 1700. This is an unsupported edge of the advertised calendar feature, not evidence that current bundled figures are miscalculated.

**Fix:** parse year/month/day under the declared calendar, using NodaTime `LocalDate` or a dedicated recorded-date type. Convert to Gregorian only afterward. Provide an input mechanism that can represent Julian-only dates and test century leap days.

## 10. P2 — Expensive background work is eager and inconsistently cancellable

**Structural/performance risk. Evidence:** [MainViewModel.cs:598](D:/dev/proj/lore/ViewModels/MainViewModel.cs:598), [MainViewModel.cs:578](D:/dev/proj/lore/ViewModels/MainViewModel.cs:578), [DailyViewModel.cs:122](D:/dev/proj/lore/ViewModels/DailyViewModel.cs:122), [TimingViewModel.cs:127](D:/dev/proj/lore/ViewModels/TimingViewModel.cs:127).

Selecting a chart starts Daily, Forecast, Timing, and Synastry work regardless of which view is open. Synastry can calculate the other 1,313 bundled charts, and Forecast starts a three-month scan by default. Forecast and match search have cancellation, but Daily, Timing, natal-event computation, and library dignity rescoring do not consistently stop superseded work. Generation counters prevent stale results from being displayed; they do not stop the wasted calculations. Native calls also compete for the same global Swiss Ephemeris lock.

**Fix:** compute costly readings when their view is activated, cache by person/version/settings/date, and give multi-step calculations cancellation tokens. Preserve the native lock. Measure chart-selection and date-navigation latency on a modest machine before and after. No specific end-user latency figure is claimed by this review.

## 11. P2 — Test boundaries exclude the export workflow where identity bugs occur

**Structural/test risk. Evidence:** [Lore.Tests.csproj:35](D:/dev/proj/lore/tests/Lore.Tests/Lore.Tests.csproj:35), [ReadingPdfTests.cs:7](D:/dev/proj/lore/tests/Lore.Tests/ReadingPdfTests.cs:7), [InventoryTests.cs:224](D:/dev/proj/lore/tests/Lore.Tests/InventoryTests.cs:224).

The test project recompiles a manually maintained list of production source files instead of referencing a shared core assembly. It includes several document exporters, but excludes `MainWindow`, the main `ExportService`, rendering, and views. Some PDF tests check the file signature and size rather than document content. Thus a green suite does not establish that an export has the right person's data or the requested contents. The sequential multi-window test also gives incomplete coverage of the guarantee in its name.

**Fix:** extract `Lore.Core` and reference it from both the UI and tests. Separate export snapshot creation from the WinUI picker and renderer so it is testable without launching the interface. Add focused tests for identity changes across awaits, true overlapping persistence, extracted PDF text, and a small number of packaged-app smoke tests. Retain the existing astronomy/reference tests.

## 12. P3 — Several classes combine responsibilities that now change independently

**Structural maintenance risk. Evidence:** [MainViewModel.cs](D:/dev/proj/lore/ViewModels/MainViewModel.cs), [TransitService.cs](D:/dev/proj/lore/Services/TransitService.cs), [ExportService.cs](D:/dev/proj/lore/Services/ExportService.cs), [LegendView.xaml.cs:185](D:/dev/proj/lore/Views/LegendView.xaml.cs:185).

`MainViewModel` is 625 lines and coordinates selection, persistence, settings, scoring, inventory propagation, and every reading. `TransitService` is 817 lines covering daily scans, forecasts, sky events, void-of-course calculations, and planetary hours. `ExportService` is 804 lines combining GPU rendering, PDF layout, and structured data conversion. The Legend embeds roughly 153 KB of UI and reference text in one source file. Repeated generation/cancellation/busy-state patterns have already diverged across view models.

**Fix:** extract components along those responsibilities as the affected features are repaired. Start with a shared persistence transaction, an immutable export request, and a calculation coordinator. Move Legend entries into validated content data. Prefer these targeted extractions to a wholesale rewrite.

## 13. P3 — Percentile presentation omits important limits of its reference group

**Editorial concern. Evidence:** [Inventory.cs:137](D:/dev/proj/lore/Models/Inventory.cs:137), [Inventory.cs:148](D:/dev/proj/lore/Models/Inventory.cs:148), [inventory.json:9](D:/dev/proj/lore/Data/inventory.json:9).

Percentiles are estimated from a normal distribution using the stored mean and standard deviation; they are not empirical ranks counted in the source data. The user-facing wording says the person scores higher than approximately a number of people in 100, while the normal-distribution assumption appears only in a code comment. The reference description omits that the study sample consisted of US online volunteers aged 19–69. It is not a universally representative comparison group. The sample description is documented in the [original study](https://ejop.psychopen.eu/index.php/ejop/article/download/1671/1671.html?inline=1).

**Fix:** label these as estimated percentiles, state the reference population and age range, and avoid implying comparison against the user's own age/sex group unless those norms are implemented. The stored means and standard deviations for all five domains and thirty facets match Table A1; this concern is about presentation, not an observed arithmetic error.

## 14. P3 — UI and maintenance text have drifted from the implementation

**Evidence:** [InventoryView.xaml:15](D:/dev/proj/lore/Views/InventoryView.xaml:15), [docs/next-steps.md:15](D:/dev/proj/lore/docs/next-steps.md:15), [SynastryScoring.cs:58](D:/dev/proj/lore/Services/SynastryScoring.cs:58).

The Inventory header always says nothing there is taken from the chart, including when the user switches to the explicitly chart-based mirror reading. The roadmap says no file exceeds 560 lines, which several files now do. The compatibility calibration comment still refers to 22,366 library pairs; 1,314 figures imply 862,641 distinct pairs. The distribution test does run over the current library and passes, so the stale comment is not evidence that the current thresholds fail.

**Fix:** make the Inventory description follow the active mode, update or date-stamp architectural inventories, and generate counts or avoid embedding them in prose. Give content changes the same focused review as calculation changes.

## What the review did not find wrong

- Models, calculation services, interpreters, and view models are recognizably separated. Deterministic reading corpora are a useful design choice.
- Native calculation calls are serialized; settings snapshots and generation guards cover many asynchronous state changes.
- Saved birth charts have substantially stronger persistence than inventory answers, including cross-process locking and backup/recovery behavior. Reuse that work.
- All nine shipped top-level JSON data files parsed without duplicate object keys. The figure library contains 1,314 unique IDs and names, 14 categories, and valid coordinate ranges. Its stored ratings are 1,132 AA and 182 A.
- The 50,250 cities and 234,787 hospital rows all have coordinates in range. Two city rows duplicate a name and exact coordinates; there were no exact name/coordinate duplicates in hospitals. Repeated names at different locations were not treated as errors.
- Corpus-coverage and figure-reference tests passed. The questionnaire's stored reference statistics match the cited paper. The official [IPIP item source](https://ipip.ori.org/30FacetNEO-PI-RItems.htm) was inspected, but this review does not claim a fresh exhaustive independent verification of every item or historical birth record.

## Recommended repair sequence and limits

First repair export snapshots and the inventory transaction boundary (findings 1–4). Then repair uncertainty propagation and profile identity (5 and 8), followed by corpus integrity and calendar representation (6, 7 and 9). Add regression tests with each repair. Use those changes to introduce the small shared components in 10–12, then align the content in 13–14.

This was a source, build, test, data-consistency, and targeted reproduction review. It did not include interactive visual inspection of every WinUI view, installation on a clean Windows machine, a dependency vulnerability audit, or a fresh primary-source audit of all 1,314 biographies and birth records. Astro-Databank pages attempted during the review were blocked by their browser-access checks; existing local reference-test success should not be described as live re-verification of those records. No critical/P0 issue was established within this scope.
