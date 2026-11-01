# Writers workspace

Direction recorded 1 November 2026, after Lore 4.5.1. This is the plan for the next
major iteration, followed by a separate expansion of the written readings. No
Writers implementation or new release version is claimed here.

## Confirmed direction

Ian wants a new **Writers** tab with two uses:

1. Create fictional characters from a notable birth date, time and place. The
   notable anchor can be either a sky event or a distinctive feature of the local
   chart; offer both.
2. Discover pairs and trios of famous people without first choosing a person.
   Their readings describe character and group interaction for writing.

Famous-group eligibility has two hard requirements, clarified by Ian:

- Geographic closeness means **nearby birthplaces**.
- The age gap is **at most ten years**, measured between the oldest and youngest
  member. In a trio, all three members must satisfy the geographic requirement.

After this iteration, expand the range of the assigned written blurbs throughout
Lore to address repetition. The Writers feature needs an initial corpus of its
own, but that does not replace or pull forward the broader editorial work.

## Character creation

Start with a time period and a place, then find meaningful candidate birth
moments. The writer can browse the anchors before choosing a character; they do
not have to invent a precise birth time in order to get started.

Two anchor families belong in the first design:

| Anchor | What makes a candidate notable | Work needed |
| --- | --- | --- |
| Sky event | New or Full Moon, eclipse, a planet turning retrograde or direct, a sign ingress | Separate the existing sky-event calculation from its natal-house overlay and expose it without a selected chart |
| Local chart | A personal planet close to an angle, a strong chart pattern, a distinctive concentration or imbalance | Search actual charts within the chosen bounds and refine qualifying times; define explicit significance thresholds |

A sky event supplies a moment shared across the world. The chosen place supplies
the local angles and houses. The reading must use both. A Full Moon alone must not
produce the same character at every location.

The result starts with a plain explanation of the anchor and its actual computed
details, followed by a character brief:

- Public manner and private need.
- Principal desire, strength and vulnerability.
- A contradiction that creates pressure.
- Behavior under pressure and in close relationships.
- Productive directions for a character arc.

Each interpretation should be traceable to its chart factors. Keep the computed
anchor distinct from the fictional interpretation. The writer owns the
character's choices, biography and eventual story.

Choosing several features at once must search their intersection. If no moment
meets all the constraints, report that and let the writer change them. Do not move
the chosen location or invent an astronomical arrangement to satisfy a trait.

Candidate lists need variety: group nearby moments from one event, show their
local differences, and prevent one frequent pattern or decade from filling every
result. Initially browse by anchor and resulting traits; reverse searching from
an arbitrary free-text personality description is a separate extension.

## Famous pairs and trios

The entry point is a fresh search over the library, independent of the selected
natal chart. Choose **Pair** or **Trio**, then the desired interaction and filters.

The proposed initial geographic limit is **100 km**, adjustable by the writer.
This distance is a design default, not an additional value confirmed by Ian. Use
great-circle distances between birth coordinates, including across the date line.
For a trio, the greatest of the three pairwise distances must be within the limit;
a chain of two nearby pairs is insufficient.

Apply the ten-year age limit as an actual calendar-date boundary, with a stated
leap-day rule, rather than subtracting birth years or averaging pairwise ages.
Normalize historical calendars through Lore's existing birth-date handling first.
All hard filters run before interaction ranking. Missing required dates or
coordinates cannot be treated as zero distance or as satisfying a filter.

Every result gives the birth-year span, maximum birthplace distance and the
interaction that earned its place. Label the geography as birthplace proximity;
do not describe it as evidence that these people met or lived nearby as adults.
Any claim of a documented relationship needs its own source.

Suggested interaction choices for the first design are cooperation, rivalry,
attraction with friction, and an unstable alliance. These are proposed editorial
categories. They need explicit mappings to chart evidence, rather than simply
renaming Lore's compatibility levels.

Pair readings explain what joins the two, where their aims clash, and how those
forces might play out in a story. Trio readings explain all three relationships,
who mediates, who feels excluded, possible coalitions, and what changes under
pressure. A strong grand trine can matter without making it the only interesting
kind of trio. Keep Lore's existing compatibility measures available alongside
these readings.

