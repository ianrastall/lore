# Lore figure library expansion — 31 October 2026

Added **13** figures to `content/celebrities.json`. The library grows from **1,314 to 1,327**. Twelve are rated AA and one A. Eleven were born in the 1990s or 2000s: the library had 53 people born since 1990 and now has 64.

## How this differs from the last batch

The batch of 7 October read about 2,500 pages by driving a browser. That is not to be repeated. Astro-Databank now answers a script with a "Checking your browser" page, and its `robots.txt` has `User-agent: anthropic-ai` / `Disallow: /`. Getting past a bot check is not something to automate.

These came through ordinary web search restricted to astro.com, one name at a time. A search returns a summary of the page rather than the page, so it is slower and gives less: 40 names were tried, and not every field of every record could be seen. Where a field was not shown, the table says so. Anyone with a browser can check a record by opening its link in `tests/Lore.Tests/Reference/adb-reference.json`.

## Added

| Figure | Born | Place | Standard | Rating, collector |
|---|---|---|---|---|
| Louane | 1996-11-26 16:00 | Sainte-Catherine, France | CET h1e | AA, Brun, certificate in hand |
| Kendji Girac | 1996-07-03 16:00 | Saint-Astier, France | MEDT h2e | AA, Geslain |
| Adèle Exarchopoulos | 1993-11-22 01:40 | Paris 14, France | MET h1e | AA, Brun |
| Squeezie | 1996-01-27 11:00 | Vitry-sur-Seine, France | CET h1e | AA, De Jabrun |
| Zohran Mamdani | 1991-10-18 01:15 | Kampala, Uganda | EAT h3e | A, Scholfield, confirmed by his mother |
| Cyprien | 1989-05-12 03:00 | Nice, France | MEDT h2e | AA, Lepoivre |
| Orelsan | 1982-08-01 21:10 | Alençon, France | MEDT h2e | AA, Geslain |
| Aurélien Tchouaméni | 2000-01-27 18:22 | Rouen, France | CET h1e | AA, Geslain |
| Léna Situations | 1997-11-19 07:06 | Paris 17, France | CET h1e | AA, Lepoivre |
| Vianney | 1991-02-13 16:25 | Pau, France | MET h1e | AA, De Jabrun |
| Hailee Steinfeld | 1996-12-11 17:26 | Tarzana, CA | PST h8w | AA, Viktor |
| William Saliba | 2001-03-24 06:50 | Bondy, France | CET h1e | AA, Geslain |
| Kingsley Coman | 1996-06-13 09:30 | Paris 14, France | MEDT h2e | AA, De Jabrun |

Eleven of the thirteen were born in France, for the reason given in the last batch: French records state the hour.

Three things were not seen in the search result and are marked in the reference file's `note`:

- **Zohran Mamdani's time standard.** East Africa Time, in force in Uganda in 1991, is taken to be it.
- **Squeezie's place** was given as Vitry, taken to be Vitry-sur-Seine.
- **Coordinates** for Squeezie, Orelsan, Léna Situations and Hailee Steinfeld are the town's or the arrondissement's, not the record's. A few arcminutes make no difference to the planets and little to the angles.

`FigureLibraryTests` checks every figure's date, time, rating and UTC instant against its reference entry. Both were made from the same reading of the record, so the test catches a slip in copying and cannot catch a wrong summary.

## Looked up and left out

**Conflicting (DD):** Taylor Swift (08:36, against 05:17 in a biography), Harry Styles, Sabrina Carpenter.

**Rated C:** Paul Mescal (rectified).

**Rated X, no time:** Alexander Zverev, Coco Gauff, Jacinda Ardern, Tadej Pogačar.

**Rated A, left out on the notes:** Dakota Fanning (an approximate time given in an interview), Zoë Kravitz (a round time from a news report, with notes that could not be read in full).

**No record or no time found by search:** Lily-Rose Depp, Pierre Niney, Emma Raducanu, Clara Luciani, Bad Bunny, Tate McRae, Barry Keoghan, Jeremy Allen White, Glen Powell, Jude Bellingham, Lamine Yamal, Elle Fanning, Shailene Woodley, Kev Adams, Thibaut Pinot, Marcus Thuram, Mike Maignan. Some of these may have records the search did not reach. Elle Fanning, Clara Luciani and Shailene Woodley do have pages; their fields were not shown.

## What would add more

Most of the current English-language names people look for have no reliable time, so a wider search will not change that. To reach further, use `scripts/celebrity-research-prompt.md`, or look names up by hand. The search results also pointed at pages worth trying next: Dayot Upamecano, Mattéo Guendouzi, Vitaa, Camille, Norman and Soko.
