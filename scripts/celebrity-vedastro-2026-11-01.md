# VedAstro list: 9,810 more figures, 1 November 2026

The library goes from 1,408 figures to 11,218, taking everyone from
[VedAstro's list of famous people](https://huggingface.co/datasets/vedastro-org/15000-Famous-People-Birth-Date-Location)
who could be identified and had a clock standard that could be stated. It follows the
[77 added on 31 October](celebrity-vedastro-2026-10-31.md). Each figure is credited to Astro-Databank
and VedAstro. Ian's test for leaving someone out applies: would someone feel bad knowing the
connection was there?

| Stage | People |
|---|---:|
| On the list and not in the library | 14,873 |
| No certain match on Wikidata (same birth date and name) | 3,792 |
| Born before 1583 | 21 |
| Already in the library under another name, or the same item twice | 66 |
| Matched | 10,994 |
| Left out by Ian's test | 557 |
| Clock standard uncertain | 553 |
| Other gaps (see below) | 74 |
| **Added** | **9,810** |

## Identity

A person is matched only when a Wikidata item has the same birth date and the same name. Names,
dates of death, occupations and sitelinks come from Wikidata. Categories come from the Wikidata
description first and the occupations after it, and the biography is a short sentence built from
the description, the birthplace and the year.

## Who was left out

Everyone convicted of a violent, sexual, hate or war crime, or of major fraud; anyone listed as a
killer, terrorist, gangster or similar; members of Nazi and fascist parties, the SS and the Klan;
German officers of the Nazi years and those who served Vichy; dictators and their households; adult
film performers; and everyone removed or left out by choice in earlier batches. The automatic
screen was read through, and the 1,700 best-known people who passed it were read by name. It will
still have missed some of the 8,000 less well known. Convictions that say more about the state than
the person (gross indecency, sedition, draft refusal) and tax or similar offences do not count.
Anyone rated too low when read on Astro-Databank in October also stays out.

## Clock standards

VedAstro's offsets are not Astro-Databank's. They look computed from today's time-zone tables: of 925
figures in both, 83 differ by more than 15 minutes, usually a whole hour. So the offsets were not
used. Each figure takes Lore's own rule (the time-zone database, or local mean time from the
birthplace's longitude before standard time). That rule was first checked against the 1,331 figures
with Astro-Databank's stated standard, and it matches 845 of the 854 outside the US. The places
where it fails were then ruled on:

| Where | Rule |
|---|---|
| US, on daylight time before 1967, outside wartime | CA, MA, NJ and NY keep daylight time. AL, MO, NC and TX take standard time, as all their records do. Everywhere else is left out (414) |
| Germany outside Berlin, 24 May to 24 September 1945 | UTC+2, not the double summer time of Berlin |
| France, June 1940 to November 1942 | Left out: two zones, two clocks (48) |
| Alsace and Lorraine, 1871 to 1918 | Left out: German time (46) |
| Netherlands before 1941 | Local time to May 1892, then Amsterdam Time, and left out from May 1916 (40), as the database no longer holds it |
| Denmark, Norway and Sweden on daylight time before 1980, Iceland before 1968, Luxembourg before 1941 | Left out |

Tommy Lee Jones, in the 31 October batch, moves to standard time by the Texas rule. Edwin Moses
(Dayton, OH, 1955) and Bill Berry (Duluth, MN, 1958) fall in states with mixed records, and stay on
daylight time until someone reads their pages.

## Birthplaces

Where the list's place was more than a degree from Wikidata's, or gave only a country, Wikidata's
birthplace is used: 336 figures. Seventeen people with no city anywhere are left out, as are 51
whose date of death is known only to the year, and 4 with nothing to say about them. Two people
under 18 are left out.
