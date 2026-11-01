"""
Identify every name on the master list on Wikidata, before it goes out for research.

A research group has to say which person each name means: Astro-Databank has returned a
namesake often enough (see scripts/celebrity-expansion-2026-10-07.md). Each name is
searched for. A human labelled with exactly that name comes first, then the most Wikipedia
editions, and a name marked "(director)" takes the human whose description says so. A close
runner-up is marked for checking by hand, and PICKS holds the people settled that way.
Nothing is written to the library or the list: this makes a report.

Usage:
    python -I scripts/wikidata_candidates.py master-list.txt report.json

Progress is kept in report.json.cache beside the report, so an interrupted run resumes.
"""

import json
import re
import sys
import unicodedata
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from wikidata_deaths import ENTITIES_PER_CALL, HUMAN, claim_item, get, gregorian, search, times

SECOND_PLACE_SHARE = 0.5    # a runner-up this close to the best is worth a look

# Read by hand. Name on the list -> the text to search for (None for the name itself), and
# a word the right person's description or occupations contain ("" for the best match).
PICKS = {
    "Alex James": (None, "bass"),
    "Andrew Ng": ("Andrew Y. Ng", ""),
    "Anthony Edwards": (None, "basketball"),
    "C.C. Wei": ("C. C. Wei", ""),
    "David Howell": (None, "chess"),
    "George P. Smith": (None, "chemist"),
    "Jin": ("Kim Seok-jin", ""),
    "John Jumper": (None, "chem"),
    "Katie Mack": (None, "astrophysicist"),
    "Mike Duncan": (None, "podcast"),
    "Sean Carroll": (None, "cosmolog"),
    "Sophia Smith": (None, "soccer"),
    "Steve Smith": (None, "cricket"),
    "Suga": ("Min Yoon-gi", ""),
    "Tom Watson": (None, "golf"),
    "V": ("Kim Tae-hyung", ""),
    "Vince Clarke": (None, "musician"),
}


def folded(name):
    text = unicodedata.normalize("NFKD", re.sub(r"\(.*?\)", "", name))
    text = "".join(c for c in text if not unicodedata.combining(c)).casefold()
    return re.sub(r"[^a-z0-9]", "", text)


def load_entities(ids, entities):
    missing = [i for i in ids if i not in entities]
    for start in range(0, len(missing), ENTITIES_PER_CALL):
        batch = missing[start:start + ENTITIES_PER_CALL]
        found = get({"action": "wbgetentities", "ids": "|".join(batch),
                     "props": "claims|labels|descriptions|sitelinks", "languages": "en|mul"})
        for qid, entity in found.get("entities", {}).items():
            claims = entity.get("claims", {})
            entities[qid] = {
                "label": label_of(entity),
                "description": entity.get("descriptions", {}).get("en", {}).get("value", ""),
                "human": any(claim_item(c) == HUMAN for c in claims.get("P31", [])),
                "born": times(claims.get("P569", [])),
                "died": times(claims.get("P570", [])),
                "occupations": [o for o in map(claim_item, claims.get("P106", [])) if o],
                "citizenship": [o for o in map(claim_item, claims.get("P27", [])) if o],
                "sitelinks": len(entity.get("sitelinks", {})),
            }


def label_of(entity):
    labels = entity.get("labels", {})
    return (labels.get("en") or labels.get("mul") or {}).get("value", "")


def load_labels(qids, labels):
    missing = sorted(q for q in qids if q not in labels)
    for start in range(0, len(missing), ENTITIES_PER_CALL):
        batch = missing[start:start + ENTITIES_PER_CALL]
        found = get({"action": "wbgetentities", "ids": "|".join(batch), "props": "labels", "languages": "en|mul"})
        for qid, entity in found.get("entities", {}).items():
            labels[qid] = label_of(entity)


def resolve(name, ids, entities, labels):
    report = {"name": name}
    hint = re.search(r"\((.*?)\)", name)
    search_text, word = PICKS.get(name, (None, hint.group(1) if hint else ""))
    word = word.lower()
    humans = [(q, entities[q]) for q in ids if q in entities and entities[q]["human"]]
    if word:
        marked = [(q, e) for q, e in humans if word in e["description"].lower() or
                  any(word in labels.get(o, "").lower() for o in e["occupations"])]
        humans = marked or humans
    if not humans:
        return report | {"match": "none"}

    wanted = folded(search_text or name)
    humans.sort(key=lambda x: (folded(x[1]["label"]) != wanted, -x[1]["sitelinks"]))
    qid, entity = humans[0]
    same_name = [e for _, e in humans[1:] if folded(e["label"]) == folded(entity["label"])]
    runner = max((e["sitelinks"] for e in same_name), default=0)
    born = [gregorian(s) for s in entity["born"]]
    died = [gregorian(s) for s in entity["died"]]
    return report | {
        "match": "close" if not word and name not in PICKS and runner >= entity["sitelinks"] * SECOND_PLACE_SHARE else "clear",
        "qid": qid,
        "label": entity["label"],
        "description": entity["description"],
        "born": born[0] if born else "",
        "died": died[0] if died else "",
        "sitelinks": entity["sitelinks"],
        "occupations": [labels.get(o, "") for o in entity["occupations"]],
        "citizenship": [labels.get(o, "") for o in entity["citizenship"]],
        "others": [{"qid": q, "label": e["label"], "description": e["description"], "sitelinks": e["sitelinks"]}
                   for q, e in humans[1:4]],
    }


def main():
    names = [l.strip() for l in Path(sys.argv[1]).read_text(encoding="utf-8").splitlines() if l.strip()]
    out = Path(sys.argv[2])
    cache_path = Path(str(out) + ".cache")
    cache = (json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists()
             else {"found": {}, "entities": {}, "labels": {}})

    for count, name in enumerate(names, 1):
        text = PICKS.get(name, (None, ""))[0]
        if not cache["found"].get(name) or (text and name not in cache.setdefault("picked", {})):
            ids = search(text or name)
            if text:
                cache["picked"][name] = True
            load_entities(ids, cache["entities"])
            cache["found"][name] = ids
            if count % 25 == 0:
                cache_path.write_text(json.dumps(cache), encoding="utf-8")
                print(f"{count}/{len(names)}", flush=True)
    cache_path.write_text(json.dumps(cache), encoding="utf-8")

    # A person labelled only in the "mul" language came back blank from an earlier run.
    blank = [q for q, e in cache["entities"].items() if not e["label"]]
    load_labels(blank, cache["labels"])
    for qid in blank:
        cache["entities"][qid]["label"] = cache["labels"].get(qid, "")

    used = {o for e in cache["entities"].values() for o in e["occupations"] + e["citizenship"]}
    load_labels(used, cache["labels"])
    cache_path.write_text(json.dumps(cache), encoding="utf-8")

    report = [resolve(n, cache["found"][n], cache["entities"], cache["labels"]) for n in names]
    out.write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    print(sum(r["match"] == "clear" for r in report), "clear,",
          sum(r["match"] == "close" for r in report), "close,",
          sum(r["match"] == "none" for r in report), "not found")


if __name__ == "__main__":
    main()
