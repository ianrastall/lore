"""
Gather the fuller record of each figure from Wikidata, by Q-number (next-steps 4.6b).

Reads wikidataId from content/celebrities.json, so nothing is searched for, and writes
two files: figures.json, one record for each figure, and connections.json, the links
between two people who are both in the library. Pictures are not fetched: a portrait
is a file name on Wikimedia Commons, each with its own licence.

Usage:
    python -I scripts/wikidata_records.py content/celebrities.json content/records cache.json

The cache holds what Wikidata returned, so a later run can change what is kept without
fetching anything again. Delete it to refresh.
"""

import datetime as dt
import gzip
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
USER_AGENT = "LoreFigureLibrary/1.0 (https://github.com/ianrastall/lore; records of public figures)"
JULIAN_MODEL = "Q1985786"
FIGURES_PER_CALL = 25    # these items are large
ITEMS_PER_CALL = 50
PAUSE_SECONDS = 0.2

BIRTH_NAME, PSEUDONYM, NICKNAME = "P1477", "P742", "P1449"
OCCUPATION, IMAGE, AWARD, MEMBER_OF, HAS_PART = "P106", "P18", "P166", "P463", "P527"
SPOUSE, PARTNER, FATHER, MOTHER, CHILD, SIBLING, RELATIVE = "P26", "P451", "P22", "P25", "P40", "P3373", "P1038"
INSTANCE_OF, BORN, DIED = "P31", "P569", "P570"
POINT_IN_TIME, START, END, END_CAUSE, KINSHIP = "P585", "P580", "P582", "P1534", "P1039"
DIVORCE = "Q93190"

FIGURE_PROPERTIES = [BIRTH_NAME, PSEUDONYM, NICKNAME, OCCUPATION, IMAGE, AWARD, MEMBER_OF, SPOUSE, PARTNER,
                     FATHER, MOTHER, CHILD, SIBLING, RELATIVE]
ITEM_PROPERTIES = [INSTANCE_OF, BORN, DIED, HAS_PART]
QUALIFIERS = [POINT_IN_TIME, START, END, END_CAUSE, KINSHIP]

GROUP_KIND = re.compile(r"\b(band|musical group|musical duo|duo|trio|quartet|quintet|girl group|boy band|"
                        r"vocal group|supergroup|comedy troupe|comedy group)\b", re.I)

# An award counts as major if this many Wikipedias and sister projects have a page on it.
MAJOR_AWARD_SITELINKS = 20

# Stated as awards on Wikidata, but a title, or a kind of thing rather than one award.
NOT_AWARDS = {"Duke of Cornwall", "Duke of York", "Prince of Wales", "honorary citizenship",
              "honorary degree", "honorary doctorate"}

# Stated as groups: two one-off charity recordings, and an item with no page anywhere
# that no source bears out.
NOT_GROUPS = {"USA for Africa", "Artists Stand Up to Cancer", "Salvador Dalí & Louise Bourgeois"}

# Vandalism among the aliases.
NOT_NAMES = {"Britney Bitch", "Bruce SpBruce Frederick Joseph Springsteenringsteen with The Sessions Band"}

# A child born after the father's death, in days.
LONGEST_PREGNANCY = 310


def get(params):
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json", "maxlag": "5"})
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                body = response.read()
                if response.headers.get("Content-Encoding") == "gzip":
                    body = gzip.decompress(body)
            data = json.loads(body)
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


def value_of(snak):
    """A snak's value, reduced to text, an item id or a dated tuple."""
    if snak.get("snaktype") != "value":
        return None
    kind, value = snak["datavalue"]["type"], snak["datavalue"]["value"]
    if kind == "wikibase-entityid":
        return value["id"]
    if kind == "time":
        return {"date": value["time"].lstrip("+")[:10], "precision": value["precision"],
                "julian": value["calendarmodel"].endswith(JULIAN_MODEL)}
    if kind == "monolingualtext":
        return value["text"]
    return value


def statements(claims, properties):
    out = {}
    for prop in properties:
        kept = []
        for claim in claims.get(prop, []):
            if claim.get("rank") == "deprecated" or (value := value_of(claim["mainsnak"])) is None:
                continue
            qualifiers = {q: [v for s in claim.get("qualifiers", {}).get(q, []) if (v := value_of(s)) is not None]
                          for q in QUALIFIERS if q in claim.get("qualifiers", {})}
            kept.append({"value": value, "rank": claim.get("rank"), "q": qualifiers})
        if kept:
            out[prop] = kept
    return out


