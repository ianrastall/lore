using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Writes the synastry reading from the contacts between two charts (SynastryService) and
// an editable corpus (Data\synastry.json). Three steps, as for the daily horoscope: rank
// the contacts, choose a few that fit in a readable page, then put the matching authored
// text under each.
//
// Everything here is deterministic and offline: the same two charts always give the same
// reading, and every sentence comes from the corpus — nothing is generated at run time.
public sealed class SynastryInterpreter
{
    private sealed class Corpus
    {
        // Bespoke lines keyed "Point|Tone|Point" with the two points in standard order
        // (e.g. "Venus|Tension|Mars"); {a} owns the first point, {b} the second.
        public Dictionary<string, string> Aspects { get; init; } = new();

        // Building blocks for any pair without a bespoke line.
        public Dictionary<string, string> PointThemes { get; init; } = new();
        public Dictionary<string, string> ToneLinks { get; init; } = new();

        public Dictionary<string, string> SunElements { get; init; } = new();
        public Dictionary<string, string> MoonElements { get; init; } = new();
        public Dictionary<string, string> OverlayPlanets { get; init; } = new();
        public Dictionary<string, string> OverlayHouses { get; init; } = new();
        public Dictionary<string, string> Houses { get; init; } = new();
        public Dictionary<string, string> Tones { get; init; } = new();
        public Dictionary<string, string> Notes { get; init; } = new();

        // The Trine reading of three charts.
        public Dictionary<string, string> TrioTones { get; init; } = new();
        public Dictionary<string, string> GrandTrines { get; init; } = new();
        public Dictionary<string, string> TrioFigures { get; init; } = new();
        public Dictionary<string, string> TrioElements { get; init; } = new();
        public Dictionary<string, string> TrioLacking { get; init; } = new();

        // Contacts as the answers describe them: tone → "strain|well" and so on → sentence.
        public Dictionary<string, Dictionary<string, string>> LivedContacts { get; init; } = new();
        public List<LivedPair> LivedPairs { get; init; } = [];
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // How much fits on the page.
    private const int MaxBonds = 3;      // conjunctions
    private const int MaxEasy = 4;       // sextiles and trines
    private const int MaxHard = 4;       // squares and oppositions
    private const int MaxPerPoint = 2;   // no one point (a Sun, say) gets to fill a list

    private const int MaxGrandTrines = 3; // the strongest, in the Trine reading
    private const int MaxFigures = 4;     // of the other figures
    private const int MaxPairLines = 5;   // of what the two sets of answers come to

    // The planets whose place in the other person's houses is written up.
    private static readonly Planet[] OverlayBodies = [Planet.Sun, Planet.Moon, Planet.Venus, Planet.Mars];

    private readonly Corpus _c;

