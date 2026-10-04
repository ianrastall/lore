# Lore celebrity expansion — 4 October 2026

Added **50** contemporary and widely recognized figures to `Data/celebrities.json`: **19 Musicians, 15 Athletes, and 16 Actors**. The library grows from **162 to 212**. Existing entries are preserved.

**39 additions are rated AA and 11 are rated A** by Astro-Databank. AA indicates a birth certificate or record; A generally indicates a personal or family account, not certificate-level certainty. Entries based only on an inferred rising sign, rectification, or unresolved conflicting times were omitted even if otherwise popular.

The research prompt is reference material; this batch follows the user's music, sports, and movies priority instead of its older historical/category wishlist.

## Verification

All dates below are Gregorian. The local birth times are retained without UTC conversion or placeholders. Coordinates are converted from Astro-Databank's published degrees/minutes/seconds to decimal degrees, rounded to four places; the number of decimal places does not imply greater geographic accuracy than the source. Source place labels are normalized to `City, ST` or `City, Country`. IANA zones use the modern birthplace, and the fallback numeric offset is standard time; the tests verify the actual historical daylight-saving offset.

The corresponding records in `tests/Lore.Tests/Reference/adb-reference.json` include URLs, check date, source time standard, notes, and an expected UTC instant calculated independently from the source's stated offset.

