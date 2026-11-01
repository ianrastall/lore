# Lore research group 10 of 37: Musicians 1 of 7

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
| Enya | Irish singer (born 1961) | 1961-05-17 |
| Ed Sheeran | English singer-songwriter (born 1991) | 1991-02-17 |
| Weird Al Yankovic | American comedy musician (born 1959) | 1959-10-23 |
| Rosé | South Korean-New Zealand singer (born 1997) | 1997-02-11 |
| John Cale | Welsh composer, singer-songwriter and record producer (born 1942) | 1942-03-09 |
| Hugh Laurie | English actor, comedian, director, musician and writer (born 1959) | 1959-06-11 |
| Lisa Manoban | Thai rapper, singer, dancer and actress (born 1997) | 1997-03-27 |
| Jennie Kim | South Korean singer-songwriter and rapper (born 1996) | 1996-01-16 |
| Roger Waters | English musician, co-founder of Pink Floyd (born 1943) | 1943-09-06 |
| Juanes | Colombian musician (born 1972) | 1972-08-09 |
| Norah Jones | American musician (born 1979) | 1979-03-30 |
| Kate Bush | English singer, pianist and songwriter (born 1958) | 1958-07-30 |
| Jeff Goldblum | American actor and musician | 1952-10-22 |
| Cyndi Lauper | American singer | 1953-06-22 |
| Lars Ulrich | Danish musician (born 1963) | 1963-12-26 |
| Billie Joe Armstrong | American rock musician (born 1972) | 1972-02-17 |
| Bruce Dickinson | English heavy metal singer (born 1958) | 1958-08-07 |
| Jennifer Hudson | American singer, actress, model and spokesperson | 1981-09-12 |
| Arvo Pärt | Estonian composer (born 1935) | 1935-09-11 |
| Jisoo | South Korean singer-songwriter, actress, and model  (born 1995) | 1995-01-03 |
| Hayley Williams | American singer | 1988-12-27 |
| Sam Smith | British singer-songwriter (born 1992) | 1992-05-19 |
| Tracy Chapman | American singer-songwriter (born 1964) | 1964-03-30 |
| Slash | British guitarist (born 1965) | 1965-07-23 |
| Angélique Kidjo | Beninese-French world music singer (born 1960) | 1960-07-14 |
| Bad Bunny | Puerto Rican rapper, singer, and record producer (born 1994) | 1994-03-10 |
| Brian Eno | British musician, music producer, music theorist and visual artist (born 1948) | 1948-05-15 |
| Dave Mustaine | American musician | 1961-09-13 |
| Kirk Hammett | American guitarist | 1962-11-18 |
| Julie Delpy | French and American actress, director, screenwriter and singer | 1969-12-21 |
| Sade Adu | British singer | 1959-01-16 |
| Michael Bublé | Canadian singer, songwriter and actor | 1975-09-09 |
| Rick Astley | British singer | 1966-02-06 |
| Carly Rae Jepsen | Canadian singer (born 1985) | 1985-11-21 |
| Danny Elfman | American film composer (born 1953) | 1953-05-29 |
| John Mayer | American musician (born 1977) | 1977-10-16 |
| Morrissey | British singer and songwriter | 1959-05-22 |
| Noel Gallagher | British singer and guitarist | 1967-05-29 |
| Post Malone | American singer and rapper (born 1995) | 1995-07-04 |
| Yo-Yo Ma | American cellist (born 1955) | 1955-10-07 |
| Jimin | South Korean singer and dancer (BTS) | 1995-10-13 |
| Mandy Patinkin | American actor and singer (born 1952) | 1952-11-30 |
| Finn Wolfhard | Canadian actor and musician (born 2002) | 2002-12-23 |
| Donald Glover | American actor, comedian, musician, and filmmaker (born 1983) | 1983-09-25 |
