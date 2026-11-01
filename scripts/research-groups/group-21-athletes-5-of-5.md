# Lore research group 21 of 37: Athletes 5 of 5

Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. Below is a table of 44 people. Check each of them against the rules that follow, and return a full record for each one who passes. Do not add anyone who is not in the table.

Most of them will not pass, and that is expected: a short list of certain entries is what I want, not a full one. Never guess, estimate or fill a gap to get someone through.

The table says who each person is, and the birth date is there to tell them from others of the same name. Look at the record you are reading: if its birth date or occupation does not fit, it is a namesake, not this person. If the right person's record gives a different date, use the record's and say so in the verification table.

## The rule that matters most: birth-time reliability

- Use **Astro-Databank** (astro.com/astro-databank) as the primary source and report its **Rodden Rating** for each person.
- Accept ONLY Rodden Rating **AA** (birth certificate or birth record) or **A** (from the person, family, or a quoted memory).
- Reject B, C, DD, X and XX ratings, rectified or speculative times, and anyone whose sources give conflicting times you cannot resolve. If in doubt, leave the person out. A shorter list of certain entries is better than a longer list with guesses.
- Never estimate, round, or invent a birth time, and never use noon as a stand-in.
- Read the source notes as well as the rating. Exclude times inferred from an Ascendant degree or reconstructed approximately from a pictured chart, even if the entry is rated A. When a certificate or a later explicit personal statement supersedes an earlier claim, explain the resolution.
- The person must have been born between the years 1200 and today.

## Format for each person

Return one JSON object per person with exactly these fields:

```json
{
  "id": "marie-curie",
  "name": "Marie Curie",
  "category": "Scientist",
  "birthDate": "1867-11-07",
  "birthTime": "12:00",
  "birthTimeKnown": true,
  "birthTimeUncertaintyMinutes": 15,
  "deathDate": "1934-07-04",
  "birthPlace": "Warsaw, Poland",
  "latitude": 52.2516,
  "longitude": 21.0085,
  "timeZoneId": "Europe/Warsaw",
  "utcOffsetHours": 1.0,
  "roddenRating": "AA",
  "source": "Astro-Databank: birth certificate or record in hand; https://www.astro.com/astro-databank/Curie,_Marie",
  "bio": "Physicist and chemist who pioneered radioactivity and remains the only person to win Nobel Prizes in two sciences."
}
```

Field rules:

- **id** — the name in lowercase, words joined by hyphens, plain unaccented letters and digits only (e.g. `gabriel-garcia-marquez`). Must be unique.
- **name** — the name the person is best known by, with correct accents.
- **category** — exactly one of: Actor, Musician, Writer, Artist, Political, Scientist, Historical, Philosopher, Director, Athlete, Media, Entrepreneur, Royalty, Spiritual. Use "Historical" for figures known mainly for their place in history (explorers, military leaders, reformers) who fit nowhere else.
- **birthDate** — `YYYY-MM-DD` in the **Gregorian calendar**. If the source gives an Old Style (Julian) date — anyone born before 1582, and later in countries that had not yet switched, such as Britain before 1752 or Russia before 1918 — convert it to the Gregorian date and say so in the verification table.
- **birthTime** — the local clock time at the birthplace as recorded, in 24-hour `HH:mm`. Do NOT convert it to UTC or to any other zone.
- **birthTimeKnown** — always `true` (anyone for whom it would be false should not be on the list).
- **birthTimeUncertaintyMinutes** — how far out the time might be, in minutes either way. `15` when the recorded time falls exactly on the hour or half hour, because such times are usually rounded; `0` only when the record gives another exact minute.
- **deathDate** — `YYYY-MM-DD` in the Gregorian calendar, or `null` if the person is living. Lore uses it to tell which people were alive at the same time, so check it as carefully as the birth date.
- **birthPlace** — `City, ST` for the United States using the two-letter state (e.g. `Tupelo, MS`); `City, Country` everywhere else (e.g. `Ulm, Germany`).
- **latitude / longitude** — decimal degrees of the birthplace to four decimal places. North and east are positive; south and west are negative.
- **timeZoneId** — the IANA time-zone name for the birthplace as it is today (e.g. `America/Chicago`, `Europe/Paris`, `Asia/Kolkata`).
- **utcOffsetHours** — that zone's standard (winter, non-daylight-saving) offset from UTC as a number, e.g. `-6.0` for America/Chicago, `1.0` for Europe/Paris, `5.5` for Asia/Kolkata.
- **roddenRating** — the current Astro-Databank rating, exactly `AA` or `A`.
- **source** — begin with `Astro-Databank`, briefly describe the birth-time evidence, and include the source page URL. Do not describe an A-rated personal account as a birth certificate unless the evidence supports that description.
- **bio** — one neutral sentence of 65 to 120 characters saying what the person is known for, ending with a full stop.

## Where to stop

Birth-time evidence is the job. Check it first, and if a person fails the rule above, stop there and give them one line in the rejected list, without filling in the other fields. Do not gather relationships, awards or other biography: those come from Wikidata once a person has passed.

## What to return

