# Validation: Lore 5.2.0

6 November 2026. The Sky view (planetary mood) and Writers' Open in Synastry.

## Automated verification

- Full core suite: 663 passed, 0 failed (652 before, 11 new in `SkyTests` and the
  updated Legend counts).
- Known sky facts: the Jupiter–Saturn conjunction of 21 December 2020 is found exact
  between 17:30 and 19:00 UT and read as a longer-running theme; the solar eclipse of
  8 April 2024, Mercury turning retrograde on 1 April 2024 and the Sun entering Aries
  at about 03:06 UT on 20 March 2024 are among the day's sky events.
- Every aspect found on three test days is within its orb when recalculated
  independently at its peak, and every aspect called exact is within 0.001°.
- `sky.json` has exactly the 127 pair lines that can occur, all distinct and non-empty,
  and every aspect found on each day of January and February 2026 has its line.
- Readings are deterministic; the tone in the reading matches the week strip's;
  the Moon's contacts are listed in time order; a day with nothing beyond the Moon
  reads Quiet; a home place adds the planetary hours; text and PDF exports carry the
  reading; dates outside 1200–2399 are refused.
- View model: nothing is calculated while the view is closed; the week runs Monday
  to Sunday and is kept while moving within it; out-of-range dates show an error.
- Self-check: `sky.json` must be whole and today's sky must be read. A staged copy
  without `sky.json` failed it, as it should, before the file was added to
  `required-files.txt`.
- Release build: the published copy passed its self-check and the personal-data gate
  (11,163 figures, 0 personal charts). Debug and release app builds pass the Jupiter
  style check.

## Orb and tone

At the Daily view's 1° orb the reading for 6 November 2026 had one aspect between
planets, and 82 days of 2026 had no foreground at all. Between planets other than the
Moon the orb is 2°: 18 days of 2026 then have no foreground.

Counting every aspect, the decades-long Neptune–Pluto and Uranus–Neptune sextiles made
199 of 2026's days Flowing. Aspects among Uranus, Neptune and Pluto are now read but do
not set the tone: 2026 reads 149 Flowing, 121 Mixed, 91 Demanding and 4 Quiet days
(America/Edmonton days).

## Artifacts

- `dist\Lore-5.2.0-win-x64-setup.exe` (91.2 MB)
- `dist\Lore-5.2.0-win-x64-portable.zip` (136.6 MB)

## Installation

Installed on Ian's machine with his approval on 6 November 2026.

| Check | Result |
| --- | --- |
| Upgrade | 5.1.0 to 5.2.0 at `D:\Lore`, all-users; installer exit 0; no restart |
| Version | `Lore.exe` reports `5.2.0+c65fdee`. A first install from a build made before the commit carried the previous commit's hash; it was rebuilt from the committed tree and installed again. |
| Uninstaller | Registered as Lore 5.2.0, install location `D:\Lore\` |
| Start Menu | `Lore.lnk` targets `D:\Lore\Lore.exe`; launched it with `--self-check`, exit 0, OK (including the Sky check) |
| Saved data | All five existing files retain their SHA-256 checksums; HUD daily caches excluded |
| Release | GitHub release 5.2.0 at `c65fdee`, marked Latest, with the installer and portable zip |

The installed check was headless.

## On-screen check (5.2.1)

A debug build with made-up data (`LORE_HOME`, no personal charts) was opened on the
Sky view at Ian's request, driven through UI Automation and captured with
`PrintWindow`; no input reached other windows. Checked: the heading, date controls,
week strip, tone, reading and wheel for 6 November 2026 (all week Flowing: its only
aspects are Venus sextile Jupiter, Venus sextile Mars and the neutral Sun–Mercury
conjunction); selecting a day in the strip and stepping into the next week; 16
November (Demanding: Mars conjunct Jupiter exact, the Sun's squares to Jupiter and
Mars building); planetary hours with a home place; the Legend's Sky group.

Two faults were found and fixed in 5.2.1: "Demanding" was cut off in the week strip,
and each day in it was announced to screen readers by its tone alone, so all seven
could sound the same. They now read "Monday 16 November: Demanding".

## Installed (5.2.1)

| Check | Result |
| --- | --- |
| Upgrade | 5.2.0 to 5.2.1 at `D:\Lore`, all-users; installer exit 0; no restart |
| Version | `Lore.exe` reports `5.2.1+7b3cbe5` |
| Uninstaller | Registered as Lore 5.2.1 |
| Start Menu | `Lore.lnk` targets `D:\Lore\Lore.exe`; `--self-check` exit 0, OK |
| Saved data | All five existing files retain their SHA-256 checksums |
| Release | GitHub release 5.2.1 at `7b3cbe5`, marked Latest |

Install logs, checksums and self-check output are under the ignored
`artifacts/qa-5.2.0`.
