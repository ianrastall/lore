#!/usr/bin/env python3
"""
Generate Data/cities.json (the app's city-search database) from the simplemaps
world-cities CSV, adding the standard UTC offset each city needs -- the CSV has
no timezone data, so we map lat/lon -> IANA timezone -> standard (non-DST) offset.

One-time prerequisite:
    pip install --user timezonefinder

Run from the project root:
    python scripts/build-cities.py

Output schema (matches Models/City.cs):
    {"name","admin","country","lat","lon","utc","pop"}

Data (c) simplemaps.com, "World Cities Database" (Basic), CC BY 4.0.
"""
import csv
import json
import sys
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from timezonefinder import TimezoneFinder

ROOT = Path(__file__).resolve().parent.parent
CSV_IN = ROOT / "Data" / "simplemaps_worldcities_basicv1.91.4" / "worldcities.csv"
JSON_OUT = ROOT / "Data" / "cities.json"

# Reference instants used to derive the *standard* offset. Subtracting the DST
# component makes the result hemisphere-agnostic (Jan is DST in the south).
_JAN = datetime(2023, 1, 1, 12)
_JUL = datetime(2023, 7, 1, 12)

# Cache standard offset per IANA zone name: ~400 unique zones vs. 50k cities.
_offset_cache: dict[str, float] = {}


def standard_offset_hours(tz_name: str) -> float:
    if tz_name in _offset_cache:
        return _offset_cache[tz_name]
    tz = ZoneInfo(tz_name)
    # Standard offset = wall offset minus the DST component, evaluated at a date
    # when DST is inactive. Try January, fall back to July (southern hemisphere).
    result = 0.0
    for dt in (_JAN, _JUL):
        aware = dt.replace(tzinfo=tz)
        dst = aware.dst()
        if dst is not None and dst.total_seconds() == 0:
            result = aware.utcoffset().total_seconds() / 3600.0
            break
    else:
        # No DST-free instant found (unusual) -- use raw January offset.
        result = _JAN.replace(tzinfo=tz).utcoffset().total_seconds() / 3600.0
    result = round(result, 2)
    _offset_cache[tz_name] = result
    return result


def main() -> int:
    if not CSV_IN.exists():
        sys.exit(f"Source CSV not found: {CSV_IN}\n"
                 "Restore the simplemaps folder under Data/ (it is gitignored).")

    tf = TimezoneFinder()
    cities: list[dict] = []
    missing_tz = 0

    with CSV_IN.open(encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            try:
                lat = float(row["lat"])
                lon = float(row["lng"])
            except (KeyError, ValueError):
                continue

            tz_name = tf.timezone_at(lat=lat, lng=lon)
            if tz_name:
                utc = standard_offset_hours(tz_name)
            else:
                # Rare (mid-ocean coords); approximate from longitude.
                utc = round(lon / 15.0)
                missing_tz += 1

            pop_raw = row.get("population") or "0"
            try:
                pop = int(float(pop_raw))
            except ValueError:
                pop = 0

            cities.append({
                "name": row["city"].strip(),
                "admin": (row.get("admin_name") or "").strip(),
                "country": row["country"].strip(),
                "lat": round(lat, 4),
                "lon": round(lon, 4),
                "utc": utc,
                "pop": pop,
            })

    # Largest cities first -- also the natural tie-break for search ranking.
    cities.sort(key=lambda c: c["pop"], reverse=True)

    with JSON_OUT.open("w", encoding="utf-8") as f:
        json.dump(cities, f, ensure_ascii=False, separators=(",", ":"))

    size_mb = JSON_OUT.stat().st_size / (1024 * 1024)
    print(f"Wrote {len(cities):,} cities to {JSON_OUT}  ({size_mb:.1f} MB)")
    print(f"Unique timezones: {len(_offset_cache)}; "
          f"cities with no tz match (longitude fallback): {missing_tz}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
