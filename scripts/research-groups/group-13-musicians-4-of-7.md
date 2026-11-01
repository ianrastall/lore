# Lore research group 13 of 37: Musicians 4 of 7

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
| FKA twigs | English singer and dancer (born 1988) | 1988-01-16 |
| Feist | Canadian singer | 1976-02-13 |
| Gary Numan | British musician (born 1958) | 1958-03-08 |
| Gustavo Dudamel | Venezuelan conductor and violinist | 1981-01-26 |
| Gustavo Santaolalla | Argentine musician and composer | 1951-08-19 |
| Lee Ranaldo | American rock musician (born 1956) | 1956-02-03 |
| Mark Hoppus | American musician and record producer | 1972-03-15 |
| Martin Gore | English songwriter, musician, singer, record producer, remixer, and DJ; founding member of Depeche Mode | 1961-07-23 |
| Renée Fleming | American opera soprano | 1959-02-14 |
| Thurston Moore | American guitarist (born 1958) | 1958-07-25 |
| Chad Smith | American drummer | 1961-10-25 |
| Ed O'Brien | English guitarist and member of Radiohead | 1968-04-15 |
| Julian Casablancas | American singer | 1978-08-23 |
| Larry Mullen Jr. | Irish drummer, member of the rock group U2 | 1961-10-31 |
| Maynard James Keenan | American singer | 1964-04-17 |
| Colin Greenwood | Bass player for rock band Radiohead.Bass player for Nick Cave and the Bad Seed. | 1969-06-26 |
| Esperanza Spalding | American jazz bassist and singer (born 1984) | 1984-10-18 |
| Flavor Flav | American rapper and television personality (born 1959) | 1959-03-16 |
| Philip Selway | English drummer | 1967-05-23 |
| Brandi Carlile | American alternative country and folk rock singer-songwriter (born 1981) | 1981-06-01 |
| Gladys Knight | American singer (born 1944) | 1944-05-28 |
| Miranda July | American performance artist, musician and writer (born 1974) | 1974-02-15 |
| Pete Wentz | American musician | 1979-06-05 |
| PinkPantheress | British singer-songwriter | 2001-04-19 |
| St. Vincent | American musician (born 1982) | 1982-09-28 |
| Ayra Starr | Beninese-Nigerian singer | 2002-06-14 |
| Hilary Hahn | American classical violinist | 1979-11-27 |
| Kim Gordon | American musician and artist, most known as co-founding member of Sonic Youth (born 1953) | 1953-04-28 |
| Patrick Stump | American musician (born 1984) | 1984-04-27 |
| Wizkid | Nigerian singer and songwriter (born 1990) | 1990-07-16 |
| András Schiff | Hungarian-born British musician (1953–) | 1953-12-21 |
| Audra McDonald | American actress and singer (born 1970) | 1970-07-03 |
| Fred Armisen | American comedian, actor and musician | 1966-12-04 |
| Geddy Lee | Canadian musician | 1953-07-29 |
| Jarvis Cocker | English singer and musician (born 1963) | 1963-09-19 |
| Johnny Marr | English musician (born 1963) | 1963-10-31 |
| Lindsey Buckingham | American musician | 1949-10-03 |
| Lucinda Williams | American rock, folk, blues, and country music singer, songwriter and musician | 1953-01-26 |
| Neil Tennant | English musician | 1954-07-10 |
| Rubén Blades | Panamanian musician (born 1948) | 1948-07-16 |
| Tems | Nigerian singer-songwriter (born 1995) | 1995-06-11 |
| Patti LuPone | American actress and singer (born 1949) | 1949-04-21 |
| Rema | Nigerian singer-songwriter and rapper (2000–) | 2000-05-01 |
| Rivers Cuomo | American musician (born 1970) | 1970-06-13 |