def label_of(entity):
    labels = entity.get("labels", {})
    return (labels.get("en") or labels.get("mul") or {}).get("value", "")


def fetch(ids, per_call, properties, store, props):
    missing = [i for i in ids if i not in store]
    for start in range(0, len(missing), per_call):
        batch = missing[start:start + per_call]
        found = get({"action": "wbgetentities", "ids": "|".join(batch), "props": props, "languages": "en|mul"})
        for qid, entity in found.get("entities", {}).items():
            aliases = entity.get("aliases", {})
            store[qid] = {
                "label": label_of(entity),
                "aliases": sorted({a["value"] for lang in ("en", "mul") for a in aliases.get(lang, [])}),
                "sitelinks": len(entity.get("sitelinks", {})),
                "wikipedias": sum(site.endswith("wiki") and site != "commonswiki" for site in entity.get("sitelinks", {})),
                "claims": statements(entity.get("claims", {}), properties),
            }
        print(f"{start + len(batch)}/{len(missing)}", flush=True)


def referenced(people):
    ids = set()
    for person in people:
        for prop in (OCCUPATION, AWARD, MEMBER_OF, SPOUSE, PARTNER, FATHER, MOTHER, CHILD, SIBLING, RELATIVE):
            for statement in person["claims"].get(prop, []):
                ids.add(statement["value"])
                ids.update(statement["q"].get(END_CAUSE, []))
                ids.update(statement["q"].get(KINSHIP, []))
    return sorted(ids)


def julian_to_gregorian(text):
    year, month, day = map(int, text.split("-"))
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


def date_text(stated):
    """'1967-05-01', '1967-05' or '1967' by what is known; None for anything vaguer."""
    if stated is None or stated["precision"] < 9 or stated["date"].startswith("-"):
        return None
    if stated["precision"] == 9:
        return stated["date"][:4]
    if stated["precision"] == 10:
        return stated["date"][:7]
    return julian_to_gregorian(stated["date"]) if stated["julian"] else stated["date"]


def first_date(statement, qualifier):
    values = statement["q"].get(qualifier, [])
    return date_text(values[0]) if values else None


def best(statements_of):
    """The statements Wikidata prefers, or all of them if it prefers none."""
    return [s for s in statements_of if s["rank"] == "preferred"] or statements_of


def compare_lifetime(date, born, died):
    """Negative if the date is before the life, positive if after, 0 within it."""
    length = len(date)
    if date < born[:length]:
        return -1
    if died and date > died[:length]:
        return 1
    return 0


def born_to_the_dead(date, died):
    """A full date within a pregnancy of a death."""
    if not died or len(date) != 10:
        return False
    return 0 < (dt.date.fromisoformat(date) - dt.date.fromisoformat(died)).days <= LONGEST_PREGNANCY


def in_latin_script(text):
    return all("LATIN" in unicodedata.name(c, "") for c in text if c.isalpha())