Default to the public figure library. Personal inventories do not become invented
answers for famous people, and fictional characters do not enter the historical
figure library. Category, era and prominence can narrow or order a search; none
may bypass the age or distance requirements.

## Search and existing code

The current 11,163 figures make 62,300,703 possible pairs and 231,779,382,061
possible trios before filtering. The existing finder chooses the best 100
connections to a selected person, then tries their 4,950 pairs. Repeating that
operation for every figure is not a suitable global search.

Build an eligibility graph from age and birthplace distance. Eligible pairs are
edges; eligible trios are triangles. This enforces the requirements on every
member without enumerating all possible triples. Cache each person's chart once
per calculation-settings snapshot and reuse pair assessments.

Measure filtered graph size and timings against the actual library before
choosing result retention or ranking shortcuts. If a future search uses sampling
or a shortlist, state its coverage; do not present its results as an exhaustive
global best. Support cancellation, progress, and rejection of stale results.

| Existing source | Reuse |
| --- | --- |
| `ChartService`, `BirthTimeResolver`, `CityService` | Actual birth charts, historical time conversion, place selection |
| `TransitService.SkyCalendar` | Sky-event finding, after separating the natal overlay |
| `AspectPatternService`, `NatalMetricsService`, `NatalEventsService` | Chart features and significance evidence |
| `SynastryService`, `SynastryScoring`, `TrioService` | Contacts, current compatibility, three-person figures and relationships |
| `LatestOnly` and existing results view models | Cancellation and stale-result behavior |
| `content/records` | Possible future sourced relationship evidence; currently incomplete and not installed, with no residence history |

`CharacterService` already means the self-report character inventory. Give the
new fictional-character models and services distinct names, such as
`WriterCharacter` and `WritersService`.

## Interface and saved work

Add Writers to the main view selector with **Create character** and **Find groups**
as its two modes, and Pair/Trio within the latter. It must work when no chart is
selected. Keep the current chart selection intact when moving in and out of it.
Existing keyboard shortcuts and export readiness must continue to work.

Use Jupiter's native controls, one main result area and an adjacent detail/reading
area where space permits. At smaller sizes, open a selected result's detail view
instead of squeezing both into unreadable columns. Empty, searching, cancelled,
failed and no-match states need explicit behavior.

Save a writer's named characters and chosen casts separately from My Charts,
including their actual birth details, anchor, settings and corpus version. Store
public casts by stable figure IDs. Allow the writer to reopen the underlying
charts and export the brief. Do not insert fictional identities into the bundled
library or silently overwrite personal charts.

## Following iteration: range of written readings

Treat repetition as an editorial and composition problem. Adding interchangeable
synonyms alone will not give different characters or relationships substance.

1. Inventory the corpus keys and fallback paths across natal, Daily, synastry,
   Davison, Trine, self-report and Writers readings. Render representative batches
   to measure repeated paragraphs, phrases and structures.
2. Expand distinct meanings and contexts: strengths, costs, contradictions,
   constructive and destructive expressions, asymmetric relationships and how a
   configuration behaves under pressure. Keep the chart evidence attached.
3. Add authored alternatives to heavily reused entries, especially generic
   fallbacks. Select coherently across the whole reading and suppress repeated
   claims within it.
4. Keep offline, deterministic composition: the same inputs, settings and corpus
   version produce the same text. If alternate readings are offered, make the
   selected alternative explicit and reproducible.
5. Verify coverage, placeholders, attribution of each person's factors and
   repetition metrics. Review samples editorially; a larger line count is not
   sufficient evidence of better writing.

The initial Writers corpus should accommodate multiple authored treatments so
that the following iteration can expand it without replacing the composition
model. No runtime text-generation service is implied.

## Implementation checkpoints

1. Confirm the Writers layout and the meaning of each proposed interaction type.
2. Benchmark the age-and-place eligibility graph and exact pair/trio search.
3. Deliver character anchor discovery and a traceable fictional-character brief.
4. Deliver global pair/trio discovery, their readings and named saved casts.
5. Verify cancellation, all-member constraints, historical date handling,
   meaningful location sensitivity, missing data and persistence. Check the tab
   without a selected chart and at smaller window sizes.
6. Run the full release checks, build the installer, then install and verify Start
   Menu delivery under the existing desktop-window permission convention.
7. Begin the broader corpus expansion as the following iteration.
