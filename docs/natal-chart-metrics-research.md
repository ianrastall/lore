# Natal-chart measurements and metrics: research and Lore gap analysis

Researched October 4, 2026 against the current checkout. This is a research and implementation proposal; chart calculations and application behavior have not been changed.

**Recommendation:** extend the Worksheet into a complete numerical reference, add a compact chart summary, and export the same underlying results. Start with natal lunar phase, declination contacts, out-of-bounds status, additional axes/points, distribution statistics, and rulership. Most of those reuse data Lore already calculates. Preserve the distinction between astronomical measurements, descriptive counts, and scores defined by an astrological rubric.

There is no finite universal list of natal-chart statistics: Western modern, traditional, sidereal, and Indian astrology use different techniques. The inventory below covers the main useful families and identifies specialist extensions separately. Priority and effort estimates are my engineering assessment, not rankings established by the sources. “Low” means mostly existing-data arithmetic; “medium” involves a new model, native wrapper, or event search; “high” involves a separate technique or broad integration.

## 1. What Lore already includes

The audit used the implementation rather than relying solely on README claims.

| Family | Current coverage | Main implementation |
|---|---|---|
| Birth and calculation provenance | Birthplace coordinates, recorded time, historical timezone conversion, source, Rodden rating, Gregorian/Julian birth-date handling, ephemeris fallback note | `BirthTimeResolver`, `WorksheetService`, `Celebrity` |
| Body positions | 13 bodies/points: Sun–Pluto, North Node, Chiron, mean Black Moon Lilith; ecliptic longitude, sign/degree, latitude, declination, longitude speed and retrograde flag | `PlanetPosition`, `ChartService` |
| Houses and angles | 12 cusps, Ascendant, Midheaven, longitude-based house membership; Placidus, Whole Sign, Equal, Koch; labelled Porphyry fallback at unsuitable latitudes | `NatalChart`, `ChartSettings`, `ChartService` |
| Longitude aspects | Five major aspects plus optional semi-sextile, semi-square, sesquiquadrate and quincunx; actual/allowed orb, applying/separating, out-of-sign flag; contacts to ASC and MC | `Aspect`, `OrbSettings`, `ChartService` |
| Configurations | Sign stelliums, grand trines, T-squares, grand crosses, and Yods when minor aspects are enabled; ASC/MC can participate in applicable patterns | `AspectPatternService` |
| Sect and lots | Day/night chart and sect-reversed Part of Fortune | `NatalChart.IsDayChart`, `WorksheetService.PartOfFortune` |
| Balance | Element and modality tallies in the written report | `ChartInterpreter.Balance`, `ModalBalance` |
| Traditional condition | Seven classical planets: essential and accidental points, component totals, notes and chart aggregate | `DignityService`, `DignityScore` |
| Time sensitivity | Recalculates a timed chart by the minute within a chosen margin, following rising/MC signs, sect, body signs and houses; unknown-time charts get a whole-day sign scan | `TimeSensitivityService` |
| Related timing features | Daily transits with daily lunar phase and station detection; forecasts, solar returns and secondary progressions | `TransitService`, `TimingService` |

Two particularly useful gaps are **data already returned but discarded** and **results present in one view but absent from exports**:

- The natal equatorial pass stores only declination. Right ascension and equatorial speeds are discarded. The ecliptic pass discards distance, latitude speed and distance speed.
- The house call returns additional values in `ascmc`, but Lore keeps only ASC and MC. `SE_VERTEX` is already declared in the wrapper.
- The Part of Fortune appears in the worksheet/PDF but is absent from `ChartExport` and the aspect grid. Dignity breakdowns likewise are absent from JSON/XML.
- Element/modality counts are produced inside the interpreter rather than exposed as a reusable numeric result. An even distribution can return its prose early without an explicit tally.
- Lunar phase and stations are daily-sky features, not measurements attached to the natal chart.

Code references: [planet model](../Models/PlanetPosition.cs), [chart calculation](../Services/ChartService.cs), [worksheet](../Services/WorksheetService.cs), [structured exports](../Services/ExportService.cs), [balance calculations](../Services/ChartInterpreter.cs).

## 2. Practical inventory of additions

### Astronomical quantities and chart geometry

