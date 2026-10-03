#!/usr/bin/env python3
"""
Generate Data/hospitals.json (the app's hospital-search database).

Sources merged:

  1. Data/hospitals_wikidata.csv  -- the original Wikidata export (curated but
     notability-filtered: only hospitals someone made a Wikidata item for, so
     it misses most small/local hospitals). Always used; committed to the repo.

  2. OpenStreetMap, via the Overpass API  -- every `amenity=hospital` /
     `healthcare=hospital` feature worldwide, including the small local ones
     Wikidata lacks. Optional (needs network); skip with --no-osm.

  Older hospitals (skip with --no-history). People were born decades ago in
  hospitals that have since been renamed or closed, so these add searchable
  entries for them:

  3. Former names of current hospitals: OpenStreetMap's old_name / was:name
     tags, and Wikidata's aliases and dated former official names. Each becomes
     its own entry at the hospital's location, e.g.
     "Waterford Regional Hospital (now University Hospital Waterford)".

  4. Former hospitals in OpenStreetMap: buildings tagged as having been a
     hospital (disused:/abandoned:/was:/historic:amenity=hospital,
     historic=hospital), labelled "(former hospital)".

  5. OpenHistoricalMap (CC0), the historical sister project of OpenStreetMap,
     whose hospitals carry start and end dates -- labelled e.g. "(1878-1945)".

Entries without a city get the nearest town from Data/cities.json (within
25 km), and entries without a country (OpenHistoricalMap) get the country of
the nearest town.

Both feed the same output schema (matches Models/Hospital.cs):
    {"name","city","country","lat","lon"}

The app resolves the historical, DST-aware time zone from the coordinates, so
no timezone/population column is needed.

--------------------------------------------------------------------------------
Running it
--------------------------------------------------------------------------------
Standard library only -- no `pip install` required. Run from the project root:

    python scripts/build-hospitals.py                 # Wikidata + all countries
    python scripts/build-hospitals.py --countries US,GB,DE
    python scripts/build-hospitals.py --no-osm        # CSV only (old behaviour)

OpenStreetMap is fetched one country at a time (each hospital is labelled with
the country whose administrative boundary the query used, since OSM's own
addr:country tag is usually absent). Results are cached under
Data/.osm_cache/ so a re-run or a crash resumes instead of refetching; pass
--refresh to ignore the cache.

Note on the OECD API: it is a *statistics* store (GDP, prices, health
aggregates such as beds-per-1000) and contains no directory of individual
hospitals with coordinates -- it cannot feed this file.

Data: Wikidata (CC0 1.0), OpenHistoricalMap (CC0 1.0) and OpenStreetMap
(c) OpenStreetMap contributors, ODbL.
Respect the Overpass API usage policy: https://dev.overpass-api.de/overpass-doc/
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSV_IN = ROOT / "Data" / "hospitals_wikidata.csv"
CITIES_IN = ROOT / "Data" / "cities.json"
JSON_OUT = ROOT / "Data" / "hospitals.json"
CACHE_DIR = ROOT / "Data" / ".osm_cache"

# Bump when the per-country OSM query or its processing changes, so stale cache
# files from an older version are refetched rather than silently reused.
OSM_CACHE_VERSION = 2

OHM_ENDPOINT = "https://overpass-api.openhistoricalmap.org/api/interpreter"
WDQS_ENDPOINT = "https://query.wikidata.org/sparql"

# Public Overpass mirrors, tried in rotation so one overloaded server (a 504)
# doesn't stall the run. Override with --endpoint or OVERPASS_URL to pin one.
DEFAULT_ENDPOINTS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
USER_AGENT = "LoreHospitalBuilder/1.0 (https://github.com/ianrastall/lore)"

# Coordinate precision for both output and de-duplication. 4 dp ~= 11 m.
COORD_DP = 4
# Two entries are treated as the same hospital when their names match (case-
# insensitively) and their coordinates agree to this many decimals (~111 m).
# Coarser than COORD_DP so the same hospital listed by both sources collapses,
# without merging genuinely distinct hospitals that share a name.
DEDUP_DP = 3


# ── helpers ────────────────────────────────────────────────────────────────

def _valid_coord(lat: float, lon: float) -> bool:
    return -90.0 <= lat <= 90.0 and -180.0 <= lon <= 180.0


def _dedup_key(name: str, lat: float, lon: float) -> tuple:
    return (name.casefold().strip(), round(lat, DEDUP_DP), round(lon, DEDUP_DP))


def _record(name: str, city: str, country: str, lat: float, lon: float) -> dict:
    return {
        "name": name.strip(),
        "city": city.strip(),
        "country": country.strip(),
        "lat": round(lat, COORD_DP),
        "lon": round(lon, COORD_DP),
    }


def _year(value: str | None) -> str:
    """First four-digit year in an OSM/OHM date ("1903-01-01", "~1850", "1890s")."""
    m = re.search(r"\d{4}", value or "")
    return m.group(0) if m else ""


def _norm(name: str) -> str:
    """Name reduced to unaccented lowercase letters and digits, for "is this really
    a different name?" ("Hopital" and "Hôpital" are not)."""
    plain = unicodedata.normalize("NFKD", name.casefold())
    plain = "".join(c for c in plain if not unicodedata.combining(c))
    return re.sub(r"[^0-9a-z]", "", plain.replace("saint", "st"))


