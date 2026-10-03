# Research prompt: finding hospital datasets for Lore

Paste everything below the line into a deep-research tool (Gemini Deep Research, ChatGPT, etc.).

This prompt asks for **sources**, not for hospitals one by one: a research tool cannot reliably type out tens of thousands of coordinates, but it can find the official lists that already contain them. Bring the result back and the best datasets can be downloaded, checked, and merged into `Data/hospitals.json` by script.

---

I maintain a free, open-source desktop astrology application. When a user enters a birth, they can search for the **hospital they were born in** to get precise birthplace coordinates. Today the app has about 26,400 hospitals, drawn from Wikidata, and coverage is far too thin for most users. I want to grow it to roughly **60,000–80,000 hospitals**, with much better coverage of the countries where most people live.

I need you to find **downloadable, machine-readable datasets that list individual hospitals**, which I can import by script. I do NOT want you to list hospitals yourself.

## Current coverage (hospitals in my database now)

| Country | Hospitals now |
|---|---|
| United States | 3,309 |
| India | 2,433 |
| China | 883 |
| Indonesia | 440 |
| Pakistan | 93 |
| Brazil | 198 |
| Bangladesh | 42 |
| Russia | 200 |
| Mexico | 130 |
| Philippines | 123 |
| Egypt | 97 |
| Vietnam | 52 |
| Iran | 110 |
| Turkey | 250 |
| Germany | 1,141 |
| United Kingdom | 1,667 |
| France | 569 |
| Italy | 341 |
| Canada | 442 |
| Australia | 340 |
| South Africa | 298 |
| Kenya | 34 |
| Colombia | 20 |
| Argentina | 140 |

Netherlands, Ethiopia, Tanzania, the Democratic Republic of the Congo and Myanmar have almost none.

## What to look for

For each country, in roughly the order of the table above (most populous and thinnest first), find the best available dataset of hospitals. Good kinds of source:

- Government health-ministry or regulator registries of licensed facilities (for example a national hospital register, a facility master list, or an official open-data portal).
- National statistics offices and official open-data portals (data.gov-style sites).
- Reputable international or academic datasets of health facilities (for example WHO or World Bank facility lists, or peer-reviewed geocoded facility datasets).

Also look specifically for **historical** sources, since users were born decades ago in hospitals that may since have closed or been renamed:

- Datasets or registers that include **closed, merged, or renamed hospitals**, with dates.
- Lists of former **maternity hospitals and maternity homes** (many people born 1920–1980 were born in these rather than in general hospitals).

Do NOT include OpenStreetMap, Wikidata, or datasets derived from them (such as healthsites.io); I already use those.

## For each dataset, report

- **Country** (or countries) covered.
- **Name** of the dataset and **publisher**.
- **Direct download URL** (or the exact page where the download link is), and the **format** (CSV, XLSX, JSON, GeoJSON, shapefile, API).
- **Approximate number of records**, and how many of those are hospitals rather than clinics, pharmacies or health posts — and which field or value distinguishes them.
- **Fields available**: name, city/town, address, postcode, latitude/longitude. If there are no coordinates, say whether addresses are complete enough to geocode.
- **Whether closed or historical facilities are included**, and back to what year.
- **Last updated** date.
- **Licence**, quoted exactly, and whether it allows me to **redistribute** the data inside a free application (and what attribution it requires). Flag anything with no clear licence or with terms that forbid redistribution or automated download.
- **Language/script** of the names (e.g. Chinese characters, Cyrillic), and whether an English or romanised name field exists.

## What to return

1. **A ranked table** of the datasets, best first, judged by: how many hospitals it would add, whether it has coordinates, licence clarity, and population of the country covered.
2. **A JSON array** with one object per dataset using the fields above, so I can work through them by script.
3. **A short list of countries where you found no usable open dataset**, and what you checked, so I know not to search again.
4. **A separate short list of historical sources** (closed hospitals, maternity homes), even if they are not machine-readable — say what form they take.

Before answering: open every URL you report and confirm it exists and leads to the data; do not guess URLs or record counts; and quote licences rather than paraphrasing them.
