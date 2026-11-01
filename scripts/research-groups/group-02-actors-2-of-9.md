# Lore research group 2 of 37: Actors 2 of 9

Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. Below is a table of 45 people. Check each of them against the rules that follow, and return a full record for each one who passes. Do not add anyone who is not in the table.

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

## The 45 people to check

| Name | Who | Born |
|---|---|---|
| David Duchovny | American actor and writer | 1960-08-07 |
| Geoffrey Rush | Australian actor (born 1951) | 1951-07-06 |
| Adrien Brody | American actor | 1973-04-14 |
| John Malkovich | American actor | 1953-12-09 |
| Kaley Cuoco | American actress and producer (born 1985) | 1985-11-30 |
| Joey King | American actress (born 1999) | 1999-07-30 |
| Larry David | American comedian, writer and actor (born 1947) | 1947-07-02 |
| Laurence Fishburne | American actor | 1961-07-30 |
| Rosamund Pike | British actress (born 1979) | 1979-01-27 |
| Saoirse Ronan | Irish-American actress (born 1994) | 1994-04-12 |
| Alicia Vikander | Swedish actress (born 1988) | 1988-10-03 |
| Benicio Del Toro | Puerto Rican actor (born 1967) | 1967-02-19 |
| Holly Hunter | American actress (born 1958) | 1958-03-20 |
| Steve Buscemi | American actor | 1957-12-13 |
| Claire Danes | American actor (born 1979) | 1979-04-12 |
| Laura Dern | American actress and producer (born 1967) | 1967-02-10 |
| Michael Fassbender | German-Irish actor (born 1979) | 1977-04-02 |
| Sophie Turner | British actress (born 1996) | 1996-02-21 |
| Vera Farmiga | American actress (born 1973) | 1973-08-06 |
| Catherine O'Hara | Canadian-American actress (1954–2026) | 1954-03-04 |
| Kit Harington | English actor (born 1986) | 1986-12-26 |
| Mads Mikkelsen | Danish actor (born 1965) | 1965-11-22 |
| Mark Hamill | American actor (born 1951) | 1951-09-25 |
| Sam Neill | New Zealand actor (1947–2026) | 1947-09-14 |
| Elle Fanning | American actress (born 1998) | 1998-04-09 |
| Joe Pesci | American actor | 1943-02-09 |
| Sean Bean | British actor (born 1959) | 1959-04-17 |
| Sienna Miller | British-American actress (born 1981) | 1981-12-28 |
| Carey Mulligan | British actress (born 1985) | 1985-05-28 |
| Michelle Williams | American actress (born 1980) | 1980-09-09 |
| Olivia Colman | British actress | 1974-01-30 |
| Patricia Arquette | American actress (born 1968) | 1968-04-08 |
| Thomas Haden Church | American actor | 1960-06-17 |
| Don Cheadle | American actor (born 1964) | 1964-11-29 |
| James McAvoy | Scottish actor (born 1979) | 1979-04-21 |
| Jesse Eisenberg | American actor | 1983-10-05 |
| Robin Wright | American actress | 1966-04-08 |
| Sissy Spacek | American actress | 1949-12-25 |
| Angela Bassett | American actress (born 1958) | 1958-08-16 |
| Channing Tatum | American actor, dancer and model (born 1980) | 1980-04-26 |
| Dev Patel | British actor (born 1990) | 1990-04-23 |
| Harvey Keitel | American actor | 1939-05-13 |
| Jenny Slate | American actress and comedian (born 1982) | 1982-03-25 |
| Lena Headey | British actress (born 1973) | 1973-10-03 |
| Lucy Lawless | New Zealand actress | 1968-03-29 |
