# Lore research group 26 of 37: Writers 1 of 2

Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. Below is a table of 49 people. Check each of them against the rules that follow, and return a full record for each one who passes. Do not add anyone who is not in the table.

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

## The 49 people to check

| Name | Who | Born |
|---|---|---|
| Orhan Pamuk | Turkish novelist, academic and Nobel laureate (born 1952) | 1952-06-07 |
| Alice Munro | Canadian writer (1931–2024) | 1931-07-10 |
| Herta Müller | German-Romanian novelist, poet and essayist (born 1953) | 1953-08-17 |
| Kazuo Ishiguro | British novelist (born 1954) | 1954-11-08 |
| J.M. Coetzee | South African and Australian writer and scholar (born 1940) | 1940-02-09 |
| Wole Soyinka | Nigerian writer and playwright (born 1934) | 1934-07-13 |
| Han Kang | South Korean writer | 1970-11-27 |
| Abdulrazak Gurnah | Tanzanian-born British writer, winner of the Nobel Prize in Literature (2021) (born 1948) | 1948-01-01 |
| Jon Fosse | Norwegian writer, playwright, and translator (born 1959) | 1959-09-29 |
| Thomas Pynchon | American novelist (born 1937) | 1937-05-08 |
| Philip Pullman | English author (born 1946) | 1946-10-19 |
| Matt Groening | American cartoonist (born 1954) | 1954-02-15 |
| Alan Moore | British comic book author (born 1953) | 1953-11-18 |
| Tom Stoppard | Czech–English playwright (1937–2025) | 1937-07-03 |
| William Gibson | American-Canadian speculative fiction writer (born 1948) | 1948-03-17 |
| John Green | American author and vlogger | 1977-08-24 |
| Ian McEwan | British author (born 1948) | 1948-06-21 |
| Julian Barnes | English writer (born 1946) | 1946-01-19 |
| Joyce Carol Oates | American author (born 1938) | 1938-06-16 |
| Donna Tartt | American writer | 1963-12-23 |
| Zadie Smith | English novelist, essayist and short story writer (born 1975) | 1975-10-25 |
| Yann Martel | Canadian writer | 1963-06-25 |
| Eiichiro Oda | Japanese manga artist | 1975-01-01 |
| Don DeLillo | American novelist, playwright and essayist (born 1936) | 1936-11-20 |
| Annie Proulx | American novelist, short story and non-fiction author (b. 1935) | 1935-08-22 |
| Amy Tan | American novelist | 1952-02-19 |
| Sally Rooney | Irish author | 1991-02-20 |
| Jonathan Franzen | American writer | 1959-08-17 |
| Liu Cixin | Chinese science fiction writer | 1963-06-23 |
| Rumiko Takahashi | Japanese manga artist | 1957-10-10 |
| Brandon Sanderson | American fantasy writer (born 1975) | 1975-12-19 |
| Michael Chabon | American novelist, short story writer, essayist | 1963-05-24 |
| Barbara Kingsolver | American author, poet and essayist | 1955-04-08 |
| Bill Bryson | American-British author (born 1951) | 1951-12-08 |
| Marilynne Robinson | American novelist and essayist (born 1943) | 1943-11-26 |
| Kim Stanley Robinson | American science fiction writer (born 1952) | 1952-03-23 |
| Harlan Coben | American writer | 1962-01-04 |
| George Saunders | American writer (born 1958) | 1958-12-02 |
| Colson Whitehead | American novelist (born 1969) | 1969-11-06 |
| Fiona Staples | Canadian comic book artist, of Saga | not known |
| N.K. Jemisin | American science fiction and fantasy writer | 1972-09-19 |
| Neal Stephenson | American science fiction writer (born 1959) | 1959-10-31 |
| Hiromu Arakawa | Japanese manga artist (born 1973) | 1973-05-08 |
| Jeffrey Eugenides | Novelist, short story writer, academic | 1960-03-08 |
| Junji Ito | Japanese horror writer and mangaka | 1963-07-31 |
| Louise Erdrich | American-Ojibwe poet, novelist, and children's writer (born 1954) | 1954-06-07 |
| Susanna Clarke | British author (1959-) | 1959-11-01 |
| Bill Watterson | American comic artist | 1958-07-05 |
| Bruce Sterling | American author, speaker and futurist (born 1954) | 1954-04-14 |