    public SynastryInterpreter(string synastryJsonPath)
    {
        if (File.Exists(synastryJsonPath))
        {
            using var stream = File.OpenRead(synastryJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else
        {
            _c = new Corpus();
        }
    }

    // `first`, `second`: each person's answers, if they have any. They shape which contacts
    // the reading spends its slots on, what is said of each, and the balance as lived; the
    // charts' own balance is unchanged and stays the headline.
    public SynastryReading Compose(Synastry s, MirrorReading? first = null, MirrorReading? second = null)
    {
        var (a, b) = ShortNames(s);
        if (first is not null && !first.IsFor(s.First)) first = null;
        if (second is not null && !second.IsFor(s.Second)) second = null;

        foreach (var x in s.Aspects)
        {
            x.Score = SynastryScoring.Priority(x) * Lift(x.First, first) * Lift(x.Second, second);
            x.Shown = false;
        }
        var ranked = s.Aspects.OrderByDescending(x => x.Score).ToList();

        // Two slow bodies in aspect (her Neptune, his Pluto) link everyone born in the
        // same years, so they say nothing about these two people in particular.
        var personal = ranked.Where(x => !SynastryScoring.IsGenerational(x)).ToList();

        var bonds = Pick(personal.Where(x => x.Tone == TransitTone.Conjunction), MaxBonds);
        var easy = Pick(personal.Where(x => x.Tone == TransitTone.Flow), MaxEasy);
        var hard = Pick(personal.Where(x => x.Tone == TransitTone.Tension), MaxHard);
        foreach (var x in bonds.Concat(easy).Concat(hard)) x.Shown = true;

        var compatibility = SynastryScoring.Assess(ranked);
        SynastryTone tone = compatibility.Tone;
        var asLived = first is null && second is null ? null : SynastryScoring.Assess(ranked, first, second);

        var said = new HashSet<string>();
        var sections = new List<DailySection> { Glance(s, a, b, tone, asLived) };
        AddAspects(sections, "Closest bonds", bonds, a, b, first, second, said);
        AddAspects(sections, "What comes easily", easy, a, b, first, second, said);
        AddAspects(sections, "What takes work", hard, a, b, first, second, said);

        var overlays = Overlays(s.FirstInSecondHouses, a, b).Concat(Overlays(s.SecondInFirstHouses, b, a)).ToList();
        if (overlays.Count > 0)
            sections.Add(new DailySection { Heading = "In each other's houses", Items = overlays });

        if (first is not null && second is not null && BothSets(first, second, a, b) is { Items.Count: > 0 } both)
            sections.Add(both);

        if (s.Davison is { } davison)
            sections.Add(DavisonSection(davison));

        return new SynastryReading
        {
            FirstName = s.First.Celebrity.Name,
            SecondName = s.Second.Celebrity.Name,
            Tone = tone,
            Compatibility = compatibility,
            AsLived = asLived,
            Sections = sections,
            Aspects = ranked,
            Trace = ranked.Select(x => TraceLine(x, a, b)).ToList(),
        };
    }

    // How much more a contact counts for the reading when its owner's answers mark out
    // the planet: as a day's reading does, what is strained is what will be noticed.
    private static double Lift(NatalPoint p, MirrorReading? answers) =>
        answers is null || p.Kind != NatalPointKind.Body ? 1 : answers.LivedOf(p.Body) switch
        {
            MirrorLived.Strain => MirrorReading.StrainLift,
            MirrorLived.Well => MirrorReading.WellLift,
            _ => 1,
        };

    private string LivedWord(SynastryTone tone) => $"{tone.Label()} ({tone.LevelText()})";

    // "By the answers it reads as Mixed (0), and the charts as Harmonious (+1)."
    private DailyItem LivedGlance(SynastryTone tone, Compatibility asLived) => new()
    {
        Title = "By the answers",
        Text = Lookup(_c.Notes, asLived.Level == tone.Level() ? "livedSame" : asLived.Level > tone.Level() ? "livedBetter" : "livedWorse", "")
            .Replace("{lived}", LivedWord(asLived.Tone)).Replace("{charts}", LivedWord(tone)),
    };

    // A contact's sentence from how its two planets are lived, once for each planet. The
    // corpus has each unordered pair of strain, well and plain once, the first named first.
    private string LivedLine(SynastryAspect x, string a, string b, MirrorReading? first, MirrorReading? second, HashSet<string> said)
    {
        static string Word(MirrorLived lived) => lived switch { MirrorLived.Strain => "strain", MirrorLived.Well => "well", _ => "plain" };
        static MirrorPlanet? Read(NatalPoint p, MirrorReading? answers) =>
            answers is not null && p.Kind == NatalPointKind.Body ? answers.Planets.FirstOrDefault(r => r.Planet == p.Body && !r.IsOrdinary) : null;

        var (pa, pb) = (Read(x.First, first), Read(x.Second, second));
        if (pa is null && pb is null) return "";
        var left = (Name: a, Point: x.First, Read: pa);
        var right = (Name: b, Point: x.Second, Read: pb);
        MirrorLived Lived(MirrorPlanet? p) => p?.Lived ?? MirrorLived.Plain;
        static int Rank(MirrorLived lived) => lived switch { MirrorLived.Strain => 0, MirrorLived.Well => 1, _ => 2 };
        if (Rank(Lived(pb)) < Rank(Lived(pa))) (left, right) = (right, left);

        string key = $"{Word(Lived(left.Read))}|{Word(Lived(right.Read))}";
        if (key == "plain|plain") return "";
        if (!said.Add($"{left.Name}|{left.Point.Name}") && !said.Add($"{right.Name}|{right.Point.Name}")) return "";
        string line = _c.LivedContacts.GetValueOrDefault(x.Tone.ToString())?.GetValueOrDefault(key) ?? "";
        return line.Length == 0 ? "" : " " + line
            .Replace("{a}", left.Name).Replace("{pa}", left.Point.Name).Replace("{ea}", left.Read?.Expression ?? "")
            .Replace("{b}", right.Name).Replace("{pb}", right.Point.Name).Replace("{eb}", right.Read?.Expression ?? "");
    }

    // The two sets of answers set against each other, whatever the charts say.
    private DailySection BothSets(MirrorReading first, MirrorReading second, string a, string b)
    {
        static bool Fits(MirrorReading answers, string rule)
        {
            var parts = rule.Split('=');
            return answers.Levels.GetValueOrDefault(parts[0]) == parts[1];
        }

        var items = new List<DailyItem>();
        foreach (var rule in _c.LivedPairs)
        {
            if (items.Count == MaxPairLines || rule.When.Count != 2) break;
            string? x = null, y = null;
            if (Fits(first, rule.When[0]) && Fits(second, rule.When[1])) (x, y) = (a, b);
            else if (Fits(second, rule.When[0]) && Fits(first, rule.When[1])) (x, y) = (b, a);
            if (x is null) continue;
            items.Add(new DailyItem { Title = rule.Title, Text = Fill(rule.Text, x, y!) });
        }
        return new DailySection { Heading = "The two sets of answers", Items = items };
    }

    // ── Three charts ──────────────────────────────────────────────────────────

    public TrioReading ComposeTrio(Trio trio)
    {
        var full = trio.Charts.Select(c => c.Celebrity.Name).ToList();
        var names = ShortNames(full);

        var glance = new List<DailyItem> { new() { Text = Lookup(_c.TrioTones, trio.Tone.ToString(), "") } };
        foreach (var chart in trio.Charts.Where(c => !c.Celebrity.BirthTimeKnown))
            glance.Add(new DailyItem { Text = Lookup(_c.Notes, "unknownBirthTimeOne",
                "{name} has no recorded birth time, so that chart's Ascendant, Midheaven, houses and Moon " +
                "are left out of the comparison.").Replace("{name}", names[trio.Charts.ToList().IndexOf(chart)]) });
        glance.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));

        // Each kind of circuit is described once: two in one element say the same thing.
        var told = new HashSet<string>();
        var grand = trio.GrandTrines.Take(MaxGrandTrines).Select(g =>
        {
            string kind = g.Element?.ToString() ?? "Mixed";
            return new DailyItem
            {
                Title = JoinList(g.Corners.Select(c => $"{names[c.Person]}'s {Label(c.Point)}")),
                Meta = $"{g.Element?.ToString() ?? "Out of sign"}  ·  widest orb {g.WidestOrb:0.0}°",
                Text = told.Add(kind)
                    ? Lookup(_c.GrandTrines, kind, "")
                    : Lookup(_c.Notes, "trioSameCircuit", "Another strand of the same circuit."),
            };
        }).ToList();
        if (grand.Count == 0)
            grand.Add(new DailyItem { Text = Lookup(_c.Notes, "trioNoGrandTrine", "No grand trine runs through all three charts.") });
        if (trio.GrandTrines.Count > MaxGrandTrines)
            grand.Add(new DailyItem { Text = Lookup(_c.Notes, "trioMoreGrandTrines", "{count} weaker grand trines are not listed.")
                .Replace("{count}", (trio.GrandTrines.Count - MaxGrandTrines).ToString()) });

        if (trio.LivedTone is { } lived)
            glance.Add(new DailyItem
            {
                Title = "By the answers",
                Text = Lookup(_c.Notes, lived == trio.Tone ? "trioLivedSame" : lived < trio.Tone ? "trioLivedBetter" : "trioLivedWorse", "")
                    .Replace("{lived}", lived.Label()).Replace("{charts}", trio.Tone.Label()),
            });
        glance.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));

