# Sky: the planetary mood of a day

Current direction: 6 November 2026. Ian asked for a tab that runs the day itself
through Lore, to say what the day is like for the world in general, and
authorized planning and building it ("Plan, then build").

## What it is

A tenth view, **Sky**, that reads a calendar day without any birth chart. The
planets are the same for everyone; only the day's boundaries depend on where the
reader is, so the day runs from local midnight to local midnight in the reader's
own time zone, as the Daily view does.

It uses no chart from the Charts list, so like Writers it takes the whole
workspace. Nothing about a person is read or written.

## The scan

`TransitService.ScanSky(date, zone, home)` samples the day hourly, exactly as the
Daily scan does (the shared sampling is factored out of `Scan`), and adds:

- **Aspects between the moving planets themselves**, Sun to Pluto. Every pair and
  every major aspect is tried, refined to the closest approach and marked building,
  exact or easing with its nearest exact time. The Moon's contacts use the Daily
  view's 1° orb; the others 2°. At 1° the reading for 6 November 2026 had one
  aspect, and 82 days of 2026 had no foreground at all; at 2° there are 18 such
  days, and an aspect still lasts days, not weeks, for the faster planets. The Sun–Moon pair is left out: its aspects are the lunar phases, which are
  already reported.
- **The day's sky events**: New and Full Moons, eclipses, stations and slow-planet
  ingresses from the existing sky calendar, plus the Sun, Mercury, Venus and Mars
  changing sign during the day.
- Everything the Daily view already has for a day: Moon sign and phase with exact
  times, void-of-course stretches, retrogrades, and with a home place the planetary
  hours.

The planet named first is the faster one in the conventional order: the Moon, then
Sun, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune, Pluto.

## The reading

`SkyInterpreter` writes it from `content/sky.json`, reusing `daily.json` for the
Moon, phases, stations, retrogrades, calendar events, planetary days and hours.

1. **The day at a glance**: the day's tone, the planetary day, the Moon's sign and
   phase.
2. **Today's aspects**: up to four aspects involving the Sun, Mercury, Venus or Mars,
   ranked as the Daily view ranks transits, then read in time order.
3. **Also in the sky**: the day's sky events, void-of-course Moon, stations and
   retrogrades.
4. **The Moon's contacts**: the Moon's aspects that are exact during the day, timed.
5. **Longer-running themes**: up to three aspects between Jupiter and the planets
   beyond it, with peak dates.
6. **Where the planets are**: each planet's sign, degree and motion.
7. **Planetary hours**, when a home place is set.

**Tone** uses the Daily view's five steps (Flowing, Mixed, Demanding, Focused,
Quiet) and thresholds. Today's aspects count fully, the background half, and the
Moon's contacts a fifth. Aspects between Uranus, Neptune and Pluto, in orb for
years at a time, are read but do not set the tone: counted, the decades-long
Neptune–Pluto and Uranus–Neptune sextiles made 55% of 2026 Flowing. Without them
2026 reads 149 Flowing, 121 Mixed, 91 Demanding and 4 Quiet days. A conjunction takes its colour from the planets in it:
Venus and Jupiter ease it, Mars, Saturn and Pluto harden it.

**The week**: a strip of the seven days (Monday to Sunday) around the chosen date,
each with its tone, to move between them.

**Why this reading?** lists every aspect in effect and which the reading used.

## Corpus

`content/sky.json`, written for this release in a collective voice (the general
mood, what people in general meet), with no slogans and no predictions of events:

- `pairs`: one line for each pair and tone ("Mars|Tension|Saturn"). Every pair of
  Sun to Pluto with Conjunction, Flow and Tension, except the five that cannot
  occur (the Sun with Mercury or Venus in Flow or Tension, Mercury–Venus in
  Tension), plus the Moon with the eight planets other than the Sun: 127 lines.
- `planetThemes` and `toneLinks`: the fallback sentence for a missing line.
- `dayTones`: the five tones for the world.
- `notes`: explanatory lines.

## Delivery

- View tab **Sky** after Writers; shortcut **Ctrl+Shift+4** (beside Daily's Ctrl+4:
  Ctrl+1 to Ctrl+9 are taken and Ctrl+0 resets the text size). Lore reopens on it
  like any other view.
- Wheel: the day's sky alone at its middle, Aries at the left, with the day's
  aspects drawn as lines, heavier for those the reading uses.
- Export: File > Export > Sky as PDF or text.
- Legend entry, README section, tests (scan facts against known dates, corpus
  coverage, determinism, view model), version 5.2.0, installer, install and
  Start Menu check, validation note.

## Honesty

The view's caption says it is read from the aspects the planets make to each other
and is a prompt for reflection, not a prediction. Astrology has no agreed method
for a world day; the ranking and the tone are Lore's own editorial choices.
