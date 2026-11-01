# Research group 1 (Actors 1 of 9): results, 31 October 2026

The first group back from an LLM. Of 45 people it accepted 15 and left out 30, and the answer
accounted for all 45. Nine went into the library at first, taking it from 1,313 to 1,322, and six
were held until someone read their record. Ian read two of those pages the same day, and both
matched, so 11 are added and 4 held.

## How the answer was checked

The answer agrees with itself: dates, times and ratings match across its three files, and every
`expectedUtc` is right when worked out again from the stated time standard. That proves nothing
about the birth times. Its notes say some Astro-Databank pages were blocked and indexed text was
used instead, so each accepted person was looked up separately, by ordinary web search restricted
to astro.com, one name at a time.

## Added: time, rating and place confirmed by that search

| Figure | Born | Place | Standard | Rating, source |
|---|---|---|---|---|
| Jason Bateman | 1969-01-14 17:39 | Rye, NY | EST h5w | A, from memory |
| Julianne Moore | 1960-12-03 17:53 | Fayetteville, NC | EST h5w | AA, BC/BR in hand |
| Peter Dinklage | 1969-06-11 01:37 | Point Pleasant, NJ | EDT h4w | AA, BC/BR in hand |
| Christina Ricci | 1980-02-12 18:30 | Santa Monica, CA | PST h8w | AA, quoted BC/BR |
| Jeremy Irons | 1948-09-19 02:00 | Ventnor, England | GDT h1e | A, from memory |
| Bryan Cranston | 1956-03-07 10:52 | Los Angeles, CA | PST h8w | AA, quoted BC/BR |
| Willem Dafoe | 1955-07-22 19:30 | Appleton, WI | CST h6w | AA, BC/BR in hand |
| Rami Malek | 1981-05-12 08:41 | Torrance, CA | PDT h7w | AA, BC/BR in hand |
| Elizabeth Olsen | 1989-02-16 18:49 | Van Nuys, CA | PST h8w | AA, BC/BR in hand |
| Kirsten Dunst | 1982-04-30 13:00 | Point Pleasant, NJ | EDT h4w | AA, BC/BR in hand; page read by Ian |
| Brendan Fraser | 1968-12-03 15:28 | Indianapolis, IN | EST h5w | AA, BC/BR in hand; page read by Ian |

Willem Dafoe carries `utcOffsetFixed`, because Astro-Databank gives CST for Appleton in July 1955
and the time-zone database gives daylight time. 39 figures now do so.

Wikidata gives different birthplaces for six of them: Moore (Fort Bragg, next to Fayetteville),
Dinklage (Morristown), Irons (Cowes), Cranston (Hollywood), Malek (Los Angeles) and Olsen (Sherman
Oaks). The library follows Astro-Databank, and for Dinklage, Moore and Malek its place comes with
the certificate. Morristown is the furthest from the recorded place, about 50 miles.

## Held: accepted by the LLM, not confirmed

The search could not show these records. Nothing contradicts them, but the library's rule is to
leave out anyone in doubt, so they wait until someone opens the link. The records are in the zip,
`lore-birth-time-audit.zip`.

| Person | The LLM's reading | Rating | Note |
|---|---|---|---|
| [John Goodman](https://www.astro.com/astro-databank/Goodman,_John) | 1952-06-20 10:32, Affton, MO, CST | A | Would need `utcOffsetFixed` |
| [Forest Whitaker](https://www.astro.com/astro-databank/Whitaker,_Forest) | 1961-07-15 10:30, Longview, TX, CST | A | Would need `utcOffsetFixed` |
| [Aamir Khan](https://www.astro.com/astro-databank/Khan,_Aamir) | 1965-03-14 10:05, Mumbai, IST | A | |
| [Alia Bhatt](https://www.astro.com/astro-databank/Bhatt,_Alia) | 1993-03-15 04:10, Mumbai, IST | A | |

## Left out, with a reason from the record

These count as rejections. **C:** Gillian Anderson (source unknown), Deepika Padukone (approximate
time from her mother's friend), Henry Cavill (interview source not established), Eddie Redmayne
(20:00 inferred from a story about the night). **X:** Helena Bonham Carter, Frances McDormand,
Woody Harrelson.

Deepika Padukone's record gives 1986-01-05. The table in the prompt said 1987, from Wikidata. She
is out either way.

## Not settled: still open

The other 23 are not rejections. The answer says of each that no qualifying record could be
verified, and its notes say that "a missing verified record is not a claim that no record exists".
Those pages may simply have been blocked. They stay research candidates and are worth a run in
another tool:

Mila Kunis, Tom Holland, Simon Pegg, Reese Witherspoon, Jason Statham, Emily Blunt, Emilia Clarke,
Adam Sandler, Rachel McAdams, Rachel Weisz, Cillian Murphy, Jennifer Connelly, Rupert Grint, Tom
Hardy, Kristen Bell, Andy Serkis, Christoph Waltz, Lucy Liu, Michael Keaton, Millie Bobby Brown,
Seth MacFarlane, Sacha Baron Cohen, Taron Egerton.

For Witherspoon, Waltz and Brown it found a time on other chart sites, and rightly did not accept
it without a source trail.

## Validation

- `FigureLibraryTests` pass over all 1,322 figures, including each UTC instant against the
  reference.
- A wrong summary from the search would not be caught: both records rest on the same reading.