class Records:
    def __init__(self, figures, people, items):
        self.figures, self.people, self.items = figures, people, items
        self.by_qid = {f["wikidataId"]: f for f in figures}
        self.problems = []

    def public_name(self, qid):
        """A person's name, if they are public enough to have a page of their own."""
        if qid in self.by_qid:
            return self.by_qid[qid]["name"]
        item = self.items.get(qid, {})
        return item.get("label") if item.get("wikipedias") else None

    def names(self, figure, person):
        claims = person["claims"]
        stated = [s["value"] for s in best(claims.get(BIRTH_NAME, []))]
        birth = next((n for n in stated if in_latin_script(n)), None)
        other = {s["value"] for prop in (PSEUDONYM, NICKNAME) for s in claims.get(prop, [])} | set(person["aliases"]) | set(stated)
        skip = {figure["name"].casefold(), (birth or "").casefold()}
        return birth, sorted(n for n in other - NOT_NAMES if n.casefold() not in skip)

    def occupations(self, person):
        return sorted({label for s in person["claims"].get(OCCUPATION, [])
                       if (label := self.items.get(s["value"], {}).get("label"))})

    def portrait(self, person):
        return next((s["value"] for s in best(person["claims"].get(IMAGE, []))), None)

    def event(self, figure, out, kind, date, **fields):
        verdict = compare_lifetime(date, figure["birthDate"], figure.get("deathDate"))
        posthumous = kind == "award" or (kind == "child" and born_to_the_dead(date, figure.get("deathDate")))
        if verdict < 0 or (verdict > 0 and not posthumous):
            self.problems.append(f"{figure['id']}: {kind} on {date} is outside the lifetime "
                                 f"{figure['birthDate']} to {figure.get('deathDate', '')}: {fields}")
            return
        out.append({"kind": kind, "date": date, **{k: v for k, v in fields.items() if v}})

    def marriage_events(self, figure, person, out, stats):
        for statement in person["claims"].get(SPOUSE, []):
            name = self.public_name(statement["value"])
            if (started := first_date(statement, START)) is not None:
                self.event(figure, out, "marriage", started, person=name)
            else:
                stats["marriages without a date"] += 1
            if (ended := first_date(statement, END)) is None:
                continue
            if DIVORCE in statement["q"].get(END_CAUSE, []):
                self.event(figure, out, "divorce", ended, person=name)
                continue
            spouse_died = self.spouse_death(statement["value"])
            if figure.get("deathDate", "")[:len(ended)] != ended and (spouse_died or "")[:len(ended)] != ended:
                self.event(figure, out, "marriageEnded", ended, person=name)

    def spouse_death(self, qid):
        if qid in self.by_qid:
            return self.by_qid[qid].get("deathDate")
        died = self.items.get(qid, {}).get("claims", {}).get(DIED, [])
        return date_text(died[0]["value"]) if died else None

    def child_events(self, figure, person, out, stats):
        for statement in person["claims"].get(CHILD, []):
            qid = statement["value"]
            name = self.public_name(qid)
            if name is None:
                stats["children left out, having no page of their own"] += 1
                continue
            if qid in self.by_qid:
                born = self.by_qid[qid]["birthDate"]
            else:
                dates = [date_text(s["value"]) for s in best(self.items.get(qid, {}).get("claims", {}).get(BORN, []))]
                born = next((d for d in dates if d), None)
            if born:
                self.event(figure, out, "child", born, person=name)
            else:
                stats["children without a date"] += 1

    def award_events(self, figure, person, out, stats):
        for statement in person["claims"].get(AWARD, []):
            item = self.items.get(statement["value"], {})
            date = first_date(statement, POINT_IN_TIME) or first_date(statement, START)
            if item.get("label") in NOT_AWARDS:
                continue
            if item.get("sitelinks", 0) < MAJOR_AWARD_SITELINKS:
                stats["minor awards left out"] += 1
            elif date is None:
                stats["major awards without a date"] += 1
            else:
                self.event(figure, out, "award", date, award=item.get("label"))

    def record(self, figure, stats):
        person = self.people[figure["wikidataId"]]
        birth, other = self.names(figure, person)
        events = []
        self.marriage_events(figure, person, events, stats)
        self.child_events(figure, person, events, stats)
        self.award_events(figure, person, events, stats)
        unique = {json.dumps(e, sort_keys=True): e for e in events}
        record = {
            "wikidataId": figure["wikidataId"],
            "birthName": birth,
            "otherNames": other,
            "occupations": self.occupations(person),
            "portrait": self.portrait(person),
            "events": sorted(unique.values(), key=lambda e: (e["date"], e["kind"], e.get("person", ""), e.get("award", ""))),
        }
        return {k: v for k, v in record.items() if v}

    def links(self):
        """Every connection between two figures, once, whichever item states it."""
        found = {}

        def add(first, second, relation, statement=None, group=None, kinship=None):
            key = (*sorted((first["id"], second["id"])), relation, group)
            link = found.setdefault(key, {"first": first["id"], "second": second["id"], "relation": relation})
            if group:
                link["group"] = group
            if kinship:
                link["kinship"] = kinship     # what the second person is to the first
            if statement is not None:
                for field, qualifier in (("from", START), ("to", END)):
                    if field not in link and (date := first_date(statement, qualifier)):
                        link[field] = date

        def ordered(a, b):
            return (a, b) if a["id"] < b["id"] else (b, a)

        for figure in self.figures:
            claims = self.people[figure["wikidataId"]]["claims"]
            for prop, relation in ((SPOUSE, "spouse"), (PARTNER, "partner"), (SIBLING, "sibling")):
                for statement in claims.get(prop, []):
                    if (other := self.by_qid.get(statement["value"])) and other is not figure:
                        add(*ordered(figure, other), relation, statement)
            for statement in claims.get(CHILD, []):
                if other := self.by_qid.get(statement["value"]):
                    add(figure, other, "parent")
            for prop in (FATHER, MOTHER):
                for statement in claims.get(prop, []):
                    if other := self.by_qid.get(statement["value"]):
                        add(other, figure, "parent")
            for statement in claims.get(RELATIVE, []):
                if (other := self.by_qid.get(statement["value"])) and other is not figure:
                    kinship = [self.items.get(k, {}).get("label") for k in statement["q"].get(KINSHIP, [])]
                    if (*sorted((figure["id"], other["id"])), "relative", None) not in found:
                        add(figure, other, "relative", statement, kinship=next((k for k in kinship if k), None))

        members = {}
        for figure in self.figures:
            for statement in self.people[figure["wikidataId"]]["claims"].get(MEMBER_OF, []):
                members.setdefault(statement["value"], {})[figure["id"]] = figure
        for qid, item in self.items.items():
            for statement in item["claims"].get(HAS_PART, []):
                if qid in members and (figure := self.by_qid.get(statement["value"])):
                    members[qid][figure["id"]] = figure
        for qid, group in members.items():
            kinds = [self.items.get(s["value"], {}).get("label", "") for s in self.items.get(qid, {}).get("claims", {}).get(INSTANCE_OF, [])]
            if len(group) < 2 or self.items.get(qid, {}).get("label") in NOT_GROUPS or not any(GROUP_KIND.search(kind) for kind in kinds):
                continue
            everyone = sorted(group.values(), key=lambda f: f["id"])
            for i, first in enumerate(everyone):
                for second in everyone[i + 1:]:
                    add(first, second, "groupmate", group=self.items[qid]["label"])
        return sorted(found.values(), key=lambda l: (l["first"], l["second"], l["relation"], l.get("group", "")))