| Addition | Lore status | Proposed calculation and incorporation | Effort / priority |
|---|---|---|---|
| Right ascension, distance, and remaining velocity components | Returned by existing native calls, discarded | Store `eq[0]`, `eq[3]`, `eq[4]`, `xx[2]`, `xx[4]`, `xx[5]` in nullable numeric fields with units. Put them in an expandable position-detail table and JSON/XML. | Low / early |
| Descendant and IC | Not first-class values | `DSC = normalize(ASC + 180°)`; `IC = normalize(MC + 180°)`. Show the four angles explicitly. ASC/DSC and MC/IC share axes, so avoid counting mirrored contacts twice in summaries. | Low / early |
| South Node | Not represented | Opposite the selected North Node by 180°. Its axis is derived, not an additional independent observation. Display and export it; choose explicitly whether both nodes participate in aspect summaries. | Low / early |
| Vertex and anti-Vertex | Native result unused | Keep `ascmc[3]` and its opposite. Add optional wheel markers, worksheet rows and contacts. Suppress without a birth time. | Low–medium / early |
| Natal lunar phase angle | Missing from natal data | `E = normalize(MoonLongitude − SunLongitude)`. Store continuous 0–360° angle, waxing/waning, and an explicitly named phase classification convention. | Low / early |
| Lunar illumination, phase angle and age | Missing | Obtain physical illumination; show percent lit separately from longitudinal phase. Find preceding exact New Moon for actual age in days/hours. Extend planet-detail data if other bodies' apparent diameter/magnitude are wanted. | Medium / next |
| Altitude, azimuth and horizon status | Missing | Add a horizontal-coordinate wrapper; retain true/apparent altitude separately and document azimuth origin. Show degrees above/below horizon and above/below-horizon body counts. Refraction and topocentric choices must be explicit. | Medium / next |
| Angular distance to each angle | Only implicit through aspects | Calculate shortest ecliptic distance to ASC, DSC, MC, IC; optionally show “nearest angle, 2°18′ away.” Keep this distinct from actual altitude or mundane angularity. | Low / early |
| Distance to house cusps | Missing | Store distance after current cusp and before next cusp along the zodiac, plus nearest-cusp distance. Useful for sensitivity and boundary flags. | Low / early |
| Fractional/mundane house position | Longitude bins only | Add a distinct calculation using longitude **and latitude**, ARMC, obliquity and the supported house method. Expose alongside the existing zodiacal house assignment rather than changing its meaning. | Medium / advanced |
| Local sidereal time, ARMC, obliquity, Julian dates and time scales | Not retained as chart metadata | Persist numeric calculation metadata with units, actual engine version and returned flags. Distinguish the input clock/UTC instant, UT1 argument and TT used by the ephemeris. | Medium / next |

The official interface documents the returned coordinate arrays and supports phenomena, horizontal coordinates, additional house points, sidereal modes and fixed stars. The proposal above maps these capabilities onto Lore's existing wrapper; availability also appears in the bundled C header. [Swiss Ephemeris programming interface](https://www.astro.com/swisseph/swephprg.htm), [bundled declarations](../Data/swisseph-2.10.3bfinal/swisseph-2.10.3bfinal/swephexp.h).

