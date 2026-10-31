# Lore figure library death dates — 31 October 2026

`content/celebrities.json` now gives a `deathDate` for each of the **757** dead among its 1,313 figures, and a `wikidataId` and `sitelinks` for all 1,313. The other **556** are living and carry no date. Dates are Gregorian, as the birth dates are, so an Old Style date in a source is converted.

## How the dates were found

`scripts/wikidata_deaths.py` searches Wikidata for each name and accepts a human whose date of birth is the figure's. Astro-Databank was not scripted (see `celebrity-expansion-2026-10-31.md`).

- **1,299** matched on name and birth date. **14** did not, and were traced by hand (below).
- Every match was then set against English Wikipedia: its "NNNN deaths" and "Living people" categories, and the dates in the article's opening sentence.
- The opening sentence agreed on the day for **718** of the dead. Everyone Wikidata gives as living is in "Living people".
- The four deaths of August and September 2026 (Philippe Bouvard, Dolly Parton, Harald V, Gloria Steinem) were also checked against news reports.

The rule for a disagreement: Wikidata's only statement, or its preferred one, stands unless a second source shows it wrong. Where Wikidata has two statements of equal rank, English Wikipedia decides.

## Changed by hand

| Figure | Date | Why |
|---|---|---|
| Bernardo Bertolucci | 2018-11-26 | Wikidata has 22 November, which is wrong |
| Amelia Earhart | 1937-07-02 | Disappeared. Wikidata has the legal declaration of 5 January 1939 |
| Andrés Segovia | 1987-06-02 | Wikidata prefers 3 June; English and Spanish Wikipedia have 2 June |
| Marilyn Monroe | 1962-08-04 | Two statements of equal rank |
| Pierre-Auguste Renoir | 1919-12-03 | Two statements of equal rank |
| Jean Genet | 1986-04-15 | Two statements of equal rank |
| Hans-Georg Gadamer | 2002-03-13 | Two statements of equal rank |
| Jeane Dixon | 1997-01-25 | Two statements of equal rank |
| Alfred Krupp | 1887-07-14 | Two statements of equal rank |
| Just Fontaine | 2023-02-28 | Two statements of equal rank; French Wikipedia has 1 March |
| Anne Frank | 1945-02-15 | **The day is not known.** Sources give February or March 1945; this is the middle of the month Wikidata prefers |
| Alfred Wegener | 1930-11-15 | **The day is not known.** He died in November 1930; this is the middle of the month |

## Kept, against English Wikipedia

Each of these differs by a day, and none changes who overlaps whom: Jacques Tati, Georges Carpentier, Honoré Daumier, Cristóbal Balenciaga, Georges Bataille, Federico García Lorca, Tristan Tzara. Paul Bocuse also stays at 20 January 2018, which French Wikipedia has: the English article's opening gives other dates for both his birth and his death.

Seven of the first sentences disagree only because they quote the Old Style date: Michelangelo, Dürer, Copernicus, Henry VIII, Elizabeth I, Edward VI, Luther.

## Not matched by birth date

The search found these people, or missed them under another name, and the birth dates differ. Each was identified by hand. `deathDate`, `wikidataId` and `sitelinks` are filled in; the library keeps Astro-Databank's birth date, which the tests hold it to.

| Figure | Library | Wikidata | Item |
|---|---|---|---|
| Leonardo da Vinci | 1452-04-23 | 1452-04-15 O.S. | Q762 |
| Niccolò Machiavelli | 1469-05-11 | 1469-05-03 O.S. | Q1399 |
| Henri IV | 1553-12-24 | 1553-12-13 | Q936976 |
| Marie de Médicis | 1575-05-06 | 1575-04-26 | Q182021 |
| Gaston Bachelard | 1884-06-26 | 1884-06-27 | Q270800 |
| Andrés Segovia | 1893-03-17 | 1893-02-21 | Q192529 |
| Juan Perón | 1895-10-07 | 1895-10-08 | Q93330 |
| Tristan Tzara | 1896-04-28 | 1896-04-16 | Q16409 |
| Chuck Yeager | 1923-02-24 | 1923-02-13 | Q271939 |
| Pelé | 1940-10-21 | 1940-10-23 | Q12897 |
| John Belushi | 1949-01-25 | 1949-01-24 | Q23858 |
| Eva Green | 1980-07-05 | 1980-07-06 | Q191644 |

Most of these are Old Style dates or a day apart. **Segovia and Yeager are out by weeks and are worth looking up.** Queen Elizabeth II (Q9682) and Caroline of Monaco (Q233966) agree on the date and were simply not found under the names the library uses.

## Wikidata's labels

Labels are not to be trusted as names: the search also returned "Megan Foxiana" for Megan Fox and "Christian Dopplerr" for Christian Doppler, both of them vandalism at the time of the run. Matching went by birth date, so neither affected the result, and nothing in the library comes from a label.