1. **A single JSON array** containing all the people, in a code block, valid JSON, with no comments and nothing else inside the block.
2. **A verification table** with one row per person: name, Rodden Rating, the Astro-Databank page URL, the birth date and time exactly as the source states them (including the time standard it names, such as LMT or CET), and a note on anything you converted or resolved (calendar conversions, disputed times, alternative birthplaces).
3. **A short list of people you considered and rejected**, with the reason, so I know not to look them up again.
4. **A reference JSON object keyed by id** for `tests/Lore.Tests/Reference/adb-reference.json`. Each value must contain `adbTitle`, `date`, `time`, `zone` (the source's time standard and offset, such as `PDT h7w`), `rating`, `source` (the source's data-source label), `expectedUtc` (`YYYY-MM-DDTHH:mm:ss`, independently derived from the source's stated offset), `url`, `checkedOn` (`YYYY-MM-DD`), and `note` for any resolution. The local `time` remains unchanged; only `expectedUtc` is converted. Do not derive the expected UTC instant from Lore's own implementation.

5. **Evidence outside Astro-Databank.** Anyone who fails only because Astro-Databank has no qualifying record, but for whom you found a primary source: a birth certificate or birth announcement stating the time, or the person or a parent stating an exact time in their own words. Give the person, the time as stated, and a link to the source. Do not put them in the JSON; they will be reviewed by hand. Do not include horoscope sites, unsourced chart databases, or anything derived from a rising sign.

Before answering, check every entry: the time appears in a cited source with rating AA or A; the JSON date and time match the verification table; the coordinates are in the right hemisphere for the birthplace; the category is spelled exactly as listed; and everyone in the table below appears in exactly one of the JSON, the rejected list or the evidence list.

## The 44 people to check

| Name | Who | Born |
|---|---|---|
| Aaron Judge | American professional baseball player (born 1992) | 1992-04-26 |
| Jordan Chiles | American artistic gymnast | 2001-04-15 |
| Justin Verlander | American baseball pitcher (born 1983) | 1983-02-20 |
| Mike Trout | American professional baseball player | 1991-08-07 |
| Nelly Korda | American golfer | 1998-07-28 |
| Rashid Khan | Afghan cricketer | 1998-09-20 |
| Siya Kolisi | South African rugby union footballer | 1991-06-16 |
| Steve Young | American football player and analyst | 1961-10-11 |
| Brooks Koepka | American professional golfer (born 1990) | 1990-05-03 |
| Cal Ripken Jr. | American retired baseball player (born 1960) | 1960-08-24 |
| Cheryl Miller | American basketball player and coach (born 1964) | 1964-01-03 |
| Clayton Kershaw | American professional baseball player (born 1988) | 1988-03-19 |
| J.J. Watt | American football player (born 1989) | 1989-03-22 |
| Jon Rahm | Spanish professional golfer | 1994-11-10 |
| Josh Allen | American football player (born 1996) | 1996-05-21 |
| Miguel Cabrera | Venezuelan baseball player | 1983-04-18 |
| Wasim Akram | Pakistani cricketer | 1966-06-03 |
| Barry Sanders | American football player (born 1968) | 1968-07-16 |
| Fred Couples | American professional golfer | 1959-10-03 |
| Mookie Betts | American professional baseball player (born 1992) | 1992-10-07 |
| Bryce Harper | American professional baseball player (born 1992) | 1992-10-16 |
| Christian McCaffrey | American football player (born 1996) | 1996-06-07 |
| Greg Maddux | American baseball player (born 1966) | 1966-04-14 |
| Joe Burrow | American football player (born 1996) | 1996-12-10 |
| Juan Soto | Dominican baseball player (born 1998) | 1998-10-25 |
| Pat Cummins | Australian cricketer | 1993-05-08 |
| Randy Moss | American football player and sports analyst (born 1977) | 1977-02-13 |
| Beauden Barrett | New Zealand rugby union player | 1991-05-27 |
| Bryson DeChambeau | American professional golfer | 1993-09-16 |
| Cheslin Kolbe | South African rugby player | 1993-10-28 |
| Tyreek Hill | American football player (born 1994) | 1994-03-01 |
| Aaron Donald | American football player (born 1991) | 1991-05-23 |
| Calvin Johnson | American football player (born 1985) | 1985-09-29 |
| Collin Morikawa | American professional golfer | 1997-02-06 |
| Emmitt Smith | American football player (born 1969) | 1969-05-15 |
| Freddie Freeman | Canadian-American professional baseball player (born 1989) | 1989-09-12 |
| Gabriel Medina | Brazilian surfer | 1993-12-22 |
| Justin Thomas | American golfer | 1993-04-29 |
| Max Scherzer | American baseball player (born 1984) | 1984-07-27 |
| Gerrit Cole | American professional baseball player (born 1990) | 1990-09-08 |
| Justin Jefferson | American football player (born 1999) | 1999-06-16 |
| Ronald Acuña Jr. | Venezuelan professional baseball player (born 1997) | 1997-12-18 |
| Viktor Hovland | Norwegian golfer | 1997-09-18 |
| Tommy Caldwell | American rock climber | 1978-08-11 |