| Figure | Category | Rating | Date and time as published | Source time standard | Source / resolution notes |
|---|---|---|---|---|---|
| [Billie Eilish](https://www.astro.com/astro-databank/Eilish%2C_Billie) | Musician | AA | 18 December 2001 at 12:17 (= 12:17 PM ) | PST h8w | BC/BR in hand. The birth certificate supersedes the earlier 11:30 Twitter claim. |
| [Olivia Rodrigo](https://www.astro.com/astro-databank/Rodrigo%2C_Olivia) | Musician | A | 20 February 2003 at 03:00 (= 03:00 AM ) | PST h8w | From memory. Recorded local clock time and Gregorian date retained. |
| [Dua Lipa](https://www.astro.com/astro-databank/Lipa%2C_Dua) | Musician | A | 22 August 1995 at 00:18 (= 12:18 AM ) | BST h1e | From memory. Her August 2023 statement gives 00:18 and supersedes earlier 11:45 AM/PM claims. |
| [Ariana Grande](https://www.astro.com/adbvip/adbvip_06_26.htm) | Musician | A | 26 June 1993 at 21:16 (= 9:16 PM ) | EDT h4w | From memory. Her father supplied 21:16; the conflicting baby-photo inscription was unverified fan material. |
| [Zendaya](https://www.astro.com/adbvip/adbvip_09_01.htm) | Actor | AA | 1 September 1996 at 18:01 (= 6:01 PM ) | PDT h7w | BC/BR in hand. The current birth-certificate record gives Walnut Creek; do not substitute Oakland. |
| [Miley Cyrus](https://www.astro.com/astro-databank/Cyrus%2C_Miley) | Musician | AA | 23 November 1992 at 16:19 (= 4:19 PM ) | CST h6w | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Kendrick Lamar](https://www.astro.com/astro-databank/Lamar%2C_Kendrick) | Musician | AA | 17 June 1987 at 15:04 (= 3:04 PM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [The Weeknd](https://www.astro.com/astro-databank/The_Weeknd) | Musician | A | 16 February 1990 at 14:45 (= 2:45 PM ) | EST h5w | From memory. Recorded local clock time and Gregorian date retained. |
| [Katy Perry](https://www.astro.com/astro-databank/Perry%2C_Katy) | Musician | AA | 25 October 1984 at 07:58 (= 07:58 AM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Lana Del Rey](https://www.astro.com/astro-databank/Del_Rey%2C_Lana) | Musician | A | 21 June 1985 at 16:47 (= 4:47 PM ) | EDT h4w | From memory. Her own statement gives 16:47; ADB replaced the earlier unsourced 02:46. |
| [Drake](https://www.astro.com/adbvip/adbvip_10_24.htm) | Musician | AA | 24 October 1986 at 02:31 (= 02:31 AM ) | EDT h4w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Doja Cat](https://www.astro.com/astro-databank/Doja_Cat) | Musician | A | 21 October 1995 at 07:14 (= 07:14 AM ) | PDT h7w | From memory. Her mother confirmed 07:14 after checking; ADB does not establish which document was checked. |
| [Demi Lovato](https://www.astro.com/astro-databank/Lovato%2C_Demi) | Musician | A | 20 August 1992 at 06:25 (= 06:25 AM ) | MDT h6w | From memory. Her March 2026 interview supplies 06:25, replacing the earlier speculative 06:00. |
| [Kesha](https://www.astro.com/astro-databank/Kesha) | Musician | AA | 1 March 1987 at 00:34 (= 12:34 AM ) | PST h8w | BC/BR in hand. The birth certificate gives 00:34 in Van Nuys, superseding her mother's approximate 00:10. |
| [Christina Aguilera](https://www.astro.com/astro-databank/Aguilera%2C_Christina) | Musician | A | 18 December 1980 at 10:46 (= 10:46 AM ) | EST h5w | From memory. Recorded local clock time and Gregorian date retained. |
| [John Legend](https://www.astro.com/astro-databank/Legend%2C_John) | Musician | AA | 28 December 1978 at 08:25 (= 08:25 AM ) | EST h5w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Frank Ocean](https://www.astro.com/astro-databank/Ocean%2C_Frank) | Musician | AA | 28 October 1987 at 07:14 (= 07:14 AM ) | PST h8w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Tyler, the Creator](https://www.astro.com/astro-databank/Tyler_the_Creator) | Musician | AA | 6 March 1991 at 15:17 (= 3:17 PM ) | PST h8w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Rosalía](https://www.astro.com/astro-databank/Rosalia) | Musician | AA | 25 September 1992 at 13:50 (= 1:50 PM ) | CEST h2e | BC/BR in hand. Use the record's 1992 date and Sant Cugat birthplace, rather than earlier conflicting online details. |
| [Finneas](https://www.astro.com/astro-databank/O%27Connell%2C_Finneas) | Musician | AA | 30 July 1997 at 23:21 (= 11:21 PM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Timothée Chalamet](https://www.astro.com/astro-databank/Chalamet%2C_Timoth%C3%A9e) | Actor | AA | 27 December 1995 at 21:16 (= 9:16 PM ) | EST h5w | Quoted BC/BR. The French birth-certificate copy records a birth in Manhattan; Nantes is the record source. |
| [Margot Robbie](https://www.astro.com/astro-databank/Robbie%2C_Margot) | Actor | A | 2 July 1990 at 07:45 (= 07:45 AM ) | AEST h10e | From memory. Her interview states 07:45; ADB identifies Gold Coast as the birthplace rather than hometown Dalby. |
| [Emma Watson](https://www.astro.com/astro-databank/Watson%2C_Emma) | Actor | AA | 15 April 1990 at 18:00 (= 6:00 PM ) | MEDT h2e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Jenna Ortega](https://www.astro.com/astro-databank/Ortega%2C_Jenna) | Actor | AA | 27 September 2002 at 17:59 (= 5:59 PM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Sydney Sweeney](https://www.astro.com/astro-databank/Sweeney%2C_Sydney) | Actor | AA | 12 September 1997 at 12:42 (= 12:42 PM ) | PDT h7w | BC/BR in hand. The birth certificate gives 12:42; the earlier noon chart was speculative. |
| [Michael B. Jordan](https://www.astro.com/astro-databank/Jordan%2C_Michael_B.) | Actor | AA | 9 February 1987 at 20:14 (= 8:14 PM ) | PST h8w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Blake Lively](https://www.astro.com/astro-databank/Lively%2C_Blake) | Actor | AA | 25 August 1987 at 05:07 (= 05:07 AM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Ben Affleck](https://www.astro.com/astro-databank/Affleck%2C_Ben) | Actor | AA | 15 August 1972 at 02:53 (= 02:53 AM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Matt Damon](https://www.astro.com/astro-databank/Damon%2C_Matt) | Actor | AA | 8 October 1970 at 15:22 (= 3:22 PM ) | EDT h4w | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Dwayne Johnson](https://www.astro.com/astro-databank/Johnson%2C_Dwayne) | Actor | AA | 2 May 1972 at 18:02 (= 6:02 PM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Chris Pratt](https://www.astro.com/astro-databank/Pratt%2C_Chris) | Actor | AA | 21 June 1979 at 16:31 (= 4:31 PM ) | CDT h5w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Chris Pine](https://www.astro.com/astro-databank/Pine%2C_Chris) | Actor | AA | 26 August 1980 at 08:25 (= 08:25 AM ) | PDT h7w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Kristen Stewart](https://www.astro.com/adbvip/adbvip_04_09.htm) | Actor | AA | 9 April 1990 at 09:21 (= 09:21 AM ) | PDT h7w | BC/BR in hand. The certificate gives 09:21 and supersedes the earlier unsourced 11:39. |
| [Dakota Johnson](https://www.astro.com/astro-databank/Johnson%2C_Dakota) | Actor | A | 4 October 1989 at 14:49 (= 2:49 PM ) | CDT h5w | News report. A contemporaneous birth announcement quotes the family's publicist for 14:49. |
| [Vin Diesel](https://www.astro.com/astro-databank/Diesel%2C%20Vin) | Actor | AA | 18 July 1967 at 15:35 (= 3:35 PM ) | PDT h7w | BC/BR in hand. The certificate gives 15:35, replacing the earlier speculative 14:30. |
| [LeBron James](https://www.astro.com/astro-databank/James%2C_LeBron) | Athlete | AA | 30 December 1984 at 16:04 (= 4:04 PM ) | EST h5w | BC/BR in hand. ADB retains the certificate's 16:04 over his conflicting 2026 recollection of 16:39. |
| [Stephen Curry](https://www.astro.com/astro-databank/Curry%2C_Stephen) | Athlete | AA | 14 March 1988 at 13:51 (= 1:51 PM ) | EST h5w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Travis Kelce](https://www.astro.com/astro-databank/Kelce%2C_Travis) | Athlete | AA | 5 October 1989 at 05:49 (= 05:49 AM ) | EDT h4w | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Kylian Mbappé](https://www.astro.com/astro-databank/Mbapp%C3%A9%2C_Kilian) | Athlete | AA | 20 December 1998 at 01:47 (= 01:47 AM ) | MET h1e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Neymar](https://www.astro.com/astro-databank/Neymar) | Athlete | AA | 5 February 1992 at 02:15 (= 02:15 AM ) | BZDT h2w | BC/BR in hand. ADB obtained the certificate in 2022 confirming 02:15. |
| [Novak Djokovic](https://www.astro.com/astro-databank/Djokovic%2C_Novak) | Athlete | AA | 22 May 1987 at 23:25 (= 11:25 PM ) | MEDT h2e | Quoted BC/BR. The quoted Belgrade civil registry record supersedes the earlier 11:00 claim. |
| [Simone Biles](https://www.astro.com/astro-databank/Biles%2C_Simone) | Athlete | AA | 14 March 1997 at 06:02 (= 06:02 AM ) | EST h5w | BC/BR in hand. The certificate gives 06:02, replacing earlier unsourced or rectified times. |
| [Max Verstappen](https://www.astro.com/astro-databank/Verstappen%2C_Max) | Athlete | A | 30 September 1997 at 13:20 (= 1:20 PM ) | CEST h2e | From memory. His official website reports his mother's delivery at 13:20. |
| [Tom Brady](https://www.astro.com/astro-databank/Brady%2C_Tom) | Athlete | AA | 3 August 1977 at 11:48 (= 11:48 AM ) | PDT h7w | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Antoine Griezmann](https://www.astro.com/astro-databank/Griezmann%2C_Antoine) | Athlete | AA | 21 March 1991 at 02:40 (= 02:40 AM ) | MET h1e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Karim Benzema](https://www.astro.com/astro-databank/Benzema%2C_Karim) | Athlete | AA | 19 December 1987 at 22:10 (= 10:10 PM ) | MET h1e | BC/BR in hand. Recorded local clock time and Gregorian date retained. |
| [Paul Pogba](https://www.astro.com/astro-databank/Pogba%2C_Paul) | Athlete | AA | 15 March 1993 at 01:45 (= 01:45 AM ) | MET h1e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Victor Wembanyama](https://www.astro.com/astro-databank/Wembanyama%2C_Victor) | Athlete | AA | 4 January 2004 at 15:00 (= 3:00 PM ) | CET h1e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Venus Williams](https://www.astro.com/adbvip/adbvip_06_17.htm) | Athlete | AA | 17 June 1980 at 14:12 (= 2:12 PM ) | PDT h7w | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |
| [Léon Marchand](https://www.astro.com/astro-databank/Marchand%2C_L%C3%A9on) | Athlete | AA | 17 May 2002 at 13:16 (= 1:16 PM ) | CEST h2e | Quoted BC/BR. Recorded local clock time and Gregorian date retained. |

## Checked and excluded

These were checked for this batch. A future source upgrade can change eligibility; recheck the current primary record before treating an exclusion as permanent.

| Figure | Rating | Reason |
|---|---|---|
| [Sabrina Carpenter](https://www.astro.com/astro-databank/Carpenter%2C_Sabrina) | DD | Conflicting 08:52 and 11:52 accounts. |
| [Harry Styles](https://www.astro.com/astro-databank/Styles%2C_Harry) | DD | Multiple conflicting, unverified birth times. |
| [Chappell Roan](https://www.astro.com/astro-databank/Roan%2C_Chappell) | C | 22:00 is speculative, inferred from a rising sign. |
| [Selena Gomez](https://www.astro.com/astro-databank/Gomez%2C_Selena) | C | Original source unknown; the reported hospital also conflicts with the birthplace. |
| [Justin Bieber](https://www.astro.com/astro-databank/Bieber%2C_Justin) | B | The listed time is rated biography/autobiography, below the required A. |
| [Adele](https://www.astro.com/astro-databank/Adele) | B | The listed time is rated biography/autobiography. |
| [Megan Thee Stallion](https://www.astro.com/astro-databank/Thee_Stallion%2C_Megan) | C | 11:00 is speculative, inferred from a rising sign. |
| [Lewis Hamilton](https://www.astro.com/astro-databank/Hamilton%2C_Lewis) | DD | Conflicting times. |
| [Carlos Alcaraz](https://www.astro.com/astro-databank/Alcaraz%2C_Carlos) | C | An alleged message supplies only an approximate time; accuracy is questioned. |
| [Michael Phelps](https://www.astro.com/astro-databank/Phelps%2C_Michael) | X | No documented birth time. |
| [Katie Ledecky](https://www.astro.com/astro-databank/Ledecky%2C_Katie) | X | No documented birth time. |
| [Ryan Gosling](https://www.astro.com/astro-databank/Gosling%2C_Ryan) | C | Time comes from IMDb trivia without a reliable original source. |
| [Daniel Radcliffe](https://www.astro.com/astro-databank/Radcliffe%2C_Daniel) | X | Birth certificate contains no recorded time. |
| [Pedro Pascal](https://www.astro.com/astro-databank/Pascal%2C_Pedro) | C | 13:15 is speculative; his family accounts conflict. |
| [Robert Pattinson](https://www.astro.com/astro-databank/Pattinson%2C_Robert) | C | Latest chart implies an approximate time, with contradictory earlier accounts. |
| [Ryan Reynolds](https://www.astro.com/astro-databank/Reynolds%2C_Ryan) | C | The original account has a conflicting birth year. |
| [Anne Hathaway](https://www.astro.com/astro-databank/Hathaway%2C_Anne) | C | Time appears in IMDb trivia without a reliable original source. |
| [Cardi B](https://www.astro.com/astro-databank/Cardi_B) | C | 18:10 is rectified from a rising sign. |
| [Lizzo](https://www.astro.com/astro-databank/Lizzo) | C | 14:30 is speculative, inferred from a rising sign. |
| [Shawn Mendes](https://www.astro.com/astro-databank/Mendes%2C_Shawn) | X | No documented birth time. |
| [Pink](https://www.astro.com/astro-databank/Pink) | X | The earlier 15:00 claim was removed as unsupported. |
| [Alicia Keys](https://www.astro.com/astro-databank/Keys%2C_Alicia) | C | Published chart and biographical accounts do not establish a reliable exact clock time. |
| [Usher](https://www.astro.com/astro-databank/Usher) | B | The listed time is rated biography/autobiography. |
| [Jennifer Lopez](https://www.astro.com/astro-databank/Lopez%2C_Jennifer) | X | Documented date without a recorded time. |
| [Mariah Carey](https://www.astro.com/astro-databank/Carey%2C_Mariah) | B | Biographical time and conflicting online birth years. |
| [Scarlett Johansson](https://www.astro.com/astro-databank/Johansson%2C_Scarlett) | C | 03:00 is rectified from an approximate time. |
| [Amanda Seyfried](https://www.astro.com/astro-databank/Seyfried%2C_Amanda) | X | Birth notice does not give a time. |
| [Jason Momoa](https://www.astro.com/astro-databank/Momoa%2C_Jason) | X | Birth notice does not give a time. |
| [Emma Stone](https://www.astro.com/astro-databank/Stone%2C_Emma) | A | Although rated A, 09:32 is inferred from an Ascendant degree, not a directly documented clock time. |
| [Becky G](https://www.astro.com/astro-databank/Becky_G) | A | Although rated A, 21:55 is an approximate time inferred from a pictured chart. |
| [Céline Dion](https://www.astro.com/astro-databank/Dion%2C_Celine) | A | Primary and alternative biography times differ (12:15 and 00:30); excluded pending resolution. |

No qualifying record was established in this pass for several other contemporary candidates, including Patrick Mahomes (the search surfaced his father), Kevin Durant, Naomi Osaka, Coco Gauff, Giannis Antetokounmpo, Jayson Tatum, Devin Booker, Shohei Ohtani, Tom Holland, and Florence Pugh. These remain research candidates, not confirmed rejections.

## Validation

- All six existing `FigureLibraryTests` pass with the expanded library, checking IDs, required data, categories, IANA zones, source ratings, birth dates/times, and UTC instants.
- Original figure records and their order are preserved; no duplicate IDs or names were introduced.
- Each new biography is one neutral sentence of 65–120 characters ending with a full stop.