        var weakest = trio.Weakest;
        var pairs = trio.Pairs.Select(p => new DailyItem
        {
            Title = $"{names[p.First]} and {names[p.Second]}",
            Meta = $"{p.Compatibility.Tone.Label()} ({p.Compatibility.Tone.LevelText()})  ·  " +
                   $"{Math.Round(p.Compatibility.Share * 100):0}% easy" +
                   (p.AsLived is { } asLived ? $"  ·  by the answers, {asLived.Tone.Label()} ({asLived.Tone.LevelText()})" : "") +
                   (ReferenceEquals(p, weakest) ? "  ·  the weakest of the three" : ""),
            Text = Lookup(_c.Tones, p.Compatibility.Tone.ToString(), ""),
        }).Where(i => i.Text.Length > 0).ToList();

        var sections = new List<DailySection>
        {
            new() { Heading = "At a glance", Items = glance },
            new() { Heading = "Grand trines through all three charts", Items = grand },
        };
        if (Figures(trio, names) is { Items.Count: > 0 } figures) sections.Add(figures);
        sections.Add(new DailySection { Heading = "The three pairs", Items = pairs });
        if (Standing(trio, names) is { Items.Count: > 0 } standing) sections.Add(standing);
        if (Elements(trio, names) is { Items.Count: > 0 } elements) sections.Add(elements);
        if (TrioAnswers(trio, names) is { Items.Count: > 0 } answers) sections.Add(answers);
        sections.Add(new DailySection { Heading = "How this is measured", Items = [new DailyItem { Text = Lookup(_c.Notes, "trioScale", "") }] });

