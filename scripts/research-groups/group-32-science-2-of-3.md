# Lore research group 32 of 37: Science 2 of 3

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
| Anne L'Huillier | French physicist | 1958-08-16 |
| Anton Zeilinger | Austrian quantum physicist (born 1945) | 1945-05-20 |
| Gérard Mourou | French physicist, winner of Nobel Prize in Physics 2018 | 1944-06-22 |
| Ken Thompson | American programmer and computer scientist, co-creator of the Unix operating system (born 1943) | 1943-02-04 |
| Shinya Yamanaka | Japanese doctor and medical scientist (born 1962) | 1962-09-04 |
| Barry Barish | American physicist | 1936-01-27 |
| Jack Szostak | American biologist | 1952-11-09 |
| Reinhard Genzel | German astronomer | 1952-03-24 |
| Demis Hassabis | British artificial intelligence researcher (born 1976) | 1976-07-27 |
| Edvard Moser | Norwegian psychologist and neuroscientist | 1962-04-27 |
| James Gosling | Canadian computer scientist | 1955-05-19 |
| Vint Cerf | American computer scientist (born 1943) | 1943-06-23 |
| Edward Witten | American theoretical physicist | 1951-08-26 |
| John Clauser | American physicist | 1942-12-01 |
| Ferenc Krausz | Austrian-Hungarian laser physicist and university teacher (born 1962) | 1962-05-17 |
| John O'Keefe | American British neuroscientist, 2014 Nobel laureate in Physiology or Medicine | 1939-11-18 |
| Maryna Viazovska | Ukrainian mathematician (born 1984) | 1984-12-02 |
| Terence Tao | Australian-American mathematician | 1975-07-17 |
| Barbara Liskov | American computer scientist | 1939-11-07 |
| Gregory Winter | British biochemist (born 1951) | 1951-04-14 |
| Pierre Agostini | French physicist | 1941-07-23 |
| Samantha Cristoforetti | Italian European Space Agency astronaut, former Italian Air Force pilot and engineer (1977) | 1977-04-26 |
| Brian Greene | American physicist | 1963-02-09 |
| Drew Weissman | American physician-scientist (born 1959) | 1959-09-07 |
| George P. Smith | Nobel prize winning US chemist | 1941-03-10 |
| Eileen Collins | Astronaut and United States Air Force pilot | 1956-11-19 |
| Sylvia Earle | American oceanographer | 1935-08-30 |
| David Baker | American biochemist and computational biologist (born 1962) | 1962-10-06 |
| Brian Kernighan | Canadian computer scientist, co-inventor of the Unix operating system | 1942-01-01 |
| Louis E. Brus | American chemist (1943–2026) | 1943-08-10 |
| Morten Meldal | Danish chemist (born 1954) | 1954-01-16 |
| Reid Wiseman | American astronaut | 1975-11-11 |
| John Jumper | American chemist and AI expert | 1985-00-00 |
| Leslie Lamport | American computer scientist | 1941-02-07 |
| Moungi Bawendi | American chemist of French and Tunisian descent | 1961-03-15 |
| Peggy Whitson | American astronaut | 1960-02-09 |
| Victor Glover | American astronaut (born 1976) | 1976-04-30 |
| Brian Cox (Physicist) | English physicist and musician | 1968-03-03 |
| Shafi Goldwasser | American computer scientist | 1958-11-14 |
| Stephen Wolfram | British-American scientist and businessman (born 1959) | 1959-08-29 |
| Cédric Villani | French mathematician and politician | 1973-10-05 |
| Mark Kelly | American politician, astronaut, and naval officer (born 1964) | 1964-02-21 |
| Adi Shamir | Israeli cryptographer (born 1952) | 1952-07-06 |
| Manjul Bhargava | Indo-Canadian mathematician | 1974-08-08 |
| Yann LeCun | American-French computer scientist (born 1960) | 1959-07-08 |
| Bob Kahn | American Internet pioneer, computer scientist (born 1938) | 1938-12-23 |
