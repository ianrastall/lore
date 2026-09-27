#!/usr/bin/env python3
"""
Generate Data/hospitals.json (the app's hospital-search database) from a Wikidata
export of hospitals with coordinates.

The Add-Chart dialog uses this the same way it uses cities.json: the chosen
hospital autofills latitude/longitude, and the app resolves the historical,
DST-aware time zone from those coordinates (GeoTimeZone + NodaTime). Hospitals
therefore need no timezone or population column -- only a name, place labels, and
precise coordinates.

Run from the project root:
    python scripts/build-hospitals.py

Source CSV (committed alongside this script for provenance):
    Data/hospitals_wikidata.csv
with columns:
    Hospital,City,Country,Latitude,Longitude

Output schema (matches Models/Hospital.cs):
    {"name","city","country","lat","lon"}

Rows with an empty name or unparseable/out-of-range coordinates are skipped.
Exact duplicate rows are collapsed. Output is sorted by name for a stable diff.

Data (c) Wikidata contributors, released under CC0 1.0 (public domain).
"""
import csv
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSV_IN = ROOT / "Data" / "hospitals_wikidata.csv"
JSON_OUT = ROOT / "Data" / "hospitals.json"


def main() -> int:
    if not CSV_IN.exists():
        sys.exit(f"Source CSV not found: {CSV_IN}\n"
                 "Place the Wikidata hospital export there "
                 "(columns: Hospital,City,Country,Latitude,Longitude).")

    seen: set[tuple] = set()
    hospitals: list[dict] = []
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
            if not (-90.0 <= lat <= 90.0 and -180.0 <= lon <= 180.0):
                skipped += 1
                continue

            city = (row.get("City") or "").strip()
            country = (row.get("Country") or "").strip()
            lat = round(lat, 4)
            lon = round(lon, 4)

            key = (name, city, country, lat, lon)
            if key in seen:
                continue
            seen.add(key)

            hospitals.append({
                "name": name,
                "city": city,
                "country": country,
                "lat": lat,
                "lon": lon,
            })

    # Alphabetical by name -- a stable order for review diffs; the app ranks
    # prefix matches first at search time regardless of file order.
    hospitals.sort(key=lambda h: (h["name"].casefold(), h["country"].casefold()))

    with JSON_OUT.open("w", encoding="utf-8") as f:
        json.dump(hospitals, f, ensure_ascii=False, separators=(",", ":"))

    size_mb = JSON_OUT.stat().st_size / (1024 * 1024)
    print(f"Wrote {len(hospitals):,} hospitals to {JSON_OUT}  ({size_mb:.1f} MB)")
    print(f"Skipped rows (no name / bad coordinates): {skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
