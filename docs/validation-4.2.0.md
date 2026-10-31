# Validation: Lore 4.2.0

31 October 2026. Windows 11, x64. Expands the self-report, extends Trine and adds 13 figures.

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
| Tests | RESULT_TESTS |
| App build | 0 warnings, 0 errors; style check at Error severity with 0 violations |
| Figure library | 1,327 figures, every date, clock time, rating and UTC instant matches its reference |
| Release build | RESULT_BUILD |
| Personal-data gate | RESULT_GATE |
| `--self-check` on the published build | RESULT_SELFCHECK |

## Exercised

Off screen, through the view models: all three questionnaires answered in turn, saved, read back by a second store, and deleted together. An older answers file holding only personality answers reads as before.

## Not verified

- **The Inventory view's new panels on screen**: the part selector, the character profile and the life-area list. They build and pass the style check, and the view model behind them is tested. Nobody has looked at them.
- **The Synastry view's Look among box on screen**, and the reading with as-lived tones.
- **The installed upgrade.** STATUS_INSTALL
- **A person's real answers.** The reading was tested with made-up answers and simulated populations.
- **Whether the pairing of chart factors with these scales means anything.** No research connects a birth chart with measured personality. The readings say so.
- **Three figures' details**, listed in `scripts/celebrity-expansion-2026-10-31.md`, were read from a search summary and not from the record.
