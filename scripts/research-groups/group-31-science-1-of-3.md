# Lore research group 31 of 37: Science 1 of 3

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
| Linus Torvalds | Finnish software engineer (born 1969) | 1969-12-28 |
| Richard Dawkins | English ethologist, evolutionary biologist and author (born 1941) | 1941-03-26 |
| Donald Knuth | American computer scientist and mathematician (born 1938) | 1938-01-10 |
| Roger Penrose | English mathematical physicist, recreational mathematician and philosopher | 1931-08-08 |
| Donna Strickland | Canadian physicist, Nobel laurate | 1959-05-27 |
| Ada Yonath | Israeli structural biologist and Nobel laureate (1939–2026) | 1939-06-22 |
| Paul Krugman | American economist (born 1953) | 1953-02-28 |
| Jennifer Doudna | American biochemist | 1964-02-19 |
| Carol Greider | American molecular biologist | 1961-04-15 |
| Elizabeth Blackburn | Australian-American biological researcher | 1948-11-26 |
| Emmanuelle Charpentier | French microbiologist and biochemist (born 1968) | 1968-12-11 |
| Grigori Perelman | Russian mathematician (born 1966) | 1966-06-13 |
| Neil deGrasse Tyson | American astrophysicist and science communicator | 1958-10-05 |
| Frances Arnold | Nobel prize winning US scientist and engineer (born 1956) | 1956-07-25 |
| François Englert | Belgian theoretical physicist (1932–2026) | 1932-11-06 |
| Venki Ramakrishnan | Nobel prize winning American and British structural biologist | 1952-01-01 |
| Michio Kaku | U.S.-American theoretical physicist, futurist and author (1947-) | 1947-01-24 |
| Saul Perlmutter | American astrophysicist and Nobel laureate | 1959-09-22 |
| Andrea Ghez | American astronomer | 1965-06-16 |
| Katalin Karikó | Hungarian biochemist | 1955-01-17 |
| May-Britt Moser | Norwegian psychologist and neuroscientist | 1963-01-04 |
| Sunita Williams | American astronaut and naval officer | 1965-09-19 |
| Andre Geim | Russian-born Dutch-British physicist | 1958-10-21 |
| Konstantin Novoselov | Russian-British physicist known for graphene work | 1974-08-23 |
| Geoffrey Hinton | British-Canadian computer scientist and psychologist | 1947-12-06 |
| Santiago Calatrava | Spanish-Swiss engineer, architect, and artist (born 1951) | 1951-07-28 |
| Adam Riess | American astrophysicist | 1969-12-16 |
| Brian Schmidt | American-born Australian astrophysicist | 1967-02-24 |
| Svante Pääbo | Swedish geneticist (born 1955) | 1955-04-20 |
| Takaaki Kajita | Japanese physicist and astronomer | 1959-03-09 |
| Andrew Wiles | English mathematician (1953–) | 1953-04-11 |
| Carolyn Bertozzi | American chemist (born 1966) | 1966-10-10 |
| Kip Thorne | American physicist (born 1940) | 1940-06-01 |
| Michel Mayor | Swiss astrophysicist & Nobel laureate of Physics | 1942-01-12 |
| Mae Jemison | American doctor and NASA astronaut | 1956-10-17 |
| Bjarne Stroustrup | Danish computer scientist, creator of C++ (born 1950) | 1950-12-30 |
| Didier Queloz | Swiss astronomer | 1966-02-23 |
| Jared Diamond | American scientist, historian, and author (born 1937) | 1937-09-10 |
| K. Barry Sharpless | American chemist and Nobel Laureate (born 1941) | 1941-04-28 |
| Arthur B. McDonald | Canadian physicist (born 1943) | 1943-08-29 |
| Jocelyn Bell Burnell | British astrophysicist (born 1943) | 1943-07-15 |
| Rainer Weiss | American physicist (1932–2025) | 1932-09-29 |
| Alain Aspect | French physicist | 1947-06-15 |
| Christina Koch | American astronaut (born 1979) | 1979-01-29 |
| Steven Pinker | Canadian-American cognitive psychologist, linguist, and author, an advocate of evolutionary psychology and the computational theory of the mind | 1954-09-18 |
