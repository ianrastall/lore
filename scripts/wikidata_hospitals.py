"""
Query WikiData SPARQL for hospitals with GPS coordinates, worldwide.

Phase 1: Query WikiData for all sovereign nations.
Phase 2: Query each nation one at a time for hospitals with coordinates.

Outputs CSV in Lore format: Hospital,City,Country,Latitude,Longitude

Usage:
    python wikidata_hospitals.py -o hospitals.csv
    python wikidata_hospitals.py -o hospitals.csv --dedup existing.csv
    python wikidata_hospitals.py -o hospitals.csv --delay 10
    python wikidata_hospitals.py -o hospitals.csv --access-token YOUR_TOKEN
    python wikidata_hospitals.py --resume progress.json -o hospitals.csv

The script saves progress after each country to a JSON checkpoint file,
so you can resume if it's interrupted.
"""

import argparse
import csv
import json
import sys
import time
import urllib.request
import urllib.parse
import urllib.error
from pathlib import Path

SPARQL_ENDPOINT = "https://query.wikidata.org/sparql"

# -------------------------------------------------------------------
# Phase 1 query: get all sovereign nations from WikiData
# -------------------------------------------------------------------
NATIONS_QUERY = """
SELECT DISTINCT ?country ?countryLabel WHERE {
  ?country wdt:P31 wd:Q3624078 .
  FILTER NOT EXISTS { ?country wdt:P31 wd:Q3024240 }
  SERVICE wikibase:label { bd:serviceParam wikibase:language "en" . }
}
ORDER BY ?countryLabel
"""

# -------------------------------------------------------------------
# Phase 2 query: hospitals in one country
# -------------------------------------------------------------------
HOSPITALS_QUERY = """
SELECT ?hospital ?hospitalLabel ?cityLabel ?lat ?lon WHERE {{
  ?hospital wdt:P31/wdt:P279* wd:Q16917 ;
            wdt:P17 wd:{country_qid} ;
            wdt:P625 ?coord .
  BIND(geof:latitude(?coord) AS ?lat)
  BIND(geof:longitude(?coord) AS ?lon)
  OPTIONAL {{
    ?hospital wdt:P131 ?city .
    ?city wdt:P31/wdt:P279* wd:Q515 .
  }}
  SERVICE wikibase:label {{ bd:serviceParam wikibase:language "en" . }}
}}
ORDER BY ?hospitalLabel
"""

# CSV country-name overrides (WikiData labels → shorter/conventional names)
COUNTRY_LABEL_OVERRIDES = {
    "United States of America": "USA",
    "United Kingdom of Great Britain and Northern Ireland": "UK",
    "People's Republic of China": "China",
    "Republic of China": "Taiwan",
    "Republic of Korea": "South Korea",
    "Democratic People's Republic of Korea": "North Korea",
    "Russian Federation": "Russia",
    "Kingdom of the Netherlands": "Netherlands",
    "Kingdom of Denmark": "Denmark",
    "Kingdom of Norway": "Norway",
    "Kingdom of Sweden": "Sweden",
    "Kingdom of Spain": "Spain",
    "Kingdom of Belgium": "Belgium",
    "Swiss Confederation": "Switzerland",
    "Republic of Ireland": "Ireland",
    "Czech Republic": "Czech Republic",
    "Republic of India": "India",
    "Federative Republic of Brazil": "Brazil",
    "United Mexican States": "Mexico",
    "Republic of South Africa": "South Africa",
    "Commonwealth of Australia": "Australia",
    "New Zealand": "New Zealand",
    "State of Japan": "Japan",
    "Republic of the Philippines": "Philippines",
    "Socialist Republic of Vietnam": "Vietnam",
    "Republic of Indonesia": "Indonesia",
    "Kingdom of Thailand": "Thailand",
    "Republic of Turkey": "Turkey",
    "Islamic Republic of Iran": "Iran",
    "Arab Republic of Egypt": "Egypt",
    "Republic of Colombia": "Colombia",
    "Argentine Republic": "Argentina",
    "Republic of Chile": "Chile",
    "Republic of Peru": "Peru",
    "Federal Republic of Germany": "Germany",
    "French Republic": "France",
    "Italian Republic": "Italy",
    "Portuguese Republic": "Portugal",
    "Hellenic Republic": "Greece",
    "Republic of Austria": "Austria",
    "Republic of Finland": "Finland",
    "Republic of Poland": "Poland",
    "Romania": "Romania",
    "Republic of Serbia": "Serbia",
    "Republic of Croatia": "Croatia",
    "Hungary": "Hungary",
    "Ukraine": "Ukraine",
    "Republic of Kenya": "Kenya",
    "Federal Republic of Nigeria": "Nigeria",
    "Islamic Republic of Pakistan": "Pakistan",
    "People's Republic of Bangladesh": "Bangladesh",
    "State of Israel": "Israel",
    "Kingdom of Saudi Arabia": "Saudi Arabia",
    "United Arab Emirates": "UAE",
    "Republic of Singapore": "Singapore",
    "Malaysia": "Malaysia",
}


