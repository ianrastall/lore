"""
Find the death date of every figure in content/celebrities.json on Wikidata.

Each name is searched for, and a result counts only if it is a human whose date of
birth on Wikidata is the figure's. Anyone else is listed for checking by hand. Nothing
is written to the library: this makes a report, and applying it is a separate step
once the "check" entries have been read.

Usage:
    python -I scripts/wikidata_deaths.py content/celebrities.json report.json

Progress is kept in report.json.cache beside the report, so an interrupted run resumes.
"""

import json
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

API = "https://www.wikidata.org/w/api.php"
USER_AGENT = "LoreFigureLibrary/1.0 (https://github.com/ianrastall/lore; death dates of public figures)"
HUMAN = "Q5"
JULIAN_MODEL = "Q1985786"
DAY_PRECISION = 11
SEARCH_RESULTS = 8
ENTITIES_PER_CALL = 50
PAUSE_SECONDS = 0.15


def get(params):
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json", "maxlag": "5"})
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = json.load(response)
            if data.get("error", {}).get("code") == "maxlag":
                time.sleep(10)
                continue
            time.sleep(PAUSE_SECONDS)
            return data
        except (urllib.error.URLError, TimeoutError) as error:
            if attempt == 5:
                raise
            print(f"retrying after {error}", file=sys.stderr)
            time.sleep(10 * (attempt + 1))
    raise RuntimeError("Wikidata kept reporting lag")


def search_terms(name):
    """The name as given, and without a bracketed or quoted part or a Jr or Sr."""
    terms = [name]
    plain = re.sub(r"\(.*?\)|\".*?\"", " ", name)
    plain = re.sub(r",?\s+(Jr\.?|Sr\.?|III|II)$", "", plain).strip()
    plain = re.sub(r"\s+", " ", plain)
    if plain and plain != name:
        terms.append(plain)
    return terms


def search(name):
    ids = []
    for term in search_terms(name):
        found = get({"action": "wbsearchentities", "search": term, "language": "en",
                     "uselang": "en", "type": "item", "limit": SEARCH_RESULTS})
        ids += [hit["id"] for hit in found.get("search", []) if hit["id"] not in ids]
    return ids


def load_entities(ids, entities):
    missing = [i for i in ids if i not in entities]
    for start in range(0, len(missing), ENTITIES_PER_CALL):
        batch = missing[start:start + ENTITIES_PER_CALL]
        found = get({"action": "wbgetentities", "ids": "|".join(batch),
                     "props": "claims|labels|sitelinks", "languages": "en"})
        for qid, entity in found.get("entities", {}).items():
            claims = entity.get("claims", {})
            entities[qid] = {
                "label": entity.get("labels", {}).get("en", {}).get("value", ""),
                "human": any(claim_item(c) == HUMAN for c in claims.get("P31", [])),
                "born": times(claims.get("P569", [])),
                "died": times(claims.get("P570", [])),
                "noValueDied": any(c["mainsnak"]["snaktype"] != "value" for c in claims.get("P570", [])),
                "sitelinks": len(entity.get("sitelinks", {})),
            }


def claim_item(claim):
    value = claim["mainsnak"].get("datavalue", {}).get("value", {})
    return value.get("id") if isinstance(value, dict) else None


def times(claims):
    """Date statements as (y-m-d text as stated, precision, calendar is Julian, rank)."""
    out = []
    for claim in claims:
        if claim["mainsnak"]["snaktype"] != "value" or claim.get("rank") == "deprecated":
            continue
        value = claim["mainsnak"]["datavalue"]["value"]
        out.append({
            "date": value["time"].lstrip("+")[:10],
            "precision": value["precision"],
            "julian": value["calendarmodel"].endswith(JULIAN_MODEL),
            "rank": claim.get("rank"),
        })
    return out


