# Lore research group 7 of 37: Actors 7 of 9

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
| Michelle Dockery | British actress | 1981-12-15 |
| Ncuti Gatwa | Rwandan-Scottish actor | 1992-10-15 |
| Peter Sarsgaard | American actor (born 1971) | 1971-03-07 |
| Rinko Kikuchi | Japanese actress | 1981-01-06 |
| Sandra Hüller | German actress (born 1978) | 1978-04-30 |
| Tom Hollander | British actor | 1967-08-25 |
| Matthew Macfadyen | English actor (born 1974) | 1974-10-17 |
| Natasha Lyonne | American actress (born 1979) | 1979-04-04 |
| Toby Jones | British actor | 1966-09-07 |
| Walton Goggins | American actor (born 1971) | 1971-11-10 |
| Zoe Kazan | American actress | 1983-09-09 |
| Jenna Fischer | American actress | 1974-03-07 |
| Jeremy Allen White | American actor (born 1991) | 1991-02-28 |
| Seth Meyers | American comedian, television host and actor | 1973-12-28 |
| Stephen Graham | English actor (born 1973) | 1973-08-03 |
| Steve Coogan | English actor and comedian | 1965-10-14 |
| Yahya Abdul-Mateen II | American actor | 1986-07-15 |
| Ben Mendelsohn | Australian actor | 1969-04-03 |
| Daisy Edgar-Jones | British actress (born 1998) | 1998-05-24 |
| Hannah Waddingham | British actress | 1974-07-28 |
| Jeremy Strong | American actor (born 1978) | 1978-12-25 |
| Rhea Perlman | American actress | 1948-03-31 |
| Stephen Root | American actor | 1951-11-17 |
| Tony Hale | American actor | 1970-09-30 |
| Uzo Aduba | American actress (born 1981) | 1981-02-10 |
| Ed Helms | American actor and comedian | 1974-01-24 |
| Ellie Kemper | American actress | 1980-05-02 |
| Josh O'Connor | British actor (born 1990) | 1990-05-20 |
| Miriam Margolyes | British-Australian actress | 1941-05-18 |
| Nick Offerman | American actor | 1970-06-26 |
| Tracy Morgan | American actor and comedian | 1968-11-10 |
| B.J. Novak | American actor | 1979-07-30 |
| Bae Doona | South Korean actress | 1979-10-11 |
| Charlie Heaton | British actor | 1994-02-06 |
| Doug Jones | American actor, contortionist and mime | 1960-05-24 |
| Kumail Nanjiani | Pakistani-American comedian and actor | 1978-05-02 |
| Nick Kroll | American comedian and actor | 1978-06-05 |
| Paddy Considine | British actor | 1973-09-05 |
| Pamela Adlon | American actress | 1966-07-09 |
| Song Kang-ho | South Korean actor | 1967-01-17 |
| Sterling K. Brown | American actor | 1976-04-05 |
| Wanda Sykes | American comedian, writer, actress and voice artist | 1964-03-07 |
| Wil Wheaton | American actor and writer | 1972-07-29 |
| Álvaro Morte | Spanish actor (born 1975) | 1975-02-23 |
| Ali Wong | American comedian and actress (born 1982) | 1982-04-19 |