def log(msg: str):
    """Print a timestamped log line to stderr."""
    ts = time.strftime("%H:%M:%S")
    print(f"[{ts}] {msg}", file=sys.stderr, flush=True)


def query_sparql(sparql: str, access_token: str = None,
                 retries: int = 3, base_wait: int = 15) -> list[dict]:
    """Send a SPARQL query and return parsed JSON results."""
    params = urllib.parse.urlencode({"query": sparql, "format": "json"})
    url = f"{SPARQL_ENDPOINT}?{params}"
    headers = {
        "Accept": "application/sparql-results+json",
        "User-Agent": "LoreHospitalBot/1.0 (astrology birth-location database; "
                      "contact: lore-project)",
    }
    if access_token:
        headers["Authorization"] = f"Bearer {access_token}"

    req = urllib.request.Request(url, headers=headers)

    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=180) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                return data["results"]["bindings"]
        except urllib.error.HTTPError as e:
            if e.code == 429:
                wait = 60 * (attempt + 1)  # Back off aggressively on rate limit
                log(f"  Rate limited (429). Waiting {wait}s before retry "
                    f"{attempt+1}/{retries}...")
                time.sleep(wait)
            elif e.code >= 500:
                wait = base_wait * (attempt + 1)
                log(f"  Server error ({e.code}). Retrying in {wait}s "
                    f"({attempt+1}/{retries})...")
                time.sleep(wait)
            elif e.code == 403:
                log(f"  HTTP 403 Forbidden. If persistent, check your token.")
                wait = base_wait * (attempt + 1)
                time.sleep(wait)
            else:
                log(f"  HTTP {e.code}: {e.reason}")
                raise
        except urllib.error.URLError as e:
            wait = base_wait * (attempt + 1)
            log(f"  Connection error: {e.reason}. Retrying in {wait}s "
                f"({attempt+1}/{retries})...")
            time.sleep(wait)
        except TimeoutError:
            wait = base_wait * (attempt + 1)
            log(f"  Timeout. Retrying in {wait}s ({attempt+1}/{retries})...")
            time.sleep(wait)

    log("  Failed after all retries. Skipping this query.")
    return []


# -------------------------------------------------------------------
# Phase 1: Fetch all nations
# -------------------------------------------------------------------
def fetch_nations(access_token: str = None) -> list[dict]:
    """Return list of {"qid": "Q30", "label": "USA"} for all nations."""
    log("Phase 1: Fetching list of all sovereign nations from WikiData...")
    results = query_sparql(NATIONS_QUERY, access_token=access_token)

    nations = []
    for r in results:
        uri = r.get("country", {}).get("value", "")
        label = r.get("countryLabel", {}).get("value", "").strip()
        if not uri or not label:
            continue
        qid = uri.rsplit("/", 1)[-1]  # "http://www.wikidata.org/entity/Q30" → "Q30"

        # Skip if label is just a Q-ID (no English label)
        if label.startswith("Q") and label[1:].isdigit():
            continue

        # Apply short-name overrides
        csv_label = COUNTRY_LABEL_OVERRIDES.get(label, label)

        nations.append({"qid": qid, "label": csv_label, "wikidata_label": label})

    log(f"  Found {len(nations)} nations.")
    return nations


# -------------------------------------------------------------------
# Phase 2: Fetch hospitals for one nation
# -------------------------------------------------------------------
def fetch_hospitals(nation: dict, access_token: str = None) -> list[dict]:
    """Fetch hospitals with coordinates for one nation. Returns CSV row dicts."""
    qid = nation["qid"]
    label = nation["label"]

    sparql = HOSPITALS_QUERY.format(country_qid=qid)
    results = query_sparql(sparql, access_token=access_token)

    rows = []
    seen = set()
    for r in results:
        name = r.get("hospitalLabel", {}).get("value", "").strip()
        city = r.get("cityLabel", {}).get("value", "").strip()
        lat = r.get("lat", {}).get("value", "")
        lon = r.get("lon", {}).get("value", "")

        if not name or not lat or not lon:
            continue
        # Skip Q-ID-only labels
        if name.startswith("Q") and name[1:].isdigit():
            continue
        if city.startswith("Q") and city[1:].isdigit():
            city = ""

        try:
            lat_f = round(float(lat), 4)
            lon_f = round(float(lon), 4)
        except ValueError:
            continue

        # Deduplicate within this country
        key = (name.lower(), city.lower())
        if key in seen:
            continue
        seen.add(key)

        rows.append({
            "Hospital": name,
            "City": city,
            "Country": label,
            "Latitude": f"{lat_f:.4f}",
            "Longitude": f"{lon_f:.4f}",
        })

    return rows


# -------------------------------------------------------------------
# Progress checkpoint
# -------------------------------------------------------------------
def save_progress(progress_path: str, completed: list[str],
                  all_rows: list[dict]):
    """Save checkpoint: which countries are done and all rows so far."""
    with open(progress_path, "w", encoding="utf-8") as f:
        json.dump({
            "completed_countries": completed,
            "rows": all_rows,
        }, f, ensure_ascii=False)


