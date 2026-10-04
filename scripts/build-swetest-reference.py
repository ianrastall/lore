"""Regenerate tests/Lore.Tests/Reference/swetest-reference.json.

Runs swetest (the Swiss Ephemeris's own command-line program, from Astrodienst's
distribution) for a set of moments and places chosen to cover the awkward cases, and
records what it prints. SwetestReferenceTests then checks that Lore, given the same
moment and place, arrives at the same numbers.

Lore and swetest share one engine, so agreement does not prove the astronomy: it proves
that Lore asks the engine the right question (Universal Time, body numbers, flags, house
codes, coordinates the right way round). The astronomy is checked separately, against
Astro-Databank's published positions.

Needs swetest64.exe, which is in the full Swiss Ephemeris download but not in the repo:
    Data/swisseph-2.10.3bfinal/swisseph-2.10.3bfinal/windows/programs/swetest64.exe

    python scripts/build-swetest-reference.py
"""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SWE = ROOT / "Data" / "swisseph-2.10.3bfinal" / "swisseph-2.10.3bfinal"
SWETEST = SWE / "windows" / "programs" / "swetest64.exe"
OUT = ROOT / "tests" / "Lore.Tests" / "Reference" / "swetest-reference.json"

# (name, UT date, UT time to the minute, latitude, longitude, why it is here)
CASES = [
    ("ulm-1879", "1879-03-14", "10:50", 48.4, 9.9833, "an ordinary 19th-century European birth"),
    ("greenwich-2000", "2000-01-01", "12:00", 51.4769, 0.0, "the J2000 epoch, on the prime meridian"),
    ("vinci-1452", "1452-04-23", "20:56", 43.78, 10.92, "the 1200-1800 ephemeris file"),
    ("paris-1250", "1250-06-15", "03:00", 48.85, 2.35, "near the start of the ephemeris range"),
    ("tokyo-2390", "2390-12-31", "23:59", 35.68, 139.69, "near the end of the ephemeris range"),
    ("before-1800", "1799-12-31", "23:59", 40.71, -74.01, "last minute of the 1200-1800 file"),
    ("after-1800", "1800-01-01", "00:00", 40.71, -74.01, "first minute of the 1800-2400 file"),
    ("sydney-1985", "1985-07-04", "06:30", -33.87, 151.21, "southern hemisphere"),
    ("ushuaia-1969", "1969-12-21", "12:00", -54.8, -68.3, "far south, at the solstice"),
    ("quito-1977", "1977-03-21", "17:00", -0.18, -78.47, "on the equator, at the equinox"),
    ("suva-2012", "2012-02-29", "23:30", -18.14, 178.44, "beside the date line, on a leap day"),
    ("reykjavik-1990", "1990-06-21", "12:00", 64.15, -21.94, "high latitude, just outside the Arctic Circle"),
    ("tromso-1990", "1990-06-21", "00:00", 69.65, 18.96, "inside the Arctic Circle: Placidus and Koch fail"),
    ("mcmurdo-2005", "2005-12-21", "06:00", -77.85, 166.67, "inside the Antarctic Circle"),
]

# swetest's name for each body -> the name Lore's tests use.
BODIES = {
    "Sun": "Sun", "Moon": "Moon", "Mercury": "Mercury", "Venus": "Venus", "Mars": "Mars",
    "Jupiter": "Jupiter", "Saturn": "Saturn", "Uranus": "Uranus", "Neptune": "Neptune",
    "Pluto": "Pluto", "mean Node": "MeanNode", "true Node": "TrueNode", "Chiron": "Chiron",
    "mean Apogee": "Lilith",
}
HOUSE_SYSTEMS = {"Placidus": "P", "WholeSign": "W", "Equal": "E", "Koch": "K"}


def swetest(*args):
    result = subprocess.run(
        [str(SWETEST), *args, "-g,", "-head", "-eswe", "-edirephe"],
        cwd=SWE, capture_output=True, text=True, check=False)
    return result.stdout.splitlines()


def main():
    if not SWETEST.exists():
        sys.exit(f"swetest64.exe not found at {SWETEST}")

    cases = []
    for name, date, time, lat, lon, why in CASES:
        y, m, d = date.split("-")
        # Lore reads every date as Gregorian (older births are entered already
        # converted); swetest would otherwise take one before October 1582 as Julian.
        when = [f"-b{int(d)}.{int(m)}.{int(y)}greg", f"-ut{time}:00"]

        bodies = {}
        for line in swetest(*when, "-p0123456789mtDA", "-fPlbsd"):
            cells = [c.strip() for c in line.split(",")]
            if cells[0] in BODIES and len(cells) == 5:
                bodies[BODIES[cells[0]]] = [float(c) for c in cells[1:]]
        if len(bodies) != len(BODIES):
            sys.exit(f"{name}: expected {len(BODIES)} bodies, got {sorted(bodies)}")

        houses = {}
        for system, code in HOUSE_SYSTEMS.items():
            lines = swetest(*when, "-p", "-fPl", f"-house{lon},{lat},{code}")
            values = {}
            for line in lines:
                cells = [c.strip() for c in line.split(",")]
                if len(cells) == 2 and not line.startswith("error"):
                    values[" ".join(cells[0].split())] = float(cells[1])
            houses[system] = {
                "substituted": any("Porphyry calculated instead" in line for line in lines),
                "ascendant": values["Ascendant"],
                "midheaven": values["MC"],
                "cusps": [values[f"house {i}"] for i in range(1, 13)],
            }

        cases.append({
            "name": name, "why": why, "utcDate": date, "utcTime": time,
            "latitude": lat, "longitude": lon, "bodies": bodies, "houses": houses,
        })

    OUT.write_text(json.dumps({
        "source": "swetest64.exe from Swiss Ephemeris 2.10.03, -eswe; regenerate with scripts/build-swetest-reference.py",
        "bodyColumns": ["longitude", "latitude", "speed in longitude per day", "declination"],
        "cases": cases,
    }, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(cases)} cases")


if __name__ == "__main__":
    main()
