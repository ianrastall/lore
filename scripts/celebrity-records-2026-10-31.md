# Lore figure library records — 31 October 2026

The fuller record planned in section 4.6b of `docs/next-steps.md`, gathered from Wikidata by Q-number with `scripts/wikidata_records.py`. This pass adds data only: nothing in the app reads it yet.

## Where it lives

`content/records/figures.json` holds one record for each figure, keyed by id. `content/records/connections.json` holds the links between two people who are both in the library. `content/celebrities.json` stays as it was, with only the `wikidataId` and `sitelinks` that lead here.

The folder is not installed with the app. `InstalledFilesTests` requires every top-level `content\*.json` to ship, and a subfolder keeps these out until something uses them. When a view starts reading one, add it to `Lore.App.csproj` and move or list it there. `FigureRecordsTests` holds the data sound until then.

## What there is

| Field | Figures with it |
|---|---|
| Birth name | 912 |
| Other names (4,580 in all) | 1,051 |
| Occupations (9,828 in all) | 1,312 |
| Portrait file name | 1,301 |
| Dated events | 1,023 |

| Event | Events | Figures |
|---|---|---|
| Marriage | 1,229 | 750 |
| Child | 1,089 | 540 |
| Major award | 2,181 | 623 |
| Divorce | 274 | 171 |
| Marriage ended, cause not stated | 357 | 241 |

**252 connections** join 289 of the figures: 63 partners, 52 parent and child, 44 in the same group, 38 spouses, 30 siblings and 25 other relatives. 87 are dated. There are fewer than the figure might suggest because both people have to be in the library, and a figure needs a well-documented birth time to be there: John Lennon, for one, is not, so The Beatles come to three connections rather than six.

Dates say as much as is known: `1967-05-01`, `1967-05` or `1967`. Old Style dates are converted. In a connection, `from` and `to` are the marriage's or the relationship's. For a `relative`, `kinship` is what the second person is to the first.

## Decided along the way

- **Major awards** are those with pages in at least 20 Wikipedias and sister projects, which is an outside measure rather than a list of ours. It keeps 357 awards and leaves out 9,008 statements of lesser ones.
- **Children with no page of their own are left out, 710 of them.** Most are private people, many of them living, and a date of birth is theirs. For the same reason a spouse with no page keeps the marriage's date without a name.

## What Wikidata lacked

- 401 figures have no birth name stated, and 262 no other name.
- Only Kris Jenner has no occupation.
- 12 have no portrait: Robert Bresson, Robert Mapplethorpe, Gianni Versace, Cristóbal Balenciaga, Vladimir Jankélévitch, Carlos Castaneda, Linda Goodman, Stephen Arroyo, Jeane Dixon, Liliane Bettencourt, Marcel Bich, Jean-Luc Delarue.
- 209 marriages have no date, 16 children have no date and 796 major awards have no date, so none of those became events.
- 290 figures have no dated event at all.

## What Wikidata got wrong

Left out by the script:

- A child of Shaquille O'Neal dated 1961, before he was born.
- Hank Williams's marriage ending in 1954, after his death.
- Three noble titles and three general honours stated as awards (`NOT_AWARDS`).
- USA for Africa and Artists Stand Up to Cancer, one-off charity records stated as bands, and an item pairing Salvador Dalí with Louise Bourgeois that nothing bears out (`NOT_GROUPS`).
- Two vandalised aliases (`NOT_NAMES`).
- 10 birth names not in the Latin alphabet were moved to other names, among them a Greek name given for Jennifer Aniston and Arabic mixed into Tiger Woods's.

Kept, and to be read with care:

- **`partner` includes rumour.** John F. Kennedy and Marilyn Monroe are stated as partners, and so is Kennedy with Marlene Dietrich. If a reading is to say these people "did" know each other, as 4.6b proposes, this relation needs reading first.
- **`relative` has odd claims,** such as Jules Ferry as an ancestor of Luc Ferry, and Michael Jackson and Stevie Wonder as third cousins. They could not be confirmed here.
- **Duos of convenience count as groups:** "Karl Marx and Friedrich Engels" and "Martin Scorsese and Robert De Niro" are items on Wikidata, and they do show that the two worked together.
- Children born after a father's death are kept: Clark Gable's son, Hank Williams's daughter, Yves Klein's son and Claus von Stauffenberg's daughter are all real.

## Birth times

`birthTimeUncertaintyMinutes` is now 15 on the 646 figures whose time falls on :00 or :30, and on no others. Opening one of these charts now shows "± 15 min" beside the time and the "If the birth time is off" panel, which checks 31 charts in about 3 ms. Nothing else about a chart or its report changes, and the score and the bands are untouched.

Across those 646 the Rising sign is in doubt for 159 (25%), the Midheaven for 156 (24%) and day or night for 20 (3%). 636 have something that depends on the exact time, usually a house. The library's lists show nothing different.