def main():
    library, out, cache_path = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    figures = json.loads(library.read_text(encoding="utf-8"))
    cache = json.loads(cache_path.read_text(encoding="utf-8")) if cache_path.exists() else {"people": {}, "items": {}}

    fetch([f["wikidataId"] for f in figures], FIGURES_PER_CALL, FIGURE_PROPERTIES, cache["people"], "claims|labels|aliases|sitelinks")
    cache_path.write_text(json.dumps(cache), encoding="utf-8")
    fetch(referenced(cache["people"].values()), ITEMS_PER_CALL, ITEM_PROPERTIES, cache["items"], "claims|labels|sitelinks")
    # The kinds of group: labels of what the groups are instances of.
    kinds = {s["value"] for q in {s["value"] for p in cache["people"].values() for s in p["claims"].get(MEMBER_OF, [])}
             for s in cache["items"].get(q, {}).get("claims", {}).get(INSTANCE_OF, [])}
    fetch(sorted(kinds), ITEMS_PER_CALL, ITEM_PROPERTIES, cache["items"], "labels|sitelinks")
    cache_path.write_text(json.dumps(cache), encoding="utf-8")

    records = Records(figures, cache["people"], cache["items"])
    stats = {"marriages without a date": 0, "children without a date": 0, "children left out, having no page of their own": 0,
             "minor awards left out": 0, "major awards without a date": 0}
    written = {f["id"]: records.record(f, stats) for f in figures}
    links = records.links()

    out.mkdir(parents=True, exist_ok=True)
    (out / "figures.json").write_text(json.dumps(written, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    (out / "connections.json").write_text(json.dumps(links, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    cache_path.with_suffix(".report.json").write_text(
        json.dumps({"stats": stats, "problems": records.problems}, ensure_ascii=False, indent=1), encoding="utf-8")
    print(len(written), "records,", len(links), "connections,", len(records.problems), "problems")


if __name__ == "__main__":
    main()