def load_progress(progress_path: str) -> tuple[list[str], list[dict]]:
    """Load checkpoint. Returns (completed_country_qids, rows)."""
    with open(progress_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    return data["completed_countries"], data["rows"]


# -------------------------------------------------------------------
# Dedup against existing CSV
# -------------------------------------------------------------------
def load_existing(path: str) -> set:
    """Load existing CSV, return set of (hospital, city, country) lowercase keys."""
    existing = set()
    with open(path, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            key = (
                row["Hospital"].strip().lower(),
                row["City"].strip().lower(),
                row["Country"].strip().lower(),
            )
            existing.add(key)
    log(f"Loaded {len(existing)} existing entries from {path}")
    return existing


# -------------------------------------------------------------------
# Write final CSV
# -------------------------------------------------------------------
def write_csv(rows: list[dict], path: str, dedup_keys: set = None):
    """Write rows to a CSV file, optionally deduplicating."""
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "Hospital", "City", "Country", "Latitude", "Longitude"
        ])
        writer.writeheader()

        written = 0
        skipped = 0
        for row in rows:
            key = (
                row["Hospital"].lower(),
                row["City"].lower(),
                row["Country"].lower(),
            )
            if dedup_keys and key in dedup_keys:
                skipped += 1
                continue
            writer.writerow(row)
            written += 1

    log(f"Wrote {written} hospitals to {path}"
        + (f" (skipped {skipped} duplicates)" if skipped else ""))


# -------------------------------------------------------------------
# Main
# -------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(
        description="Query WikiData for hospital GPS coordinates, worldwide.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python wikidata_hospitals.py -o hospitals.csv
  python wikidata_hospitals.py -o hospitals.csv --delay 10 --access-token TOKEN
  python wikidata_hospitals.py -o hospitals.csv --dedup Lore_1.2_hospitals_final.csv
  python wikidata_hospitals.py --resume progress.json -o hospitals.csv
        """,
    )
    parser.add_argument(
        "-o", "--output", required=True, metavar="FILE",
        help="Output CSV file path.",
    )
    parser.add_argument(
        "-d", "--dedup", metavar="CSV",
        help="Existing CSV to deduplicate against.",
    )
    parser.add_argument(
        "--delay", type=float, default=5.0,
        help="Seconds to wait between country queries (default: 5).",
    )
    parser.add_argument(
        "--access-token", metavar="TOKEN",
        help="WikiData OAuth 2.0 access token for higher rate limits.",
    )
    parser.add_argument(
        "--checkpoint", metavar="FILE", default="wikidata_progress.json",
        help="Checkpoint file for resume (default: wikidata_progress.json).",
    )
    parser.add_argument(
        "--resume", metavar="FILE",
        help="Resume from a previous checkpoint file.",
    )
    args = parser.parse_args()

    access_token = args.access_token

    # Resume or start fresh
    if args.resume:
        log(f"Resuming from checkpoint: {args.resume}")
        completed_qids, all_rows = load_progress(args.resume)
        log(f"  {len(completed_qids)} countries already done, "
            f"{len(all_rows)} hospitals collected.")
        checkpoint_path = args.resume
    else:
        completed_qids = []
        all_rows = []
        checkpoint_path = args.checkpoint

    # Phase 1: Get list of nations
    nations = fetch_nations(access_token=access_token)
    if not nations:
        log("ERROR: Could not fetch nation list. Check your connection.")
        sys.exit(1)

    # Filter out already-completed nations
    remaining = [n for n in nations if n["qid"] not in completed_qids]
    total = len(nations)
    done_count = len(completed_qids)

    log(f"Phase 2: Querying hospitals for {len(remaining)} remaining nations "
        f"(of {total} total). Delay between queries: {args.delay}s")
    log("")

    # Phase 2: Query each nation, one at a time
    for i, nation in enumerate(remaining):
        progress = f"[{done_count + i + 1}/{total}]"
        log(f"{progress} {nation['label']} (wd:{nation['qid']})...")

        rows = fetch_hospitals(nation, access_token=access_token)
        log(f"  → {len(rows)} hospitals")

        all_rows.extend(rows)
        completed_qids.append(nation["qid"])

        # Save checkpoint after every country
        save_progress(checkpoint_path, completed_qids, all_rows)

        # Be polite — wait before the next query
        if i < len(remaining) - 1:
            time.sleep(args.delay)

    # Sort all results by Country, City, Hospital
    all_rows.sort(key=lambda r: (r["Country"], r["City"], r["Hospital"]))

    # Load dedup set if requested
    dedup_keys = None
    if args.dedup:
        dedup_keys = load_existing(args.dedup)

    # Write final CSV
    write_csv(all_rows, args.output, dedup_keys)

    # Summary
    countries_with_data = len(set(r["Country"] for r in all_rows))
    log("")
    log(f"Done. {len(all_rows)} hospitals across {countries_with_data} countries.")
    log(f"Output: {args.output}")
    log(f"Checkpoint: {checkpoint_path}")

    # Clean up checkpoint on success
    cp = Path(checkpoint_path)
    if cp.exists():
        log(f"(Checkpoint kept at {checkpoint_path} — "
            f"delete it manually if you're satisfied with the output.)")


if __name__ == "__main__":
    main()