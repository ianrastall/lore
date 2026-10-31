# Validation: Lore 4.2.0 and 4.2.1

31 October 2026. Windows 11, x64. 4.2.0 expands the self-report, extends Trine and adds 13 figures. 4.2.1 corrects what seeing it on screen showed. Results below are for 4.2.1 unless they say otherwise.

## Found on screen, and fixed in 4.2.1

A debug build with made-up people, driven through UI Automation and inspected from screenshots. None of these could be caught by the tests.

| Fault | Fix |
| --- | --- |
| The five answers to a statement ran off the edge, so "Very accurate" could not be seen or chosen. Same labels and layout in the personality questionnaire, so it predates 4.2. | They share the width and wrap. Chosen through UI Automation: "1 of 120 answered" |
| "Stop and discard this sitting" was clipped | The bar lays out in columns |
| Look among started blank, and could lose its selection when the list was refreshed | Bound to the scope, and the list is only replaced when the categories change |
| Match and finder scores taking the answers into account did not say so | The summaries say so |
| Chart and answers kept the heading of the questionnaire it was opened from | Headed Chart and answers |
| "Tried 1 trios" | One trio |

## Calibration

| Measure | Result |
| --- | --- |
| Pairs moved by the answers, both people answered (3,989 random pairs, answers drawn to look like people's) | 71.9% same band, 27.8% one band, 0.3% two |
| The same, one person answered | 79.4% same band, 20.6% one band, none two |
| Trios with a strong T-square, yod or meeting point (figure weight 0.5, 3,978 random trios) | About 1 in 4, 1 in 10 and 1 in 20 |
| The first rule for figures, before the bar | 99.97% of trios had one, so it was replaced |
| Every character expression | Met by at least 1 person in 500 of a simulated 2,000 |

`The_answers_move_a_pair_by_a_band_and_not_across_the_scale` holds the first of these.

The first measurement used random answers and showed 63% of pairs moving. Random answers lean towards strain against the reference figures, so that overstated it, and it was measured again with facets drawn to correlate within their traits.

## Passed

| Check | Result |
| --- | --- |
| Tests | 547 of 547 passed |
| App build | 0 warnings, 0 errors; style check at Error severity with 0 violations |
| Figure library | 1,327 figures, every date, clock time, rating and UTC instant matches its reference |
| Release build | `build/build.ps1` built the installer and portable zip with no errors |
| Personal-data gate | Passed: 1,327 figures, 0 personal charts |
| `--self-check` on the published build | OK |

## Installed

On Ian's machine, 4.2.0 over 4.1.0 and then 4.2.1 over that, at his word.

| Check on the 4.2.1 install | Result |
| --- | --- |
| Both upgrades | Exit 0, Apps entry **Lore** 4.2.1 at `D:\Lore`, uninstaller present, Start entry kept |
| User data through both upgrades | All 5 files matched their checksums; the daily reading caches were not compared, because the HUD rewrites them |
| `--self-check` on the installed copy | OK |
| Launch from the Start shortcut | Window in 0.5 s, responding, closed cleanly, and the charts and answers unchanged afterwards. No screenshot: it shows Ian's own charts |

## Exercised

Off screen, through the view models: all three questionnaires answered in turn, saved, read back by a second store, and deleted together. An older answers file holding only personality answers reads as before.

On screen, with made-up people: the personality, character and life-area profiles; the part selector; Chart and answers; the introductions and question pages of all three; the match list; the finder, its scope and a row chosen; the Trine reading and its as-lived tone; the comparison and its as-lived tone.

## Not verified

- **Trine figures, Who holds it together and What the three bring on screen**: the made-up trio had none of them. Their text is tested, and was read in a text dump.
- **A questionnaire answered from start to finish by hand.** One answer was chosen on screen; whole sittings were run through the view model.
- **A person's real answers.** The reading was tested with made-up answers and simulated populations.
- **Whether the pairing of chart factors with these scales means anything.** No research connects a birth chart with measured personality. The readings say so.
- **Three figures' details**, listed in `scripts/celebrity-expansion-2026-10-31.md`, were read from a search summary and not from the record.