def julian_to_gregorian(text):
    year, month, day = map(int, text.split("-"))
    # Julian day number of a Julian calendar date, then back out as Gregorian.
    a = (14 - month) // 12
    y = year + 4800 - a
    m = month + 12 * a - 3
    jdn = day + (153 * m + 2) // 5 + 365 * y + y // 4 - 32083
    a = jdn + 32044
    b = (4 * a + 3) // 146097
    c = a - 146097 * b // 4
    d = (4 * c + 3) // 1461
    e = c - 1461 * d // 4
    m = (5 * e + 2) // 153
    return (f"{100 * b + d - 4800 + m // 10:04d}-{m + 3 - 12 * (m // 10):02d}-"
            f"{e - (153 * m + 2) // 5 + 1:02d}")


def gregorian(stated):
    if stated["julian"] and stated["precision"] >= DAY_PRECISION:
        return julian_to_gregorian(stated["date"])
    return stated["date"]


def folded(name):
    text = unicodedata.normalize("NFKD", name)
    text = "".join(c for c in text if not unicodedata.combining(c)).lower()
    return re.sub(r"[^a-z0-9 ]", "", text)


def born_on(entity, date):
    return any(s["precision"] >= DAY_PRECISION and date in (s["date"], gregorian(s)) for s in entity["born"])


def born_in(entity, year):
    return any(s["date"][:4] == year for s in entity["born"])


def resolve(figure, ids, entities):
    humans = [(qid, entities[qid]) for qid in ids if qid in entities and entities[qid]["human"]]
    exact = [(qid, e) for qid, e in humans if born_on(e, figure["birthDate"])]
    report = {"id": figure["id"], "name": figure["name"], "born": figure["birthDate"], "bio": figure["bio"][:120]}

    if not exact:
        year = figure["birthDate"][:4]
        near = [{"qid": qid, "label": e["label"], "born": [s["date"] for s in e["born"]],
                 "died": e["died"], "sitelinks": e["sitelinks"]}
                for qid, e in humans if born_in(e, year)]
        near += [{"qid": qid, "label": e["label"], "born": [s["date"] for s in e["born"]],
                  "died": e["died"], "sitelinks": e["sitelinks"]}
                 for qid, e in humans if folded(e["label"]) == folded(figure["name"]) and not born_in(e, year)]
        return {**report, "match": "none", "near": near}

    qid, entity = max(exact, key=lambda x: x[1]["sitelinks"])
    report |= {"qid": qid, "label": entity["label"], "sitelinks": entity["sitelinks"],
               "candidates": len(exact)}
    if len(exact) > 1:
        report["others"] = [{"qid": q, "label": e["label"], "died": e["died"]} for q, e in exact if q != qid]

    stated = entity["died"]
    preferred = [s for s in stated if s["rank"] == "preferred"] or stated
    days = sorted({gregorian(s) for s in preferred if s["precision"] >= DAY_PRECISION})
    report["match"] = "exact"
    report["death"] = days[0] if len(days) == 1 else None
    report["statements"] = stated
    report["unknownValue"] = entity["noValueDied"]
    return report


def main():
    library, out = Path(sys.argv[1]), Path(sys.argv[2])
    figures = json.loads(library.read_text(encoding="utf-8"))
    cache_path = Path(str(out) + ".cache")
    cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {"found": {}, "entities": {}}

    for count, figure in enumerate(figures, 1):
        if figure["id"] not in cache["found"]:
            ids = search(figure["name"])
            load_entities(ids, cache["entities"])
            cache["found"][figure["id"]] = ids
            if count % 25 == 0:
                cache_path.write_text(json.dumps(cache), encoding="utf-8")
                print(f"{count}/{len(figures)}", flush=True)
    cache_path.write_text(json.dumps(cache), encoding="utf-8")

    report = [resolve(f, cache["found"][f["id"]], cache["entities"]) for f in figures]
    out.write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    print(sum(r["match"] == "exact" for r in report), "matched,",
          sum(r["match"] == "none" for r in report), "to check")


if __name__ == "__main__":
    main()