        return new TrioReading { Names = full, Trio = trio, Sections = sections };
    }

    // The other figures that run through all three, the strongest of each kind first.
    private DailySection Figures(Trio trio, List<string> names)
    {
        var told = new HashSet<TrioFigureKind>();
        var items = trio.Figures.OrderByDescending(f => f.Weight).Take(MaxFigures).Select(f =>
        {
            string line = told.Add(f.Kind) ? Lookup(_c.TrioFigures, f.Kind.ToString(), "") : Lookup(_c.Notes, "trioSameFigure", "Another figure of the same kind.");
            string focus = f.Focus is { } person ? names[person] : "";
            return new DailyItem
            {
                Title = $"{f.Kind.Label()}: {JoinList(f.Corners.Select(c => $"{names[c.Person]}'s {Label(c.Point)}"))}",
                Meta = $"widest orb {f.WidestOrb:0.0}°" + (focus.Length > 0 ? $"  ·  the focus is {focus}" : ""),
                Text = line.Replace("{focus}", focus),
            };
        }).ToList();
        return new DailySection { Heading = "Other figures through all three charts", Items = items };
    }

    // Who the other two both get on with, and who they do not.
    private DailySection Standing(Trio trio, List<string> names)
    {
        if (trio.Hub is not { } hub || trio.Outsider is not { } outsider) return new DailySection { Heading = "", Items = [] };
        return new DailySection
        {
            Heading = "Who holds it together",
            Items =
            [
                new DailyItem
                {
                    Title = $"{names[hub]} is the easiest of the three",
                    Text = Lookup(_c.Notes, "trioHub", "").Replace("{name}", names[hub]).Replace("{other}", names[outsider]),
                },
            ],
        };
    }

    // What the three bring between them: an element nobody has, and one that rests on one person.
    private DailySection Elements(Trio trio, List<string> names)
    {
        var items = new List<DailyItem>();
        foreach (var (element, counts) in TrioService.ElementsOf(trio))
        {
            int total = counts.Sum(), most = counts.Max();
            if (total == 0)
                items.Add(new DailyItem { Title = $"No {element}", Text = Lookup(_c.TrioLacking, element.ToString(), "") });
            else if (most >= 3 && most * 3 >= total * 2)
                items.Add(new DailyItem
                {
                    Title = $"{names[Array.IndexOf(counts, most)]} brings the {element}",
                    Text = Lookup(_c.TrioElements, element.ToString(), "").Replace("{name}", names[Array.IndexOf(counts, most)]),
                });
        }
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "What the three bring between them", Items = items };
    }

    // Each person's most strained and most easily lived planets among those they bring to
    // a pair: where the answers put the weight of the trio.
    private DailySection TrioAnswers(Trio trio, List<string> names)
    {
        var items = new List<DailyItem>();
        for (int i = 0; i < trio.Charts.Count; i++)
        {
            if (trio.Answers.ElementAtOrDefault(i) is not { } answers) continue;
            static string Named(Planet p) => p is Planet.Sun or Planet.Moon or Planet.NorthNode ? "the " + p.Name() : p.Name();
            var strained = answers.Planets.Where(p => p.Lived == MirrorLived.Strain && !p.IsOrdinary).Select(p => Named(p.Planet)).ToList();
            var well = answers.Planets.Where(p => p.Lived == MirrorLived.Well && !p.IsOrdinary).Select(p => Named(p.Planet)).ToList();
            if (strained.Count == 0 && well.Count == 0) continue;
            string text = "";
            if (strained.Count > 0) text += Lookup(_c.Notes, "trioStrained", "").Replace("{name}", names[i]).Replace("{list}", JoinList(strained));
            if (well.Count > 0) text += (text.Length > 0 ? " " : "") + Lookup(_c.Notes, "trioWell", "").Replace("{name}", names[i]).Replace("{list}", JoinList(well));
            items.Add(new DailyItem { Title = names[i], Text = text });
        }
        return new DailySection { Heading = "As the three are living it", Items = items };
    }

    // ── Ranking ───────────────────────────────────────────────────────────────
    // Which contacts the reading spends its few slots on (the weights are in SynastryScoring).

    // The best-ranked few, with a cap on how often either person's same point appears.
    private static List<SynastryAspect> Pick(IEnumerable<SynastryAspect> ranked, int max)
    {
        var picked = new List<SynastryAspect>();
        foreach (var x in ranked)
        {
            if (picked.Count == max) break;
            if (picked.Count(p => p.First == x.First) < MaxPerPoint &&
                picked.Count(p => p.Second == x.Second) < MaxPerPoint)
                picked.Add(x);
        }
        return picked;
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private DailySection Glance(Synastry s, string a, string b, SynastryTone tone, Compatibility? asLived)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Lookup(_c.Tones, tone.ToString(), "") }
        };
        if (asLived is not null) items.Add(LivedGlance(tone, asLived));

        bool timedA = s.First.Celebrity.BirthTimeKnown, timedB = s.Second.Celebrity.BirthTimeKnown;

        if (ElementItem(_c.SunElements, Planet.Sun, s, a, b) is { } suns) items.Add(suns);
        // Without a birth time the Moon may be in the next sign along.
        if (timedA && timedB && ElementItem(_c.MoonElements, Planet.Moon, s, a, b) is { } moons) items.Add(moons);

        if (!timedA && !timedB)
            items.Add(new DailyItem { Text = Lookup(_c.Notes, "unknownBirthTimeBoth",
                "Neither chart has a recorded birth time, so the Ascendants, Midheavens, houses and Moons " +
                "are left out of the comparison.") });
        else if (!timedA || !timedB)
            items.Add(new DailyItem { Text = Lookup(_c.Notes, "unknownBirthTimeOne",
                "{name} has no recorded birth time, so that chart's Ascendant, Midheaven, houses and Moon " +
                "are left out of the comparison.").Replace("{name}", timedA ? b : a) });

        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "At a glance", Items = items };
    }

    // The two Sun signs (or Moon signs) compared by element.
    private DailyItem? ElementItem(Dictionary<string, string> lines, Planet body, Synastry s, string a, string b)
    {
        if (s.First.GetPlanet(body) is not { } first || s.Second.GetPlanet(body) is not { } second)
            return null;

        Element ea = first.Sign.GetElement(), eb = second.Sign.GetElement();
        // Corpus keys run in element order, so the person with the earlier element is {a}.
        bool swap = eb < ea;
        string key = swap ? $"{eb}|{ea}" : $"{ea}|{eb}";

        return new DailyItem
        {
            Title = $"{body.Symbol()} {body.Name()} signs: {a} in {first.Sign.Name()}, {b} in {second.Sign.Name()}",
            Meta = ea == eb ? $"Both {ea}" : $"{ea} and {eb}",
            Text = Fill(Lookup(lines, key, ""), swap ? b : a, swap ? a : b)
        };
    }

    private void AddAspects(List<DailySection> sections, string heading, List<SynastryAspect> aspects, string a, string b,
        MirrorReading? first, MirrorReading? second, HashSet<string> said)
    {
        if (aspects.Count == 0) return;
        sections.Add(new DailySection
        {
            Heading = heading,
            Items = aspects.Select(x => new DailyItem
            {
                Title = $"{a}'s {Label(x.First)} {x.Type.Verb()} {b}'s {Label(x.Second)}",
                Meta = $"{x.Type} {x.Type.Symbol()}  ·  orb {x.Orb:0.0}°",
                Text = AspectText(x, a, b) + LivedLine(x, a, b, first, second, said)
            }).ToList()
        });
    }

    // The bespoke line for this pair and tone if the corpus has one; otherwise a plainer
    // sentence assembled from the building blocks.
    private string AspectText(SynastryAspect x, string a, string b)
    {
        // Corpus keys name the two points in standard order, whoever owns which.
        bool swap = Order(x.Second) < Order(x.First);
        var (lo, hi) = swap ? (x.Second, x.First) : (x.First, x.Second);

        if (_c.Aspects.TryGetValue($"{lo.Name}|{x.Tone}|{hi.Name}", out var bespoke) && !string.IsNullOrWhiteSpace(bespoke))
            return Fill(bespoke, swap ? b : a, swap ? a : b);

        string first = Lookup(_c.PointThemes, x.First.Name, x.First.Name);
        string link = Lookup(_c.ToneLinks, x.Tone.ToString(), "meets");
        string second = Lookup(_c.PointThemes, x.Second.Name, x.Second.Name);
        return $"{a}'s {first} {link} {b}'s {second}.";
    }

    // One person's Sun, Moon, Venus and Mars in the other's houses: one entry per house,
    // so planets that fall together are read together rather than repeating the line.
    private IEnumerable<DailyItem> Overlays(IReadOnlyList<HouseOverlay> overlays, string guest, string host)
    {
        var byHouse = OverlayBodies
            .SelectMany(body => overlays.Where(o => o.Planet == body))
            .GroupBy(o => o.House);

        foreach (var house in byHouse)
        {
            string text = Lookup(_c.OverlayHouses, house.Key.ToString(), "");
            if (text.Length == 0) continue;

            var bodies = house.Select(o => o.Planet).ToList();
            yield return new DailyItem
            {
                Title = $"{guest}'s {JoinList(bodies.Select(p => $"{p.Symbol()} {p.Name()}"))} " +
                        $"in {host}'s {ChartInterpreter.Ordinal(house.Key)} house",
                Meta = Capitalise(Lookup(_c.Houses, house.Key.ToString(), "")),
                Text = text.Replace("{guest}", guest).Replace("{host}", host)
                           .Replace("{brings}", JoinList(bodies.Select(p => Lookup(_c.OverlayPlanets, p.Name(), p.Name()))))
            };
        }
    }

    // The Davison chart is read on its own (DavisonInterpreter); here it is only said
    // what it is, when and where it is cast for, and where its reading is to be found.
    private DailySection DavisonSection(NatalChart d)
    {
        static string Coordinate(double value, char positive, char negative)
        {
            int total = (int)Math.Round(Math.Abs(value) * 60);
            return $"{total / 60}°{total % 60:D2}'{(value < 0 ? negative : positive)}";
        }

        return new DailySection
        {
            Heading = "The Davison chart",
            Items =
            [
                new DailyItem
                {
                    Title = "A chart for the midpoint in time and place",
                    Meta = $"{d.CalculatedForUtc.ToString("d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UT  ·  " +
                           $"{Coordinate(d.Celebrity.Latitude, 'N', 'S')}, {Coordinate(d.Celebrity.Longitude, 'E', 'W')}",
                    Text = Lookup(_c.Notes, "davison",
                        "The Davison chart is cast for the moment halfway between the two births, at the place halfway " +
                        "between the two birthplaces. Unlike the comparison above, which sets one chart against the " +
                        "other, it is a single real chart, and astrologers read it as the chart of the relationship itself."),
                },
                new DailyItem
                {
                    Text = Lookup(_c.Notes, "davisonPointer",
                        "The Davison chart has a reading, a wheel and a worksheet of its own: choose Davison chart at the top of the Synastry view."),
                },
            ],
        };
    }

    // "warmth", "warmth and drive", "warmth, affection and drive".
    private static string JoinList(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }

    // ── Wording helpers ───────────────────────────────────────────────────────

    private static string TraceLine(SynastryAspect x, string a, string b)
    {
        string shown = x.Shown ? "   ✓ in the reading" : "";
        return $"{x.First.Symbol} {x.Type.Symbol()} {x.Second.Symbol}   " +
               $"{a}'s {x.First.Name} {x.Type.Verb()} {b}'s {x.Second.Name}  ·  orb {x.Orb:0.00}°{shown}";
    }

    // First names, unless the two people share one.
    private static (string a, string b) ShortNames(Synastry s)
    {
        string fullA = s.First.Celebrity.Name, fullB = s.Second.Celebrity.Name;
        string a = FirstName(fullA), b = FirstName(fullB);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? (fullA, fullB) : (a, b);
    }

    // First names, unless two of the people share one.
    private static List<string> ShortNames(IReadOnlyList<string> full)
    {
        var first = full.Select(FirstName).ToList();
        return first.Select((name, i) =>
            first.Count(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 1 ? full[i] : name).ToList();
    }

    private static string FirstName(string full)
    {
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : full;
    }

    // Sun, Moon … Lilith, then the Ascendant and Midheaven.
    private static int Order(NatalPoint p) => p.Kind switch
    {
        NatalPointKind.Ascendant => 13,
        NatalPointKind.Midheaven => 14,
        _ => (int)p.Body
    };

    private static string Label(NatalPoint p) => p.IsAngle ? p.Name : $"{p.Symbol} {p.Name}";

    private static string Fill(string line, string a, string b) => line.Replace("{a}", a).Replace("{b}", b);

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
