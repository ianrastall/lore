"""
Split the master list into research groups, each file a whole prompt to paste into an LLM.

Reads the report from scripts/wikidata_candidates.py, sorts people by field, and within a
field by the number of Wikipedia editions, so the first group of a field holds its best-known
names. The rules, the record format and what to return come from
scripts/celebrity-research-prompt.md, so there is one copy of them. A second run rewrites the
groups and the index, so it would blank the Given to and Back columns: copy them across.

Usage:
    python scripts/build-research-groups.py candidates.json scripts/research-groups
"""

import json
import math
import re
import sys
from pathlib import Path

PROMPT = Path(__file__).parent / "celebrity-research-prompt.md"
GROUP_SIZE = 50

# Who Wikidata could not give correctly, read by hand: description and birth date.
KNOWN = {
    "Dave Stewart": ("English musician, co-founder of Eurythmics", "1952-09-09"),
    "David Howell": ("English chess grandmaster", "1990-11-14"),
    "Fiona Staples": ("Canadian comic book artist, of Saga", ""),
    "Grant Marshall": ("English musician, Daddy G of Massive Attack", "1959-12-18"),
    "Michael Johnson": ("American sprinter, Olympic champion", "1967-09-13"),
    "Pedro Martínez": ("Dominican baseball pitcher, Hall of Fame", "1971-10-25"),
    "Sophia Smith": ("American soccer player", "2000-08-10"),
    "Steve Smith": ("Australian cricketer", "1989-06-02"),
    "Tom Watson": ("American golfer", "1949-09-04"),
    "V": ("South Korean singer, Kim Tae-hyung of BTS", "1995-12-30"),
    "Vince Clarke": ("English musician of Erasure and Depeche Mode", "1960-07-03"),
}
# Stand-in for an edition count: these people are well known, whatever the wrong match had.
KNOWN_EDITIONS = 40

# Order matters: the first rule that matches wins.
RULES = [
    ("Chess", r"chess"),
    ("Games", r"video game|game (designer|developer|programmer|producer)|game creator|dwarf fortress"),
    ("Food", r"\bchef\b|restaurateur|food (writer|critic)|cook(ing|book)?\b|baker\b|pastry"),
    ("Athletes", r"football|soccer|basketball|baseball|tennis|golf|cricket|rugby|hockey|racing driver|formula one|"
                 r"swimmer|sprinter|runner|athlet|gymnast|cyclist|skater|skier|snowboard|surfer|skateboard|climber|"
                 r"boxer|wrestler|hurdler|pole vault|javelin|decathlete|sportsperson|sprint"),
    ("Science", r"physicist|chemist|biolog|mathematician|astronom|astrophysic|cosmolog|geneticist|neuroscien|biochem|"
                r"scientist|primatolog|oceanograph|immunolog|microbiolog|paleo|economist|psycholog|linguist|researcher|"
                r"cryptograph|cryptolog|astronaut|cosmonaut|physician|surgeon|anthropolog|engineer"),
    ("Public life, tech and business", r"programmer|software|computer|entrepreneur|businessp|"
                                       r"business (executive|magnate|woman|man)|chief executive|\bceo\b|investor|"
                                       r"technolog|philanthrop|diplomat|politician|prime minister|president of|"
                                       r"activist|public health"),
    ("Musicians", r"singer|musician|rapper|guitarist|drummer|bassist|bass guitar|songwriter|composer|pianist|violinist|"
                  r"cellist|conductor|\bdj\b|record producer|saxophon|trumpet|jazz|band\b|keyboardist|vocalist|opera"),
    ("Directors and creators", r"film director|director|filmmaker|screenwriter|television (producer|writer|creator)|"
                               r"showrunner|cinematograph|film editor|film producer|animator"),
    ("Actors", r"actor|actress"),
    ("Comedy and media", r"comedian|television (host|personality|presenter)|talk show|journalist|broadcaster|podcast|"
                         r"news ?anchor|presenter|radio|youtuber|internet personality|editor"),
    ("Writers", r"novelist|writer|poet|author|playwright|manga|comics|comic|cartoonist|essayist|historian|biographer|graphic"),
    ("Art and design", r"artist|painter|sculptor|photograph|architect|fashion|designer|ceramic|muralist|model\b"),
]
# The order of the groups: the fields people look for first.
FIELDS = ["Actors", "Musicians", "Athletes", "Directors and creators", "Writers", "Comedy and media",
          "Art and design", "Science", "Public life, tech and business", "Games", "Food", "Chess"]


