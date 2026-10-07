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
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // How much fits on the page.
    private const int MaxBonds = 3;      // conjunctions
    private const int MaxEasy = 4;       // sextiles and trines
    private const int MaxHard = 4;       // squares and oppositions
    private const int MaxPerPoint = 2;   // no one point (a Sun, say) gets to fill a list

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

    public SynastryReading Compose(Synastry s)
    {
        var (a, b) = ShortNames(s);

        foreach (var x in s.Aspects)
        {
            x.Score = SynastryScoring.Priority(x);
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

        var sections = new List<DailySection> { Glance(s, a, b, tone) };
        AddAspects(sections, "Closest bonds", bonds, a, b);
        AddAspects(sections, "What comes easily", easy, a, b);
        AddAspects(sections, "What takes work", hard, a, b);

        var overlays = Overlays(s.FirstInSecondHouses, a, b).Concat(Overlays(s.SecondInFirstHouses, b, a)).ToList();
        if (overlays.Count > 0)
            sections.Add(new DailySection { Heading = "In each other's houses", Items = overlays });

        if (s.Davison is { } davison)
            sections.Add(DavisonSection(davison));

        return new SynastryReading
        {
            FirstName = s.First.Celebrity.Name,
            SecondName = s.Second.Celebrity.Name,
            Tone = tone,
            Compatibility = compatibility,
            Sections = sections,
            Aspects = ranked,
            Trace = ranked.Select(x => TraceLine(x, a, b)).ToList(),
        };
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

    private DailySection Glance(Synastry s, string a, string b, SynastryTone tone)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Lookup(_c.Tones, tone.ToString(), "") }
        };

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

    private void AddAspects(List<DailySection> sections, string heading, List<SynastryAspect> aspects, string a, string b)
    {
        if (aspects.Count == 0) return;
        sections.Add(new DailySection
        {
            Heading = heading,
            Items = aspects.Select(x => new DailyItem
            {
                Title = $"{a}'s {Label(x.First)} {x.Type.Verb()} {b}'s {Label(x.Second)}",
                Meta = $"{x.Type} {x.Type.Symbol()}  ·  orb {x.Orb:0.0}°",
                Text = AspectText(x, a, b)
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

    // The Davison chart, stated and not interpreted: when and where it is cast for, and
    // where everything in it stands.
    private DailySection DavisonSection(NatalChart d)
    {
        static string Position(double longitude) =>
            $"{ZodiacSignExtensions.FormatDegreeInSign(longitude)} {ZodiacSignExtensions.FromLongitude(longitude).Name()}";
        static string Coordinate(double value, char positive, char negative)
        {
            int total = (int)Math.Round(Math.Abs(value) * 60);
            return $"{total / 60}°{total % 60:D2}'{(value < 0 ? negative : positive)}";
        }

        var lines = new List<string>();
        if (d.Timed)
        {
            lines.Add($"Ascendant {Position(d.Ascendant)}");
            lines.Add($"Midheaven {Position(d.Midheaven)}");
        }
        lines.AddRange(d.Planets
            .Where(p => d.Timed || p.Planet != Planet.Moon)
            .Select(p => $"{p.PlanetSymbol} {p.PlanetName} {Position(p.Longitude)}{(p.IsRetrograde ? " ℞" : "")}" +
                         (d.Timed ? $" — {ChartInterpreter.Ordinal(d.GetHouseForLongitude(p.Longitude))} house" : "")));

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
                    Title = "Where everything stands in it",
                    Meta = d.Timed ? d.HouseSystemLabel
                        : Lookup(_c.Notes, "davisonUntimed",
                            "a birth time is missing, so the midpoint may be hours out: the Ascendant, Midheaven, houses and Moon are left out"),
                    Text = string.Join("\n", lines),
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
