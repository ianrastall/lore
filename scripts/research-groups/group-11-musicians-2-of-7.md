# Lore research group 11 of 37: Musicians 2 of 7

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
| Krist Novoselic | American rock musician | 1965-05-16 |
| Mary Steenburgen | American actress, singer and songwriter | 1953-02-08 |
| Eddie Vedder | American singer and guitarist | 1964-12-23 |
| Flea | Australian musician | 1962-10-16 |
| Awkwafina | American actress, comedian, and rapper | 1988-06-02 |
| Elvis Costello | English singer-songwriter (born 1954) | 1954-08-25 |
| Suga | South Korean rapper, singer, record producer and songwriter | 1993-03-09 |
| Jin | South Korean singer | 1992-12-04 |
| Mary J. Blige | American singer (born 1971) | 1971-01-11 |
| RM | South Korean rapper, singer, songwriter, record producer, and leader of the boy band BTS | 1994-09-12 |
| Tony Iommi | British rock guitarist (born 1948) | 1948-02-19 |
| Busta Rhymes | American rapper (born 1972) | 1972-05-20 |
| Joe Hisaishi | Japanese composer, conductor, pianist | 1950-12-06 |
| John Paul Jones | British bassist | 1946-01-03 |
| Mike Dirnt | American musician (born 1972) | 1972-05-04 |
| Steve Harris | British bassist | 1956-03-12 |
| Troye Sivan | Australian singer-songwriter and actor | 1995-06-05 |
| Dionne Warwick | American singer (born 1940) | 1940-12-12 |
| Howard Shore | Canadian film score composer (born 1946) | 1946-10-18 |
| Alex Turner | British musician (born 1986) | 1986-01-06 |
| Grace Jones | Jamaican singer, model and actress | 1948-05-19 |
| John Frusciante | American guitarist | 1970-03-05 |
| Lang Lang | Chinese pianist | 1982-06-14 |
| Robert Trujillo | American bass guitarist | 1964-10-23 |
| RuPaul | American drag performer, actor, and musician | 1960-11-17 |
| Alexandre Desplat | French film composer (born 1961) | 1961-08-23 |
| Bryan Ferry | British singer (born 1945) | 1945-09-26 |
| Chaka Khan | American singer (born 1953) | 1953-03-23 |
| Gracie Abrams | American singer-songwriter (born 1999) | 1999-09-07 |
| Itzhak Perlman | Israeli-American violinist and conductor (born 1945) | 1945-08-31 |
| Jemaine Clement | New Zealand actor and musician | 1974-01-10 |
| PJ Harvey | English singer-songwriter | 1969-10-09 |
| Robert Smith | English singer, multi-instrumentalist and songwriter | 1959-04-21 |
| Steve Vai | American guitarist | 1960-06-06 |
| Erykah Badu | American singer-songwriter (born 1971) | 1971-02-26 |
| Janelle Monáe | American singer and actress (born 1985) | 1985-12-01 |
| Karol G | Colombian singer and songwriter | 1991-02-14 |
| Maya Hawke | American actress and singer-songwriter (born 1998) | 1998-07-08 |
| Sara Bareilles | American recording artist; pop rock singer-songwriter and pianist | 1979-12-07 |
| Gerard Way | American singer (born 1977) | 1977-04-09 |
| Sonny Rollins | American jazz saxophonist and composer (1930–2026) | 1930-09-07 |
| Toni Collette | Australian actress and musician | 1972-11-01 |
| LL Cool J | American rapper and actor (born 1968) | 1968-01-14 |
| Rob Halford | British heavy metal singer | 1951-08-25 |
| Travis Barker | American drummer (born 1975) | 1975-11-14 |