def _useful_alias(alias: str, current: str) -> bool:
    """A former/alternative name worth its own search entry: not the same name in
    different punctuation, and not a bare acronym such as "UCLH"."""
    alias = alias.strip()
    if len(alias) < 6 or " " not in alias:
        return False
    if alias.upper() == alias and not any(c.isdigit() for c in alias):
        return False
    return _norm(alias) != _norm(current)


def _split_names(value: str | None) -> list[str]:
    """OSM packs several values into one tag with ';'."""
    return [v.strip() for v in (value or "").split(";") if v.strip()]


# ── source 1: Wikidata CSV ───────────────────────────────────────────────────

def load_wikidata() -> list[dict]:
    if not CSV_IN.exists():
        print(f"  (no {CSV_IN.name}; skipping Wikidata base)")
        return []

    out: list[dict] = []
    skipped = 0
    with CSV_IN.open(encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            name = (row.get("Hospital") or "").strip()
            if not name:
                skipped += 1
                continue
            try:
                lat = float(row["Latitude"])
                lon = float(row["Longitude"])
            except (KeyError, ValueError, TypeError):
                skipped += 1
                continue
            if not _valid_coord(lat, lon):
                skipped += 1
                continue
            out.append(_record(name, (row.get("City") or ""),
                               (row.get("Country") or ""), lat, lon))
    print(f"  Wikidata: {len(out):,} hospitals ({skipped} rows skipped)")
    return out


# ── source 2: OpenStreetMap via Overpass ────────────────────────────────────

def _overpass(endpoints: list[str], query: str,
              tries: int = 8, base_delay: int = 6) -> dict:
    """POST an Overpass QL query, rotating across mirrors and retrying.

    A 504/429 means the server is busy, not that the query is wrong, so each
    retry moves to the next mirror in the list and backs off (capped at 60s).
    """
    data = urllib.parse.urlencode({"data": query}).encode()
    last_err: Exception | None = None
    for attempt in range(1, tries + 1):
        endpoint = endpoints[(attempt - 1) % len(endpoints)]
        try:
            req = urllib.request.Request(
                endpoint, data=data, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=900) as resp:
                payload = json.load(resp)
            # A server that runs out of time or memory mid-query still answers
            # 200, with a "remark" and partial (often zero) results. Treat that
            # as a failure to retry, not as "this country has no hospitals".
            remark = str(payload.get("remark") or "").lower()
            if "error" in remark or "timed out" in remark or "out of memory" in remark:
                raise TimeoutError(f"server remark: {payload.get('remark')}")
            return payload
        except urllib.error.HTTPError as e:
            last_err = e
            # 429 = rate limited, 5xx = server busy/timeout: back off & rotate.
            # A 400 (bad query) or similar is not retryable -- fail fast.
            if e.code not in (429, 500, 502, 503, 504):
                raise
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as e:
            last_err = e
        if attempt < tries:
            wait = min(base_delay * (2 ** (attempt - 1)), 60)
            nxt = endpoints[attempt % len(endpoints)]
            host = nxt.split("//", 1)[-1].split("/", 1)[0]
            print(f"    {type(last_err).__name__} "
                  f"({getattr(last_err, 'code', '')}); retry {attempt}/{tries - 1} "
                  f"in {wait}s via {host}")
            time.sleep(wait)
    raise last_err if last_err else RuntimeError("Overpass request failed")


# ISO 3166-1 alpha-2 -> English name. Static on purpose: asking Overpass for
# every country boundary on Earth is one heavy global query that reliably 504s
# on busy public servers, and it is the same list every run. The per-country
# hospital fetches below resolve each boundary server-side from its ISO code.
COUNTRIES: dict[str, str] = {
    "AD": "Andorra", "AE": "United Arab Emirates", "AF": "Afghanistan",
    "AG": "Antigua and Barbuda", "AI": "Anguilla", "AL": "Albania",
    "AM": "Armenia", "AO": "Angola", "AR": "Argentina", "AS": "American Samoa",
    "AT": "Austria", "AU": "Australia", "AW": "Aruba", "AX": "Åland Islands",
    "AZ": "Azerbaijan", "BA": "Bosnia and Herzegovina", "BB": "Barbados",
    "BD": "Bangladesh", "BE": "Belgium", "BF": "Burkina Faso", "BG": "Bulgaria",
    "BH": "Bahrain", "BI": "Burundi", "BJ": "Benin", "BL": "Saint Barthélemy",
    "BM": "Bermuda", "BN": "Brunei", "BO": "Bolivia", "BQ": "Caribbean Netherlands",
    "BR": "Brazil", "BS": "Bahamas", "BT": "Bhutan", "BW": "Botswana",
    "BY": "Belarus", "BZ": "Belize", "CA": "Canada", "CD": "DR Congo",
    "CF": "Central African Republic", "CG": "Republic of the Congo",
    "CH": "Switzerland", "CI": "Côte d'Ivoire", "CK": "Cook Islands",
    "CL": "Chile", "CM": "Cameroon", "CN": "China", "CO": "Colombia",
    "CR": "Costa Rica", "CU": "Cuba", "CV": "Cape Verde", "CW": "Curaçao",
    "CY": "Cyprus", "CZ": "Czechia", "DE": "Germany", "DJ": "Djibouti",
    "DK": "Denmark", "DM": "Dominica", "DO": "Dominican Republic", "DZ": "Algeria",
    "EC": "Ecuador", "EE": "Estonia", "EG": "Egypt", "EH": "Western Sahara",
    "ER": "Eritrea", "ES": "Spain", "ET": "Ethiopia", "FI": "Finland",
    "FJ": "Fiji", "FK": "Falkland Islands", "FM": "Micronesia",
    "FO": "Faroe Islands", "FR": "France", "GA": "Gabon", "GB": "United Kingdom",
    "GD": "Grenada", "GE": "Georgia", "GF": "French Guiana", "GG": "Guernsey",
    "GH": "Ghana", "GI": "Gibraltar", "GL": "Greenland", "GM": "Gambia",
    "GN": "Guinea", "GP": "Guadeloupe", "GQ": "Equatorial Guinea", "GR": "Greece",
    "GT": "Guatemala", "GU": "Guam", "GW": "Guinea-Bissau", "GY": "Guyana",
    "HK": "Hong Kong", "HN": "Honduras", "HR": "Croatia", "HT": "Haiti",
    "HU": "Hungary", "ID": "Indonesia", "IE": "Ireland", "IL": "Israel",
    "IM": "Isle of Man", "IN": "India", "IQ": "Iraq", "IR": "Iran",
    "IS": "Iceland", "IT": "Italy", "JE": "Jersey", "JM": "Jamaica",
    "JO": "Jordan", "JP": "Japan", "KE": "Kenya", "KG": "Kyrgyzstan",
    "KH": "Cambodia", "KI": "Kiribati", "KM": "Comoros",
    "KN": "Saint Kitts and Nevis", "KP": "North Korea", "KR": "South Korea",
    "KW": "Kuwait", "KY": "Cayman Islands", "KZ": "Kazakhstan", "LA": "Laos",
    "LB": "Lebanon", "LC": "Saint Lucia", "LI": "Liechtenstein", "LK": "Sri Lanka",
    "LR": "Liberia", "LS": "Lesotho", "LT": "Lithuania", "LU": "Luxembourg",
    "LV": "Latvia", "LY": "Libya", "MA": "Morocco", "MC": "Monaco",
    "MD": "Moldova", "ME": "Montenegro", "MF": "Saint Martin", "MG": "Madagascar",
    "MH": "Marshall Islands", "MK": "North Macedonia", "ML": "Mali",
    "MM": "Myanmar", "MN": "Mongolia", "MO": "Macau", "MP": "Northern Mariana Islands",
    "MQ": "Martinique", "MR": "Mauritania", "MS": "Montserrat", "MT": "Malta",
    "MU": "Mauritius", "MV": "Maldives", "MW": "Malawi", "MX": "Mexico",
    "MY": "Malaysia", "MZ": "Mozambique", "NA": "Namibia", "NC": "New Caledonia",
    "NE": "Niger", "NF": "Norfolk Island", "NG": "Nigeria", "NI": "Nicaragua",
    "NL": "Netherlands", "NO": "Norway", "NP": "Nepal", "NR": "Nauru",
    "NU": "Niue", "NZ": "New Zealand", "OM": "Oman", "PA": "Panama",
    "PE": "Peru", "PF": "French Polynesia", "PG": "Papua New Guinea",
    "PH": "Philippines", "PK": "Pakistan", "PL": "Poland",
    "PM": "Saint Pierre and Miquelon", "PR": "Puerto Rico", "PS": "Palestine",
    "PT": "Portugal", "PW": "Palau", "PY": "Paraguay", "QA": "Qatar",
    "RE": "Réunion", "RO": "Romania", "RS": "Serbia", "RU": "Russia",
    "RW": "Rwanda", "SA": "Saudi Arabia", "SB": "Solomon Islands",
    "SC": "Seychelles", "SD": "Sudan", "SE": "Sweden", "SG": "Singapore",
    "SH": "Saint Helena", "SI": "Slovenia", "SJ": "Svalbard and Jan Mayen",
    "SK": "Slovakia", "SL": "Sierra Leone", "SM": "San Marino", "SN": "Senegal",
    "SO": "Somalia", "SR": "Suriname", "SS": "South Sudan",
    "ST": "São Tomé and Príncipe", "SV": "El Salvador", "SX": "Sint Maarten",
    "SY": "Syria", "SZ": "Eswatini", "TC": "Turks and Caicos Islands",
    "TD": "Chad", "TG": "Togo", "TH": "Thailand", "TJ": "Tajikistan",
    "TL": "Timor-Leste", "TM": "Turkmenistan", "TN": "Tunisia", "TO": "Tonga",
    "TR": "Turkey", "TT": "Trinidad and Tobago", "TV": "Tuvalu", "TW": "Taiwan",
    "TZ": "Tanzania", "UA": "Ukraine", "UG": "Uganda", "US": "United States",
    "UY": "Uruguay", "UZ": "Uzbekistan", "VA": "Vatican City",
    "VC": "Saint Vincent and the Grenadines", "VE": "Venezuela",
    "VG": "British Virgin Islands", "VI": "United States Virgin Islands",
    "VN": "Vietnam", "VU": "Vanuatu", "WF": "Wallis and Futuna", "WS": "Samoa",
    "YE": "Yemen", "YT": "Mayotte", "ZA": "South Africa", "ZM": "Zambia",
    "ZW": "Zimbabwe",
}


def fetch_countries() -> list[tuple[str, str]]:
    """Return [(ISO 3166-1 alpha-2, English country name), ...] (static)."""
    return sorted(COUNTRIES.items())


# Lifecycle tags marking something that used to be a hospital.
FORMER_HOSPITAL_TAGS = ("disused:amenity", "abandoned:amenity", "was:amenity",
                        "historic:amenity", "historic")


def _element_coord(el: dict) -> tuple[float, float] | None:
    lat, lon = el.get("lat"), el.get("lon")
    if lat is None or lon is None:
        center = el.get("center") or {}
        lat, lon = center.get("lat"), center.get("lon")
    if lat is None or lon is None or not _valid_coord(float(lat), float(lon)):
        return None
    return float(lat), float(lon)


def fetch_country_hospitals(endpoints: list[str], iso: str, country_name: str,
                            refresh: bool) -> list[dict]:
    """Hospitals within one country's admin boundary, cached to disk.

    Each cached record carries a "kind": "current" (a hospital today), "renamed"
    (a former name of a current hospital), or "former" (a building that used to
    be a hospital), so --no-history can drop the last two without refetching.
    """
    cache = CACHE_DIR / f"osm{OSM_CACHE_VERSION}_{iso}.json"
    if cache.exists() and not refresh:
        try:
            return json.loads(cache.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            pass  # corrupt cache -> refetch

    former_filters = "".join(f' nwr["{t}"="hospital"](area.a);' for t in FORMER_HOSPITAL_TAGS)
    # Not restricted to admin_level=2: territories with their own ISO code (Hong
    # Kong, Guam, Aruba, the French overseas departments...) are mapped at other
    # levels, and a level-2-only lookup silently finds nothing for them.
    query = (
        "[out:json][timeout:600];"
        f'area["ISO3166-1"="{iso}"]["boundary"="administrative"]->.a;'
        '(nwr["amenity"="hospital"](area.a);'
        ' nwr["healthcare"="hospital"](area.a);'
        f"{former_filters});"
        "out center tags;"
    )
    payload = _overpass(endpoints, query)
    # An empty answer is more often a lagging mirror (one whose area index isn't
    # built yet answers "nothing", with no error) than a country with no
    # hospitals: confirm it on each mirror in turn before believing it.
    if not payload.get("elements"):
        for endpoint in endpoints:
            try:
                payload = _overpass([endpoint], query, tries=2)
            except Exception:  # noqa: BLE001 -- try the next mirror
                continue
            if payload.get("elements"):
                break

    out: list[dict] = []
    for el in payload.get("elements", []):
        tags = el.get("tags", {})
        coord = _element_coord(el)
        if coord is None:
            continue
        lat, lon = coord
        city = (tags.get("addr:city") or tags.get("addr:town")
                or tags.get("addr:village") or "")
        is_current = tags.get("amenity") == "hospital" or tags.get("healthcare") == "hospital"
        name = (tags.get("name") or tags.get("name:en") or "").strip()

        if is_current:
            if not name:
                continue  # unnamed hospital building -> not useful in a picker
            out.append(_record(name, city, country_name, lat, lon) | {"kind": "current"})
            # Earlier names of this same hospital, searchable on their own.
            for key in ("old_name", "old_name:en", "was:name"):
                for old in _split_names(tags.get(key)):
                    if _useful_alias(old, name):
                        out.append(_record(f"{old} (now {name})", city, country_name, lat, lon)
                                   | {"kind": "renamed"})
            continue

        # Not a hospital now, but tagged as having been one.
        if not any(tags.get(t) == "hospital" for t in FORMER_HOSPITAL_TAGS):
            continue
        name = name or (tags.get("was:name") or tags.get("old_name") or "").split(";")[0].strip()
        if not name:
            continue
        closed = _year(tags.get("end_date") or tags.get("was:end_date"))
        suffix = f" (former hospital, closed {closed})" if closed else " (former hospital)"
        out.append(_record(name + suffix, city, country_name, lat, lon) | {"kind": "former"})

    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
    return out


def load_osm(endpoints: list[str], only: list[str] | None, refresh: bool,
             delay: float) -> list[dict]:
    countries = fetch_countries()
    if only:
        wanted = {c.upper() for c in only}
        countries = [(iso, nm) for iso, nm in countries if iso in wanted]
        missing = wanted - {iso for iso, _ in countries}
        if missing:
            print(f"  WARNING: unknown country code(s): {', '.join(sorted(missing))}")
    print(f"  Querying {len(countries)} countries "
          f"(cached under {CACHE_DIR.relative_to(ROOT)}/)")

    out: list[dict] = []
    for i, (iso, name) in enumerate(countries, 1):
        cached = (CACHE_DIR / f"osm{OSM_CACHE_VERSION}_{iso}.json").exists() and not refresh
        try:
            rows = fetch_country_hospitals(endpoints, iso, name, refresh)
        except Exception as e:  # noqa: BLE001 -- one country must not abort all
            print(f"  [{i}/{len(countries)}] {iso} {name}: FAILED ({e}); skipped")
            continue
        out.extend(rows)
        extra = len(rows) - sum(1 for r in rows if r.get("kind", "current") == "current")
        print(f"  [{i}/{len(countries)}] {iso} {name}: {len(rows) - extra:,}"
              f"{f' (+{extra:,} older)' if extra else ''}"
              f"{' (cache)' if cached else ''}")
        if not cached and i < len(countries):
            time.sleep(delay)  # be polite to the public endpoint
    print(f"  OpenStreetMap: {len(out):,} hospitals total")
    return out


# ── older hospitals: Wikidata former names ───────────────────────────────────

WD_ALIASES_QUERY = """
SELECT ?label ?alias ?coord WHERE {
  ?h wdt:P31/wdt:P279* wd:Q16917 ; wdt:P625 ?coord ; skos:altLabel ?alias .
  FILTER(LANG(?alias) = "en")
  ?h rdfs:label ?label . FILTER(LANG(?label) = "en")
}"""

# "Official name" statements that have an end date: definitely former names.
WD_FORMER_NAMES_QUERY = """
SELECT ?label ?old ?coord WHERE {
  ?h wdt:P31/wdt:P279* wd:Q16917 ; wdt:P625 ?coord ; p:P1448 ?st .
  ?st ps:P1448 ?old ; pq:P582 ?end .
  ?h rdfs:label ?label . FILTER(LANG(?label) = "en")
}"""


def _sparql(query: str, tries: int = 5) -> list[dict]:
    url = WDQS_ENDPOINT + "?" + urllib.parse.urlencode({"query": query, "format": "json"})
    headers = {"User-Agent": USER_AGENT, "Accept": "application/sparql-results+json"}
    last_err: Exception | None = None
    for attempt in range(1, tries + 1):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=120) as r:
                return json.load(r)["results"]["bindings"]
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as e:
            last_err = e
            if attempt < tries:
                time.sleep(min(10 * attempt, 60))
    raise last_err if last_err else RuntimeError("Wikidata query failed")


def _wkt_point(value: str) -> tuple[float, float] | None:
    """Wikidata coordinates come as "Point(lon lat)"."""
    m = re.match(r"Point\(([-\d.eE]+) ([-\d.eE]+)\)", value or "")
    if not m:
        return None
    lon, lat = float(m.group(1)), float(m.group(2))
    return (lat, lon) if _valid_coord(lat, lon) else None


def load_wikidata_names(base: list[dict], refresh: bool) -> list[dict]:
    """Former and alternative names of hospitals already in the Wikidata CSV.

    Matched to the CSV by name and position, which keeps only hospitals the CSV
    accepted (so hospital ships and items with no country stay out) and supplies
    their city and country.
    """
    cache = CACHE_DIR / "wikidata_names.json"
    rows: dict | None = None
    if cache.exists() and not refresh:
        try:
            rows = json.loads(cache.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            rows = None
    if rows is None:
        rows = {
            "aliases": [[b["label"]["value"], b["alias"]["value"], b["coord"]["value"]]
                        for b in _sparql(WD_ALIASES_QUERY)],
            "former": [[b["label"]["value"], b["old"]["value"], b["coord"]["value"]]
                       for b in _sparql(WD_FORMER_NAMES_QUERY)],
        }
        CACHE_DIR.mkdir(parents=True, exist_ok=True)
        cache.write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8")

    by_key = {_dedup_key(r["name"], r["lat"], r["lon"]): r for r in base}
    out: list[dict] = []
    for kind, label_fmt in (("former", "{old} (now {name})"), ("aliases", "{old} ({name})")):
        for label, old, coord in rows[kind]:
            point = _wkt_point(coord)
            if point is None or not _useful_alias(old, label):
                continue
            match = by_key.get(_dedup_key(label, *point))
            if match is None:
                continue
            out.append(_record(label_fmt.format(old=old, name=match["name"]),
                               match["city"], match["country"], match["lat"], match["lon"])
                       | {"kind": "renamed"})
    print(f"  Wikidata former/alternative names: {len(out):,}")
    return out


# ── older hospitals: OpenHistoricalMap ───────────────────────────────────────

def load_ohm(refresh: bool) -> list[dict]:
    """Hospitals mapped in OpenHistoricalMap, labelled with their years."""
    cache = CACHE_DIR / "ohm.json"
    if cache.exists() and not refresh:
        try:
            out = json.loads(cache.read_text(encoding="utf-8"))
            print(f"  OpenHistoricalMap: {len(out):,} (cache)")
            return out
        except (OSError, json.JSONDecodeError):
            pass

    payload = _overpass([OHM_ENDPOINT], '[out:json][timeout:600];nwr["amenity"="hospital"];out center tags;')
    out: list[dict] = []
    for el in payload.get("elements", []):
        tags = el.get("tags", {})
        name = (tags.get("name:en") or tags.get("name") or "").strip()
        coord = _element_coord(el)
        if not name or coord is None:
            continue
        start, end = _year(tags.get("start_date")), _year(tags.get("end_date"))
        if end:
            name += f" ({start}–{end})" if start else f" (closed {end})"
        city = tags.get("addr:city") or ""
        out.append(_record(name, city, "", *coord) | {"kind": "former" if end else "current"})

    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps(out, ensure_ascii=False), encoding="utf-8")
    print(f"  OpenHistoricalMap: {len(out):,}")
    return out


# ── nearest town for entries with no city / country ──────────────────────────

def fill_places(records: list[dict]) -> None:
    """Give city-less entries the nearest town in Data/cities.json (within 25 km),
    and country-less ones that town's country (within 50 km)."""
    if not CITIES_IN.exists():
        return
    cities = json.loads(CITIES_IN.read_text(encoding="utf-8-sig"))
    grid: dict[tuple[int, int], list[dict]] = {}
    for c in cities:
        grid.setdefault((math.floor(c["lat"]), math.floor(c["lon"])), []).append(c)

    def nearest(lat: float, lon: float) -> tuple[dict | None, float]:
        best, best_km = None, float("inf")
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                for c in grid.get((math.floor(lat) + dy, math.floor(lon) + dx), ()):
                    # Equirectangular distance: plenty accurate at tens of km.
                    x = math.radians(c["lon"] - lon) * math.cos(math.radians((c["lat"] + lat) / 2))
                    y = math.radians(c["lat"] - lat)
                    km = 6371 * math.hypot(x, y)
                    if km < best_km:
                        best, best_km = c, km
        return best, best_km

    cities_filled = countries_filled = 0
    for r in records:
        if r["city"] and r["country"]:
            continue
        town, km = nearest(r["lat"], r["lon"])
        if town is None:
            continue
        if not r["city"] and km <= 25:
            r["city"] = town["name"]
            cities_filled += 1
        if not r["country"] and km <= 50:
            r["country"] = town["country"]
            countries_filled += 1
    print(f"  Nearest town added to {cities_filled:,} entries; country to {countries_filled:,}")


# ── merge + write ────────────────────────────────────────────────────────────

def main() -> int:
    ap = argparse.ArgumentParser(description="Build Data/hospitals.json.")
    ap.add_argument("--no-osm", action="store_true",
                    help="use only the Wikidata CSV (skip OpenStreetMap)")
    ap.add_argument("--countries", default="",
                    help="comma-separated ISO 3166-1 alpha-2 codes to limit "
                         "the OSM fetch (e.g. US,GB,DE); default is all")
    ap.add_argument("--endpoint", default=None,
                    help="pin a single Overpass endpoint (or set OVERPASS_URL); "
                         "default rotates across public mirrors")
    ap.add_argument("--refresh", action="store_true",
                    help="ignore the OSM cache and refetch")
    ap.add_argument("--no-history", action="store_true",
                    help="leave out former names, former hospitals and "
                         "OpenHistoricalMap (current hospitals only)")
    ap.add_argument("--delay", type=float, default=2.0,
                    help="seconds to wait between country queries (default 2)")
    args = ap.parse_args()

    import os
    override = args.endpoint or os.environ.get("OVERPASS_URL")
    endpoints = [override] if override else DEFAULT_ENDPOINTS
    only = [c.strip() for c in args.countries.split(",") if c.strip()] or None

    print("Building hospitals.json")
    records = load_wikidata()
    wikidata_base = list(records)

    if not args.no_osm:
        try:
            osm = load_osm(endpoints, only, args.refresh, args.delay)
        except Exception as e:  # noqa: BLE001
            print(f"\nERROR: OpenStreetMap fetch failed: {e}")
            print("Wrote nothing. Re-run with --no-osm to build from Wikidata "
                  "only, or check network access to the Overpass endpoint.")
            return 1
        if args.no_history:
            osm = [r for r in osm if r.get("kind", "current") == "current"]
        records += osm

    # Older hospitals from the two extra sources. Either failing (no network,
    # busy server) just leaves its entries out; it never blocks the build.
    if not args.no_history and not args.no_osm:
        for label, fetch in (("Wikidata names", lambda: load_wikidata_names(wikidata_base, args.refresh)),
                             ("OpenHistoricalMap", lambda: load_ohm(args.refresh))):
            try:
                records += fetch()
            except Exception as e:  # noqa: BLE001
                print(f"  {label}: FAILED ({e}); skipped")

    # De-duplicate, keeping the first occurrence (Wikidata precedes OSM, so its
    # curated country label wins when the same hospital appears in both).
    merged: dict[tuple, dict] = {}
    for rec in records:
        key = _dedup_key(rec["name"], rec["lat"], rec["lon"])
        merged.setdefault(key, rec)

    fill_places(list(merged.values()))
    older = sum(1 for r in merged.values() if r.get("kind", "current") != "current")
    hospitals = sorted(({k: v for k, v in r.items() if k != "kind"} for r in merged.values()),
                       key=lambda h: (h["name"].casefold(), h["country"].casefold()))

    with JSON_OUT.open("w", encoding="utf-8") as f:
        json.dump(hospitals, f, ensure_ascii=False, separators=(",", ":"))

    size_mb = JSON_OUT.stat().st_size / (1024 * 1024)
    dropped = len(records) - len(hospitals)
    print(f"\nWrote {len(hospitals):,} hospitals to {JSON_OUT}  ({size_mb:.1f} MB)")
    print(f"Merged from {len(records):,} rows; {dropped:,} duplicates removed; "
          f"{older:,} entries are former names or former hospitals.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