The astronomical phase cycle has eight familiar phases, but boundaries used for categorical labels should be stated. A longitudinal elongation is not itself a percentage of the disk illuminated. [NASA: Moon phases](https://science.nasa.gov/moon/moon-phases/).

### Contacts, motion and solar relationship

| Addition | Lore status | Proposed calculation and incorporation | Effort / priority |
|---|---|---|---|
| Declination parallels / contra-parallels | Declinations present; detection absent | With signed declinations `dA`, `dB`, parallel orb is `abs(dA−dB)`; contra-parallel orb is `abs(dA+dB)`. Define same/opposite-side and near-equator policies. Add a separate contact list with its own configurable orb. | Low / early |
| Out-of-bounds status and excess | Missing | `excess = abs(declination) − obliquityAtBirth`; out-of-bounds when positive. Show hemisphere and excess arc-minutes. Do not compare to a permanently fixed 23°26′. | Low / early |
| Declination contacts applying/separating | Missing | Retain declination velocity, or recompute at adjacent instants; compare whether the appropriate signed-declination orb is shrinking. Do not reuse longitude speed for this. | Low–medium / next |
| Natal station proximity | Speed and retrograde present; station dates absent | Bracket/refine zero crossings of longitude speed before/after birth. Show station type, UTC time and signed days from birth. Search a bounded window; report unavailable/not found rather than fabricate a result. | Medium / next |
| Swift/slow motion | Explicitly deferred in dignity service | Compare to a documented per-body mean-speed convention. Show the raw speed and reference; stations and retrograde require separate treatment. Avoid one universal speed threshold for all planets. | Low–medium / next |
| Solar elongation and condition | Cazimi/combust/beams used only inside scoring | Expose separation from Sun, thresholds and condition as typed fields. Add east/west elongation and explicitly defined morning/evening or oriental/occidental labels. Observed visibility requires more than longitude separation. | Low–medium / early |
| Exactness and aggregate aspect statistics | Orbs present; statistics absent | Store actual separation and signed residual; count by aspect, applying/separating, out-of-sign and included point set. List closest contacts and per-body contact count. | Low / early |
| Unaspected planets | Not flagged | Define “no major aspects to the selected planets under these orbs.” Include that definition with the flag; changing minor-aspect or angle settings changes the answer. | Low / early |
| Additional harmonic aspects | Four minors only | Optional quintile 72°, biquintile 144°, septile family and novile family. Use independent narrow orbs. Extend enum, glyphs, settings, renderer colours, labels, interpreter and tests together. | Medium / later |
| Midpoints and midpoint contacts | Missing | Compute circular midpoint axes, then contacts under a separate rule. Begin with Sun/Moon midpoint; allow expanded lists on demand. Handle 359°/1° correctly and mark exactly antipodal pairs ambiguous. | Low–medium / later |
| Antiscia / contra-antiscia | Missing | Tropical antiscion `normalize(180°−longitude)`; contra-antiscion is opposite it. Store mirror points and optional conjunction contacts with an independent orb. | Low / later |
| Additional configurations | Five types present | Add kite and mystic rectangle to existing aspect-graph detection; distinguish sign-based stelliums from conjunction chains and house stelliums. Export constituent contacts and orb criteria. | Medium / later |

Parallels compare positions relative to the celestial equator, independently of zodiacal longitude; a 1° orb is a commonly used option, not a universal standard. [Cafe Astrology: declination contacts](https://cafeastrology.com/declinations_parallels.html). Out-of-bounds compares declination with the ecliptic's obliquity, which changes with epoch. [Astrodienst: out-of-bounds planets](https://www.astro.com/astrowiki/en/Oob).

Quintiles and biquintiles are explicitly listed at 72° and 144° by Astrodienst. [Astrodienst chart/table legend](https://www.astro.com/faq/fq_fh_legend_f.htm). Midpoints are a separate analytical technique; a circular midpoint has an opposite counterpart. [Cafe Astrology: midpoints](https://cafeastrology.com/astrologytopics/midpoints.html). Antiscia reflect the solstice axis. [Antiscia: origin and history](https://www.astro.com/astrology/aa_article190903_e.htm). Kite and mystic-rectangle patterns extend the existing configuration inventory. [Nicholas Campion: aspects and chart shapes](https://www.skyscript.co.uk/aspects2.html).

### Numerical summaries and traditional analysis

| Addition | Lore status | Proposed calculation and incorporation | Effort / priority |
|---|---|---|---|
| Element and modality percentages | Counts exist in prose only | Expose counts, denominator, percentages and ties. Default to a documented body set; optionally retain the current all-13 tally as a preset. | Low / early |
| Polarity balance | Missing | Fire/Air versus Earth/Water counts and percentages; use clear traditional labels and document inclusion rules. | Low / early |
| House, quadrant and hemisphere distribution | Missing | Counts per house and angular/succedent/cadent group; house-based quadrants and explicitly defined hemispheres. Geometric above-horizon counts should use altitude, not arbitrary house numbers. | Low–medium / early |
| Zodiacal concentration / occupied arc | Missing | Sort selected longitudes including wraparound; largest gap and `360° − largestGap` give smallest containing arc. Show occupied signs/houses and optional chart-shape classification. | Low–medium / next |
| Dominant planet/sign or contact-network score | Missing as a defined summary | Start with transparent counts/ranks. If weighted, publish each component and tie rule. Label it “prominence under this method”; degree of connectivity or orb closeness is not a probability. | Low–medium / later |
| Chart ruler and house rulers | Rulership table private to dignity scoring | Expose traditional/modern rulership choices; ASC sign ruler requires birth time. List each cusp sign's ruler and that ruler's position, house and condition. | Low / early |
| Dispositor chains and final dispositors | Missing | Directed graph from each body to the ruler of its sign. Detect self-loops, mutual pairs and longer cycles. Report multiple endpoints/cycles when present instead of forcing one final ruler. | Low–medium / early |
| Mutual reception | Not recognized in scoring | Separate domicile exchange, exaltation exchange and mixed reception; include whether an aspect is required by the selected tradition. Display graph links before adding any scoring bonuses. | Low–medium / next |
| Bound, decan and triplicity rulers | Tables used internally only | Expose ruler names, degree boundaries, remaining arc in bound/decan and day/night/participating roles; publish table provenance. | Low / early |
| Per-degree almuten / strongest planet | Missing as a named result | Score all eligible classical rulers at a selected degree and preserve ties. A chart-wide almuten requires its own stated recipe; highest existing planet tally is only “highest under Lore's rubric.” | Medium / later |
| Per-planet sect condition and expanded accidental dignity | Day/night only; partial scoring | Add sect membership/conditions, waxing Moon, swift/slow and other selected traditional factors as structured components. Use a versioned optional rubric. | Medium / next |
| Lot of Spirit and additional lots | Fortune only | Day Spirit `ASC + Sun − Moon`; night Spirit `ASC + Moon − Sun`, normalized. More lots need a formula/source registry and time/sect dependencies. | Low–medium / early |
| Contacts to Fortune/Spirit and their rulers | Missing | Promote lots to derived chart points, add optional contacts and ruler details, and include in exports. Keep them out of physical-body counts. | Medium / next |
| Quantified uncertainty and robustness | Sign/house/sect changes only | Add circular position ranges, aspect/orb ranges, changed pattern/rulership/lot results, and sensitivity to selected house/orb settings. “Present in 91% of sampled times” must state its sampling assumptions. | Medium / next |
| Library distributions and percentiles | Fixed verdict bands only | Export a metric table for the reference library and rank by a documented cohort, settings and version. Show ties and sample size; it is a library percentile, not a general-population percentile. | Medium / later |

The dispositor is a placement's ruler; reception requires additional interpretive choices. [Skyscript: dispositor](https://www.skyscript.co.uk/glossary/dispositor/), [Skyscript: reception](https://www.skyscript.co.uk/glossary/reception/). The Lot of Spirit reverses Fortune's day/night formulas. [Robert Hand: the Lot or Part of Fortune](https://www.astro.com/astrology/in_fortune_e.htm).

For balance statistics, offer named inclusion presets such as ten planets, seven classical planets, and Lore's current thirteen entries. Weighted percentages need `100 × groupWeight / totalWeight`, with weights shown. Nodes/Lilith and planets should not acquire the same weight by accident. Preserve ties; an “even” chart still deserves explicit numbers.

For optional geometric contact closeness, `max(0, 1 − orb/allowedOrb)` is a possible display rule, with separate handling when the allowed orb is zero. It describes closeness relative to a chosen allowance, not measured astrological influence.

### Specialist extensions

These broaden the chart system and should be optional rather than crowding the default worksheet.

| Extension | What to measure | Implementation scope |
|---|---|---|
| Additional bodies | Ceres, Pallas, Juno, Vesta; selected numbered asteroids, Eris or others | Bundled header contains IDs for the four main asteroids. Check data coverage, body catalog, glyphs, missing-data states and exports; others may need additional ephemeris files. |
| Lilith variants | Mean, osculating and other explicitly named apogee definitions | Separate identifiers/settings. Do not conflate a lunar apogee with asteroid Lilith. |
| Fixed stars | Position at birth epoch, magnitude, planetary/angle contacts, optionally rise/culmination | Bundle the required star catalog; compute epoch-dependent positions, not a timeless longitude table. Keep a small selected set and a documented orb policy. |
| More house systems | Regiomontanus, Campanus, Porphyry, Alcabitius and others | Extend settings, native codes and reference fixtures. Use actual fallback system for every dependent metric. |
| Sidereal zodiac / ayanamsa | Named ayanamsa value and sidereal placements/cusps | Add calculation settings and snapshot them with chart metadata; recompute all zodiac-dependent results consistently. Equatorial astronomy does not change just because a zodiac label changes. |
| Harmonic / draconic charts | Harmonic longitude `normalize(n×longitude)`; draconic longitude relative to selected node | Separate derived chart modes and rules. Ordinary natal house/report text should not automatically be reused. [Astrodienst: harmonic chart](https://www.astro.com/astrowiki/en/Harmonic_Chart). |
| Indian astrology | Nakshatra/pada, tithi, yoga, karana; divisional charts; Shadbala, Ashtakavarga, yogas | Requires a stated sidereal/ayanamsa convention and dedicated formulas/reference fixtures. Nakshatra width is 13°20′ and pada width 3°20′. Shadbala is a separate multi-component system, not a replacement label for Lore's dignity score. [AstroWatch: nakshatras/padas](https://astrowatch.live/docs/nakshatras-padas), [Moira: technique/API inventory](https://moira-astro.com/docs/api-reference). |
| Prenatal lunation / eclipses | Previous New/Full Moon, nearest/preceding eclipse; exact time, position and natal contacts | Event search and eclipse calculations; define whether global occurrence or local visibility matters. [Prenatal eclipses and lunations](https://www.astro.com/astrology/ivccn_article190821_e.htm). |
| Planetary day/hour | Ruler, unequal-hour index, start/end and duration | Sunrise/sunset event calculations plus a declared planetary sequence; handle polar no-rise/no-set explicitly. [Moira: planetary-hours capability](https://moira-astro.com/docs/api-reference). |
| Gauquelin sectors / research geometry | 36-sector position and selected rise/set conventions | Separate research output with algorithm/method recorded. Orbital elements, apsides, heliocentric positions and parans are also possible advanced astronomy/technique panels, but are lower priority for Lore's current scope. |

## 3. Incorporate the numbers through one shared result

Avoid calculating a metric independently in the report, worksheet and export. A proposed `NatalMetrics` result would hold typed, unformatted values:

```text
CalculationMetadata: settings snapshot, actual engine/data, flags, time scales
BodyDetails: coordinates, units, solar condition, station proximity, OOB
DerivedPoints: DSC, IC, South Node, Vertex, Fortune, Spirit
Contacts: longitude / declination / midpoint / mirror, coordinate basis, orb rules
Distribution: included points, counts, weights, denominators, percentages
Rulership: chart/house rulers, dispositors, cycles, receptions
Dignities: named rubric, version, individual components and totals
Sensitivity: input window, sampling method, ranges and changing classifications
```

This is a proposed schema, not an existing class. Extend `PlanetPosition` for retained astronomical fields, and create separate result types for derived values. Suggested pure services are `NatalMetricsService`, `DeclinationAspectService`, `RulershipService` and `LotService`; native operations remain behind `ChartService` and its shared lock.

Only expose physical distance, illumination, magnitude and diameter where they are meaningful for a physical body; nodes, lots and apogee points need explicit not-applicable states. Store right ascension in degrees and optionally display hours, with the conversion labelled. For OOB calculations, record whether true or mean obliquity was used and apply that convention consistently.

**Point representation needs care.** `NatalPointKind` currently has Body, Ascendant and Midheaven, and `IsAngle` means every non-body. Adding lots or midpoints without changing that assumption would treat them as angles. Introduce explicit point categories/identifiers and typed coordinate/motion availability; preserve existing enum values and interpretation keys. Do not make Fortune a moving planet merely to fit the current model.

**Calculation settings must travel with the result.** Keep selected body set, zodiac/ayanamsa, centre, house method, orb policy, rulership convention and dignity rubric in immutable snapshots. New native calls and global settings must stay within `SweLock`; set/reset native sidereal/topocentric state so calculations cannot inherit a previous chart's settings.

Suggested presentation:

- **Worksheet:** compact “Chart statistics,” “Moon and solar relationship,” “Declination contacts,” and “Rulers and lots” blocks; expanded astronomical detail on request. Always show units, denominators and relevant method labels.
- **Report:** a few deterministic interpretation paragraphs for the most useful results. Add vetted corpus entries only after the numeric result is stable; avoid overwhelming the reader with every calculated contact.
- **Wheel:** optional point/contact layers and hover/click details. Adding aspect enum values also requires extending `AspectColors`, which is indexed by enum ordinal.
- **Exports:** extend `ChartExport` once so JSON/XML match; add schema/metric/rubric versions and full numeric precision independent of formatted strings. Extend `Worksheet`, text generation and `WorksheetPdf` together.
- **Library:** later filters such as out-of-bounds Moon, angular planets or mutual reception; compute expensive event searches lazily and cache by chart/settings snapshot.

## 4. Correctness issues to resolve before adding more scores

1. **Third-house scoring differs from the published Lilly table.** `DignityService.HouseScore` assigns house 3 zero. The published table assigns +1. Lore also omits several table factors: mutual reception, swift/slow, oriental/occidental, waxing/waning Moon, freedom from the Sun's beams, and selected partile contacts. That makes the present implementation a subset/variant; a source-matched rubric should be separately versioned. [Current scoring](../Services/DignityService.cs), [Deborah Houlding: Lilly's scoring table](https://www.skyscript.co.uk/dig5.html).

2. **Verdict thresholds are local calibration.** +27 and −1 are tuned against a bundled chart corpus according to the code comments. Expanding factors changes score distributions and can change badges, list colouring and the wheel rim. Preserve the old rubric or deliberately recalibrate and explain the change. These labels are not universal measurements of a person's prospects.

3. **Missing coordinates currently look like a real zero.** The equatorial call falls back to declination `0` on failure. `CalculateBody` also constructs a position without declination. Parallel detection on those objects could invent equatorial contacts. Use nullable values/availability status; missing does not mean zero. Apply the same rule to stations, rulers, lots and missing bodies.

4. **Untimed and uncertain charts need metric-specific treatment.** Suppress angles, Vertex, lots and houses without a birth time. Body aspects, lunar phase and near-threshold OOB status may also vary across the day; show ranges or mark them provisional. The current sensitivity summary only tracks selected signs, houses and sect, so it does not yet establish robustness of all aspects, patterns or future metrics. Restrict its wording or extend its coverage.

5. **Use actual chart time and method.** A helper that also serves returns/progressions must use `CalculatedForUtc` for epoch-dependent values rather than assume every chart is at the recorded birth instant. Use actual Porphyry fallback geometry for dependent results. Day/night should be nullable when unknown, and near-horizon conventions should be declared.

6. **Modern counts and traditional scores need explicit rules.** A sign stellium currently counts every entry in `Planets`, including nodes/Lilith; that is one convention. Peregrine and triplicity treatments also differ among traditions. Record the chosen rules and don't silently broaden a traditional classical-planet calculation to every new asteroid or point.

## 5. Recommended implementation order and verification

| Stage | Deliverable | Why this order |
|---|---|---|
| 1. Shared numbers | Typed metrics, availability/units/settings, retain discarded fields; export Fortune, sect and dignity components; explicit distribution counts | Establish consistent data before more UI or interpretation. |
| 2. Most useful natal additions | Phase angle, parallels/contra-parallels, OOB/excess, DSC/IC/South Node/Vertex, angular/cusp distances, chart/house rulers, Spirit | Mostly existing data; substantial worksheet value with modest calculation cost. |
| 3. Deeper conditions | Stations, physical illumination and lunation age, dispositors/receptions, bound/decan rulers, quantified sensitivity; audit/version expanded dignity rubric | Adds searches and tradition-dependent choices after the basic data pipeline works. |
| 4. Optional analytical modes | Midpoints, antiscia, extra patterns/aspects, fixed stars/asteroids, additional house systems and sidereal/specialist techniques | Keeps default output usable and isolates broader scope. |

Verification should include circular wraparound, exact/near-boundary cases, opposite declinations and near-equator ambiguity, missing ephemeris data, untimed charts, high-latitude house fallback, station reversal, and rulership cycles/ties. Check displayed/JSON/XML/PDF results agree on the same numeric values. Extend the existing `swetest` fixtures for retained coordinates and native geometry; it checks wrapper integration with the same engine, not independent physical validation. Use separate astronomical/reference cases where useful. Named historical rubric examples should cover both the score and its component trail.

Because this task is research only, no application build or tests were run. The existing source and test fixtures were inspected, and the only authored artifact is this report.

Source-access note: some Astrodienst/Skyscript pages blocked full-page retrieval. Where that happened, only indexed definitions/snippets were used; no unseen article content was inferred. The implementation proposals and effort estimates are my analysis of Lore's code.
