# Proposal: a personality inventory beside the chart

Written 6 October 2026 against Lore 2.5.0. **Nothing here is decided or built.** It is
a proposal to be read by someone outside the project and picked apart. Section 9 lists
the questions I would most like answered.

## 1. The idea in one paragraph

Lore reads a natal chart as a description of a person. But a chart is the same for
everyone born at that minute, and it has no way to know how a given person is
actually living it: the same Mars can be courage in one person and a short temper in
another. The proposal is to let the reader sit a standard, public-domain personality
questionnaire inside Lore, and then show a new view that sets the two side by side:
what the chart describes, and what the person reports. The chart is never altered.
Only the wording of this one view depends on the answers.

## 2. What Lore is, for a reader who has not seen it

A Windows desktop app (C#, WinUI 3) that calculates natal charts with the Swiss
Ephemeris and writes readings from them. Seven views: Chart, Report, Worksheet,
Daily, Forecast, Timing, Synastry. Three things about it shape this proposal:

- **No text is generated at run time.** Every sentence comes from hand-written
  corpora (JSON files of keyed lines) that the code selects and joins. No language
  model is involved. A new view needs a new corpus.
- **It already scores each planet's condition.** `DignityService` gives the seven
  traditional planets a score from the classical rules (rulership, detriment, fall
  and so on), for charts with a known birth time. This is the tradition's own
  measure of whether a planet works easily or with strain.
- **Everything is local.** Saved charts live in one file on the user's PC. Nothing is
  sent anywhere.

## 3. The questionnaire

**IPIP-NEO-120** (Johnson, 2014). 120 statements, each answered on a five-point scale
from "very inaccurate" to "very accurate". About 15 to 25 minutes.

- It measures the five broad traits of the Five-Factor Model and **30 narrower
  facets**, six per trait, four statements each. The facets are what make it usable
  here; five numbers would be too coarse.
- It is in the **public domain**. The IPIP site states that permission is already
  granted to anyone for any purpose, commercial or not, with no fee and no need to
  ask. That fits Lore's AGPL licence without complication.
- It is well studied: built from over 21,000 responses, with facet reliability
  averaging about .80.

Considered and set aside for now:

| Instrument | Why not first |
|---|---|
| Characteristics of Self-Actualization Scale (Kaufman, 2018), 30 items | Closest to the idea, but offered on the author's site "for educational and entertainment purposes" with no stated licence for bundling. Would need the author's permission. |
| PID-5 (the DSM-5 maladaptive trait inventory) | Copyright American Psychiatric Association; as I understand it, free to researchers and clinicians, other uses need permission. Also openly clinical, which is the wrong tone. |
| 300-item IPIP-NEO | Same facets, more reliable, but an hour long. |
| 20- to 50-item short forms | No facets, so nothing to connect to individual planets. |

## 4. From answers to scores

1. Each facet is the sum of its four answers (some reversed), 4 to 20.
2. Each facet is placed against a reference population and put in one of three bands:
   **low**, **typical**, **high** (roughly the bottom sixth, the middle two thirds
   and the top sixth), with a further mark for the very extreme.
3. The reader sees the plain facet profile first, with no astrology attached. This
   is honest value on its own and is the part with real evidence behind it.

The reference figures are an open point: see section 8.

## 5. Connecting facets to planets

This is the invented part. There is no research linking any questionnaire score to
any chart factor; the table below is a design choice made by matching what each
planet traditionally signifies to the facet that measures the nearest thing.

| Planet | Facets | Fit |
|---|---|---|
| Sun (confidence, purpose, vitality) | Self-Efficacy, Achievement-Striving, Cheerfulness, Modesty (reversed) | fair |
| Moon (feeling, security, need) | Anxiety, Depression, Vulnerability, Emotionality | good |
| Mercury (thought, speech) | Intellect | thin |
| Venus (relating, taste, harmony) | Friendliness, Gregariousness, Trust, Cooperation, Artistic Interests | good |
| Mars (drive, anger, assertion) | Assertiveness, Anger, Activity Level, Excitement-Seeking | good |
| Jupiter (expansion, generosity, excess) | Adventurousness, Liberalism, Altruism, Immoderation | fair |
| Saturn (discipline, duty, caution, fear) | Self-Discipline, Dutifulness, Orderliness, Cautiousness, Self-Consciousness | good |
| Neptune (imagination, compassion) | Imagination, Sympathy | thin |

Uranus, Pluto, Chiron, the nodes and Lilith have no reasonable facet and are left
out. One facet (Morality) is unused. The view would say plainly which planets it
does and does not speak about.

## 6. What the view would say

Two kinds of reading, in this order.

### 6.1 How each planet is being lived

For each planet in the table, the pattern of its facets picks one of a small set of
named expressions, each with a written paragraph. The interesting cases are
combinations, not single scores. For Mars:

| Assertiveness | Anger | Expression | Gist |
|---|---|---|---|
| typical or high | typical | direct | drive goes where it is pointed |
| low | high | held in | anger felt and not spent; resentment, sudden outbursts |
| high | high | combative | quick to push and quick to flare |
| low | low | muted | little push and little heat; others decide |

Three to four expressions for each of eight planets is about **30 paragraphs**.

### 6.2 Where the chart and the answers part company

Here the chart supplies an expectation and the questionnaire supplies the report.
The expectation is the planet's condition, which Lore already computes: a dignified
planet is expected to work easily, a debilitated one with strain.

| Chart says | Answers say | Reading |
|---|---|---|
| works easily | lived well | as written |
| works easily | lived with strain | **unlived**: the trouble is not in the chart's description; look to circumstance or habit |
| strained | lived with strain | as written; the reading names the classical difficulty |
| strained | lived well | **hard-won**: a difficult placement that has been worked on |

This is the nearest thing to the original question (how far a person has grown into
their chart), and it gets there without a single overall "level" score. Two
off-diagonal paragraphs for each of the seven traditional planets is about
**14 paragraphs**, plus a short closing summary.

**A worked example.** Mars in Aries (its own sign, so "works easily"); the person
scores low on Assertiveness and high on Anger. The view would say, in substance:
*Your chart describes a Mars that acts quickly and openly. Your answers describe
anger that is felt strongly and seldom shown. The directness is yours to use and is
not being used.* The Report tab continues to say what it always said about Mars in
Aries.

### 6.3 Possible later addition: a measure of overall functioning

The Five-Factor Model measures style, not health: a blunt, solitary, disorganised
person can be perfectly well. To say more about maturity directly, the IPIP also
holds public-domain stand-ins for the "character" scales of Cloninger's Temperament
and Character Inventory (self-directedness is the one most tied to overall
functioning) and for the VIA character strengths. Roughly 20 to 40 further items.
Left out of the first version; the exact scales have not been chosen or checked.

## 7. How it would sit in Lore

- **An eighth view**, working name *Mirror*. No other view reads from it or changes.
- **Only for charts the user has saved** (My Charts). The 212 bundled public
  figures cannot sit a questionnaire and the view says so.
- **Answers are stored with the saved chart, on the PC only.** Left out of the My
  Charts export unless the user ticks a box. One button deletes them.
- The questionnaire can be left and resumed, and retaken later; each sitting is
  dated, since the mood of the day moves some facets.
- **Charts with no birth time** get 6.1 only: Lore does not compute planetary
  condition without a time, so 6.2 has no expectation to compare against.
- Scoring is plain arithmetic with no UI dependency, so it goes in the test project
  like the rest of the calculation layer.

**Size:** large, a release of its own, and mostly writing. In stages, each usable
alone:

1. Questionnaire, scoring and the plain facet profile. Medium.
2. The per-planet reading (6.1). Medium; about 30 paragraphs.
3. The comparison (6.2). Small to medium; about 14 paragraphs.
4. PDF and text export; the optional scales of 6.3. Small to medium.

## 8. Weaknesses, stated plainly

1. **The link between the two halves is made up.** The questionnaire is validated;
   astrology is not, and large studies have found no relation between birth charts
   and measured personality. So "as written" in 6.2 is not confirmation of
   anything, and matches and gaps should be expected at about the rate chance gives.
   The view has to be worded as a prompt for reflection (the phrase Synastry already
   uses), never as a finding.
2. **Self-report is weakest where it matters most.** People with little insight
   into themselves describe themselves inaccurately, and most people flatter
   themselves a little.
3. **Style is not health** (see 6.3). "Lived with strain" in 6.2 is an inference
   from extreme facet scores, not a measurement of difficulty.
4. **The mapping is uneven.** Good for Moon, Venus, Mars and Saturn; thin for
   Mercury and Neptune; absent for five bodies.
5. **Tone.** A view that tells someone their Moon is "lived with strain" on the
   strength of high Anxiety and Depression scores is close to commenting on their
   mental health. No clinical words, no diagnosis, and probably a standing line
   that this is not an assessment of wellbeing.
6. **Reference figures.** The published norms come from internet volunteers,
   mostly young, and differ by age and sex. Lore knows the age from the birth date
   but would have to ask the sex or use combined figures. Johnson's raw data is
   public, but its terms of reuse and the exact figures to bundle have not been
   checked.
7. **Flattering generalities.** Any paragraph vague enough will feel accurate to
   anyone. The paragraphs must say different things for different scores, or the
   questionnaire is decoration.

## 9. Questions for a second opinion

1. Is the central move sound: keep the chart fixed and let the answers choose the
   wording, instead of producing a "modified chart"?
2. Is using the classical dignity score as the chart's "expectation" in 6.2
   defensible to an astrologer, or should the expectation come from somewhere else
   (sign and house emphasis, hard aspects, or all three)?
3. Is the planet-to-facet table in section 5 reasonable? Which rows would you
   change, and is there any honest way to cover Mercury better?
4. Is three bands per facet too coarse, or about right for four-item facets with
   reliability near .80?
5. Does weakness 1 sink the idea, or is "a validated questionnaire, presented in
   the chart's vocabulary" worth having on its own terms?
6. Is 6.3 needed for the idea to mean anything, so that it should be in the first
   version after all?
7. Is there a public-domain instrument better suited than the IPIP-NEO-120 that
   this proposal has missed?
8. What would a careful psychologist object to in the wording of 6.1 and 6.2?

## Sources

- IPIP permission statement: <https://ipip.ori.org/newPermission.htm>
- IPIP index of scales (TCI, VIA and other stand-ins): <https://ipip.ori.org/newIndexofScaleLabels.htm>
- Johnson (2014), the IPIP-NEO-120 paper: <https://bpb-us-e1.wpmucdn.com/sites.psu.edu/dist/1/163537/files/2023/07/IPIPNEO120.pdf>
- Structure and reliability of the IPIP-NEO-120: <https://pmc.ncbi.nlm.nih.gov/articles/PMC7871748>
- Kaufman (2018), self-actualization scale: <https://scottbarrykaufman.com/selfactualizationtests>
