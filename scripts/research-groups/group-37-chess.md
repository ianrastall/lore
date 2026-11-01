# Lore research group 37 of 37: Chess

Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. Below is a table of 37 people. Check each of them against the rules that follow, and return a full record for each one who passes. Do not add anyone who is not in the table.

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

## The 37 people to check

| Name | Who | Born |
|---|---|---|
| Viswanathan Anand | Indian chess grandmaster | 1969-12-11 |
| Anatoly Karpov | Russian chess player | 1951-05-23 |
| Vladimir Kramnik | Russian chess grandmaster | 1975-06-25 |
| Judit Polgár | Hungarian chess player | 1976-07-23 |
| Gukesh Dommaraju | Indian chess grandmaster | 2006-05-29 |
| Ding Liren | Chinese grandmaster and former World Chess Champion | 1992-10-24 |
| Hikaru Nakamura | American chess player | 1987-12-09 |
| Fabiano Caruana | Italian-American chess player | 1992-07-30 |
| Ian Nepomniachtchi | Russian chess player | 1990-07-14 |
| Susan Polgár | Hungarian chess player (born 1969) | 1969-04-19 |
| Levon Aronian | Armenian-American chess player | 1982-10-06 |
| Hou Yifan | Chinese chess player | 1994-02-27 |
| Alexandra Kosteniuk | Russian and Swiss chess player | 1984-04-23 |
| Boris Gelfand | Belarusian-Israeli chess player | 1968-06-24 |
| Shakhriyar Mamedyarov | Azerbaijani chess player | 1985-04-12 |
| Vasyl Ivanchuk | Ukrainian chess player | 1969-03-18 |
| Humpy Koneru | Indian chess player | 1987-03-31 |
| Ju Wenjun | Chinese chess player | 1991-01-31 |
| Teimour Radjabov | Azerbaijani chess player | 1987-03-12 |
| Praggnanandhaa Rameshbabu | Indian chess player | 2005-08-10 |
| Nigel Short | English chess player and writer | 1965-06-01 |
| Alireza Firouzja | French-Iranian chess player | 2003-06-18 |
| Michael Adams | British chess grandmaster | 1971-11-17 |
| Anish Giri | Dutch chess player | 1994-06-28 |
| Peter Svidler | Russian chess player | 1976-06-17 |
| David Howell | English chess grandmaster | 1990-11-14 |
| Wesley So | Filipino-American chess player | 1993-10-09 |
| Harika Dronavalli | Indian Chess.com player | 1991-01-12 |
| Nodirbek Abdusattorov | Uzbek chess grandmaster (born 2004) | 2004-09-18 |
| Tan Zhongyi | Chinese chess player | 1991-05-29 |
| Maxime Vachier-Lagrave | French chess player | 1990-10-21 |
| Pia Cramling | Swedish chess player | 1963-04-23 |
| Vaishali Rameshbabu | Indian chess player | 2001-06-21 |
| Arjun Erigaisi | Indian chess player | 2003-09-03 |
| Lei Tingjie | Chinese chess player | 1997-03-03 |
| Bibisara Assaubayeva | Kazakhstani chess player | 2004-02-26 |
| Luke McShane | British chess player | 1984-01-07 |
