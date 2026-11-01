# Lore research group 9 of 37: Actors 9 of 9

Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. Below is a table of 46 people. Check each of them against the rules that follow, and return a full record for each one who passes. Do not add anyone who is not in the table.

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

## The 46 people to check

| Name | Who | Born |
|---|---|---|
| William Daniels | American actor (born 1927) | 1927-03-31 |
| Alba Rohrwacher | Italian actress | 1979-02-27 |
| Britt Lower | American actress | 1985-08-02 |
| Jack Lowden | Scottish actor | 1990-06-02 |
| Keeley Hawes | British actress | 1976-02-10 |
| Suraj Sharma | Indian actor | 1993-03-21 |
| John Mulaney | American comedian and actor (born 1982) | 1982-08-26 |
| Niecy Nash | American actress | 1970-02-23 |
| Quinta Brunson | American comedian, writer, producer and actress | 1989-12-21 |
| Rob Corddry | American actor and comedian | 1971-02-04 |
| Cooper Hoffman | American film actor | 2003-00-00 |
| Leslie Jones | American comedian and actress (born 1967) | 1967-09-07 |
| Marc Maron | American comedian, actor, and media personality (born 1965) | 1963-09-27 |
| Sutton Foster | American actress | 1975-03-18 |
| KiKi Layne | American actress | 1991-12-10 |
| Molly Gordon | American actress | 1994-12-06 |
| Rob Brydon | British actor | 1965-05-03 |
| Rob Delaney | American comedian and actor | 1977-01-19 |
| Marisa Abela | English actress | 1996-12-08 |
| Toby Huss | American actor | 1966-12-09 |
| Michelle Gomez | Scottish actress | 1966-11-23 |
| Brett Goldstein | British actor and comedian | 1980-00-00 |
| Karl Pilkington | English television personality, social commentator, actor, author and former radio producer | 1972-09-23 |
| Matt Berry | British actor and comedian (born 1974) | 1974-05-02 |
| Noel Fielding | British comedian and actor | 1973-05-21 |
| Vicky McClure | British actress (born 1983) | 1983-05-08 |
| Hannah Einbinder | American comedian and actress | 1995-05-21 |
| Rhys Darby | New Zealand actor, comedian and voice actor | 1974-03-21 |
| Harvey Guillén | American actor | 1990-05-03 |
| Mike Birbiglia | Comedian, actor, writer | 1978-06-20 |
| Natasha Rothwell | American actress and writer (born 1980) | 1980-10-18 |
| Dominic Sessa | American actor | 2002-10-25 |
| Kayvan Novak | British actor | 1978-11-23 |
| Myha'la | American actress | 1996-04-06 |
| Shamier Anderson | Canadian actor | 1991-05-06 |
| Sharon Horgan | Irish actress, writer and comedian | 1970-07-13 |
| Sheila Atim | Ugandan actress | 1991-01-09 |
| Yerin Ha | Australian actress | 1998-00-00 |
| Natasia Demetriou | British actress | 1984-01-15 |
| Paapa Essiedu | British actor | 1990-06-11 |
| Nick Mohammed | British actor | 1980-10-04 |
| Paula Pell | American actor, writer and producer | 1963-04-15 |
| Phil Dunster | English actor | 1992-03-31 |
| Richa Moorjani | Indian-American actress | 1989-05-26 |
| Rose Matafeo | New Zealand comedian and actor | 1992-02-25 |
| Roy Wood Jr. | American comedian and actor | 1978-12-11 |
