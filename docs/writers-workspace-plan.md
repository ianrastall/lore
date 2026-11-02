# Writers workspace

Current direction: 1 November 2026. Ian authorized autonomous implementation,
testing and delivery of this work and the following corpus expansion. Version
5.0.0 is installed and verified; see [validation](validation-5.0.0.md).

## Binding direction

The new Writers tab has two uses:

1. Discover notable birth moments for fictional characters, using both sky events
   and distinctive local chart features. Read them through Lore's existing natal
   interpreter.
2. Find famous pairs and trios without selecting a starting person. Use the same
   synastry and Trine readings, measures, levels and ordering as elsewhere in Lore.

**No writing prompts of any kind.** Ian rejected the first prototype's scene
suggestions, character arcs, writer-directed advice and invented interaction
categories. Those were removed. Do not reintroduce them in the corpus expansion.

## Famous groups: all requirements are hard filters

- Every member must have died at least **fifty calendar years ago**. Ian explicitly
  chose time since death, not time since birth. On 1 November 2026 the inclusive
  cutoff is 1 November 1976. The application takes a local-date snapshot for each
  search and advances the cutoff with the date.
- Every member must be in the **same field**. Lore's current consistent field
  classification is the library category. Even when searching all categories,
  members within a group must have the same one. The interface states this basis.
- Birthplaces must be nearby: default **100 km**, adjustable from 1 to 1,000 km.
  Geographic proximity means birthplaces, as Ian confirmed, not inferred residence
  history. A trio must meet the distance limit on all three pairwise distances.
- The oldest and youngest must have been born within **ten calendar years**.
  Normalize Julian dates before comparing; a 29 February anniversary falls on
  28 February in a non-leap year. Do not average pairwise age differences.

Missing or partial death dates cannot establish the fifty-year floor and are
excluded. Missing field, birthplace coordinates or usable birth dates cannot pass
their requirements. Zero latitude/longitude are valid when explicitly recorded.
Birthplace proximity is never described as evidence that people actually met.

Field, biography/name/place search, birth-year range and minimum prominence can
narrow discovery. None can relax the requirements. Pair-level and trio-tone
filters use the existing labels.

## Character anchors

Choose a city, start date and a range of 7, 30, 90 or 366 days, within the
ephemeris-supported 1200–2400 period. Available anchors:

- New/Full Moons, eclipse lunations, stations and slow-planet sign ingresses.
- A selected planet from Sun to Saturn on the Ascendant or Midheaven, refined to
  an ecliptic-longitude residual below 0.001°.
- Chart patterns found at hourly samples, explicitly labelled as samples rather
  than exact formation times.
- A sky event and the selected planet within 3° of the Ascendant or Midheaven at
  that same place and moment. If the intersection is empty, show no matches.

Global events supply a moment; the chosen place supplies angles and houses. Keep
the exact UTC instant, including sub-minute precision, separately from display
clock time. Historical local mean time and DST use the existing resolver.
Eclipse results identify the exact lunation, distinguish it from greatest eclipse
and do not claim local visibility. Angle contacts do not claim physical horizon
visibility.

The brief is the existing natal reading plus the computed anchor and birth
details. A fictional character's choices and biography belong to the writer;
Lore supplies no scene, plot or character-development instructions.

## Search and standard readings

Build an eligibility graph in birth-date order, applying the field, death, age and
distance filters before chart comparison. Its edges are pairs and its triangles
are trios. Enumerate every eligible group, calculate each person's chart once per
settings snapshot, and cache pair assessments and trine-contact masks.

Pair order is the existing compatibility level, contemporaneity, easy share and
name order. Trio order is the existing Golden/Harmonious/Mixed/Strained tone,
contemporaneity, strongest grand-trine weight, easy share and name order. The
global pass is checked against the existing synastry and trio calculations.
Count every eligible group and retain up to 10,000 results, enough to show all
groups at the default distance with the current library and historical cutoff.
State both the result count and retained count for broader searches. No sampling
or hidden shortlist of people is used.

`WriterInterpreter` delegates to `ChartInterpreter` and `SynastryInterpreter`,
including `ComposeTrio`. It adds only anchor/provenance/eligibility data. New
writer-specific prose and compatibility scores are out of scope.

## Interface, persistence and delivery

Writers works without a selected chart and retains the main chart selection.
Ctrl+9 opens it. Its modes are Create a character, Find famous groups and Saved
briefs. Search controls collapse after a search. On a narrow window the selected
reading has its own detail view with a return to the results.

Named snapshots live in `%LOCALAPPDATA%\Lore\writer-briefs.json`, separately from
My Charts, including exact birth instants, settings, corpus version and composed
text. Saves merge under a cross-process lock and replace atomically with a backup.
A damaged store is left untouched. A pending/failed save is handled during close.
Reopen snapshots exactly as saved, preview their underlying charts and export
their actual reading as text or PDF. Private Writer stores are forbidden by the
release gate.

Use isolated debug profiles for all screenshots and UI checks. Run core tests,
app/style builds, packaged self-check and personal-data gate; build an installer,
upgrade the installed copy with data checksums, verify registration/uninstaller
and launch through the Start Menu shortcut. Ian authorized windows and installers
for this autonomous Writers/corpus work; no further supervision is required.

## Following iteration: broader reading corpus

Expand the authored range of the normal natal, Daily, synastry, Davison, Trine and
self-report readings. Keep their meaning, scoring, evidence, names and offline
determinism. Measure repetition over reproducible batches, author substantively
different treatments, improve coverage and reduce repeated claims within a
reading. Do not change questionnaire statements, scoring or reference groups.

Writers inherits those improvements through the shared interpreters. Do not add
writing advice, prompts, scene suggestions or invented relationship categories.