def field(person):
    description = person.get("description", "").lower()
    everything = description + " | " + " | ".join(person.get("occupations", [])).lower()
    for text in (description, everything):    # the description says what they are chiefly known for
        for name, pattern in RULES:
            if re.search(pattern, text):
                return name
    raise ValueError(f"no field for {person['name']}: {description}")


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def instructions():
    """The rule, the record format and what to return, from the research prompt."""
    text = PROMPT.read_text(encoding="utf-8").replace("\r\n", "\n")
    body = text[text.index("## The rule that matters most"):text.index("## Already in my library")]
    body = re.sub(r"After merging, run .*?\n\n", "", body, flags=re.S)
    ending = "; and the person is not in the list below."
    assert ending in body
    body = body.replace(ending, ".").replace("the category is spelled exactly as listed.", "the category is spelled exactly as listed; and everyone in the table below appears in exactly one of the JSON, the rejected list or the evidence list.")
    return body.strip()


def row(person):
    what = person["description"].replace("|", "/")
    what = what[:1].upper() + what[1:]
    return f"| {person['name']} | {what} | {person.get('born') or 'not known'} |"


def group_file(number, total, title, people, rules):
    lines = [
        f"# Lore research group {number} of {total}: {title}",
        "",
        "Paste everything below the line into a deep-research tool, and bring the whole answer back to be checked and merged.",
        "",
        "---",
        "",
        "I maintain an astrology application whose library of public figures must contain ONLY people with a reliably "
        f"documented birth time. Below is a table of {len(people)} people. Check each of them against the rules that follow, "
        "and return a full record for each one who passes. Do not add anyone who is not in the table.",
        "",
        "Most of them will not pass, and that is expected: a short list of certain entries is what I want, not a full one. "
        "Never guess, estimate or fill a gap to get someone through.",
        "",
        "The table says who each person is, and the birth date is there to tell them from others of the same name. Look at "
        "the record you are reading: if its birth date or occupation does not fit, it is a namesake, not this person. If the "
        "right person's record gives a different date, use the record's and say so in the verification table.",
        "",
        rules,
        "",
        f"## The {len(people)} people to check",
        "",
        "| Name | Who | Born |",
        "|---|---|---|",
    ]
    lines += [row(p) for p in people]
    return "\n".join(lines) + "\n"


def main():
    report, out = Path(sys.argv[1]), Path(sys.argv[2])
    people = json.loads(report.read_text(encoding="utf-8"))
    for person in people:
        if person["name"] in KNOWN:
            description, born = KNOWN[person["name"]]
            person |= {"description": description, "born": born, "occupations": [], "sitelinks": KNOWN_EDITIONS}

    by_field = {f: [] for f in FIELDS}
    for person in people:
        by_field[field(person)].append(person)

    groups = []
    for name in FIELDS:
        members = sorted(by_field[name], key=lambda p: (-p["sitelinks"], p["name"]))
        count = math.ceil(len(members) / GROUP_SIZE)
        for index in range(count):
            part = members[index * len(members) // count:(index + 1) * len(members) // count]
            groups.append((f"{name} {index + 1} of {count}" if count > 1 else name, part))

    rules = instructions()
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("group-*.md"):
        old.unlink()
    index = [
        "# Lore research groups",
        "",
        f"{len(people):,} people in {len(groups)} groups, by field. Within a field the best-known names come first, so group 1 "
        "of a field is the one to start on. Each file is a whole prompt: paste it into a deep-research tool and bring the "
        "answer back to be checked and merged.",
        "",
        "Astro-Databank's robots file names `anthropic-ai`, so these are for other tools, not for Claude's own research.",
        "",
        "Expect few to pass. Most current English-language names have no reliable birth time, and each failure is one line.",
        "",
        "| Group | Field | People | Starts with | Given to | Back |",
        "|---:|---|---:|---|---|---|",
    ]
    for number, (title, part) in enumerate(groups, 1):
        name = f"group-{number:02d}-{slug(title)}.md"
        (out / name).write_text(group_file(number, len(groups), title, part, rules), encoding="utf-8", newline="\n")
        first = ", ".join(p["name"] for p in part[:3])
        index.append(f"| {number} | [{title}]({name}) | {len(part)} | {first} | | |")
    (out / "README.md").write_text("\n".join(index) + "\n", encoding="utf-8", newline="\n")
    print(len(groups), "groups,", sum(len(p) for _, p in groups), "people")
    print({name: len(by_field[name]) for name in FIELDS})


if __name__ == "__main__":
    main()
