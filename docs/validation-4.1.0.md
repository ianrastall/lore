# Validation: Lore 4.1.0

31 October 2026. Windows 11, x64. Adds Trine, the reading of three charts (tri-synastry), with a finder.

## Calibration

20,000 random trios from the 1,314 library figures, seeded so the run repeats:

| Finding | Result |
| --- | --- |
| Trios with any grand trine through all three charts | 91.1%, 3.65 on average |
| Golden | 3.4% |
| Harmonious | 25.5% |
| Mixed | 33.8% |
| Strained | 37.3% |
| Cost | 0.13 ms a trio |

The first rule, summing all grand trines, made 10.5% of trios Golden and 49.8% Strained. It was replaced: Golden now rests on the single strongest grand trine, at weight 0.5, and Strained needs the three pair levels to total below zero. `TrioServiceTests` holds the proportions against the library.

Golden is more common among people who already suit each other. For Albert Einstein and Mata Hari (Soulmates), 351 of 1,312 thirds are Golden. In the finder for Albert Einstein, 640 of the 4,950 trios from his top 100 connections are Golden, and the search took 347 ms.

## Passed

| Check | Result |
| --- | --- |
| Tests | 507 of 507 passed |
| App build | 0 warnings, 0 errors; style check at Error severity with 0 violations |

The build installed on Ian's machine so far is `baf9822`, which has Trine but not the finder.

| Check on that install | Result |
| --- | --- |
| Upgrade of the installed 4.0.0 | Exit 0, Apps entry **Lore** 4.1.0, Start entry kept |
| User data through the upgrade | All 5 files matched their checksums; the daily reading caches were not compared, because the HUD rewrites them |
| `--self-check` on the installed copy | OK |

## Exercised

In a debug build with made-up data, driven through UI Automation and inspected from screenshots: Albert Einstein and Mata Hari in Synastry, Trine chosen, the best-third list with Golden marks, Bernard Buffet chosen, and the reading with its wheel.

Off screen, the app's drawing code produced the three-ring wheel PNG and the PDF for that trio, and the text export was read. The wheel was inspected: three rings, with a gold triangle for the strongest grand trine.

That check found two wording faults, both fixed afterwards: "the two birth charts" under the title in the Trine view, and a wheel caption that wrapped badly.

## Not verified

- The finder on screen, and the two wording fixes. They are built and tested but not yet seen.
- Installation of the build with the finder.
- Trine exports through the save dialog, as for the other exports. Their code ran off screen.
- Trine with charts of your own, and the search box for a third person.
