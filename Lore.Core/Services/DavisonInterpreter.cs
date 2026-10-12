using Lore.Models;
using System.Globalization;
using System.Text.Json;

namespace Lore.Services;

// Writes the reading of a Davison chart (ChartService.Davison) from an editable corpus
// (Data\davison.json): the relationship's Sun, Moon and Ascendant by sign, its planets by
// house, the closest aspects inside it, and whose own planets stand on its main points.
//
// As with the other readings, everything is deterministic and offline: the same two
// charts always give the same reading, and every sentence comes from the corpus.
public sealed class DavisonInterpreter
{
    private sealed class Corpus
    {
        public Dictionary<string, string> Notes { get; init; } = new();
        public Dictionary<string, string> Sun { get; init; } = new();      // by sign name
        public Dictionary<string, string> Moon { get; init; } = new();
        public Dictionary<string, string> Rising { get; init; } = new();
        public Dictionary<string, string> Houses { get; init; } = new();   // "Planet|House"
        public Dictionary<string, string> HouseAreas { get; init; } = new();

        // Bespoke lines keyed "Point|Tone|Point", the two points in standard order.
        public Dictionary<string, string> Aspects { get; init; } = new();
        public Dictionary<string, string> PointThemes { get; init; } = new();
        public Dictionary<string, string> ToneLinks { get; init; } = new();

        public Dictionary<string, string> Contacts { get; init; } = new(); // by Davison point
        public Dictionary<string, string> PersonThemes { get; init; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // The planets whose house is written up, and that can be stood on.
    private static readonly Planet[] HouseBodies =
        [Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter, Planet.Saturn];

    private const int MaxAspects = 7;
    private const int MaxPerPoint = 3;     // no one point gets to fill the list
    private const int MaxAssembled = 2;    // aspects written from building blocks
    private const int MaxContacts = 6;

    // How close one person's planet must be to a point of the Davison chart to stand on it.
    public const double ContactOrb = 3.0;

    private readonly Corpus _c;

    public DavisonInterpreter(string davisonJsonPath)
    {
        if (File.Exists(davisonJsonPath))
        {
            using var stream = File.OpenRead(davisonJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else
        {
            _c = new Corpus();
        }
    }

    // Null if the comparison carries no Davison chart.
    public DavisonReading? Compose(Synastry s)
    {
        if (s.Davison is not { } d) return null;
        var (a, b) = ShortNames(s);

        var sections = new List<DailySection> { Opening(d) };
        sections.Add(Character(d));
        if (d.Timed) sections.Add(Houses(d));
        sections.Add(Aspects(d));
        sections.Add(Contacts(d, [(a, s.First), (b, s.Second)]));
        sections.Add(Positions(d));
        sections.RemoveAll(x => x.Items.Count == 0);

        return new DavisonReading
        {
            FirstName = s.First.Celebrity.Name,
            SecondName = s.Second.Celebrity.Name,
            Chart = d,
            Sections = sections,
        };
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private DailySection Opening(NatalChart d)
    {
        var items = new List<DailyItem>
        {
            new()
            {
                Title = "A chart for the midpoint in time and place",
                Meta = $"{d.CalculatedForUtc.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)} UT  ·  " +
                       $"{Coordinate(d.Celebrity.Latitude, 'N', 'S')}, {Coordinate(d.Celebrity.Longitude, 'E', 'W')}",
                Text = Lookup(_c.Notes, "intro", ""),
            },
        };
        if (!d.Timed) items.Add(new DailyItem { Text = Lookup(_c.Notes, "untimed", "") });
        items.Add(new DailyItem { Text = Lookup(_c.Notes, "reflection", "") });
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "The relationship's own chart", Items = items };
    }

    // Its Sun, and with both birth times its Moon and Ascendant, by sign.
    private DailySection Character(NatalChart d)
    {
        var items = new List<DailyItem>();
        if (d.GetPlanet(Planet.Sun) is { } sun)
            items.Add(new DailyItem
            {
                Title = $"☉ Sun in {sun.Sign.Name()}",
                Meta = "What the relationship is for",
                Text = Lookup(_c.Sun, sun.Sign.Name(), ""),
            });
        if (d.Timed && d.GetPlanet(Planet.Moon) is { } moon)
            items.Add(new DailyItem
            {
                Title = $"☽ Moon in {moon.Sign.Name()}",
                Meta = "How it feels from inside",
                Text = Lookup(_c.Moon, moon.Sign.Name(), ""),
            });
        if (d.Timed)
        {
            var rising = ZodiacSignExtensions.FromLongitude(d.Ascendant);
            items.Add(new DailyItem
            {
                Title = $"↑ {rising.Name()} rising",
                Meta = "The face it shows the world",
                Text = Lookup(_c.Rising, rising.Name(), ""),
            });
        }
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "Its character", Items = items };
    }

    private DailySection Houses(NatalChart d)
    {
        var items = new List<DailyItem>();
        foreach (var body in HouseBodies)
        {
            if (d.GetPlanet(body) is not { } p) continue;
            int house = d.GetHouseForLongitude(p.Longitude);
            string text = Lookup(_c.Houses, $"{body.Name()}|{house}", "");
            if (text.Length == 0) continue;
            items.Add(new DailyItem
            {
                Title = $"{body.Symbol()} {body.Name()} in the {ChartInterpreter.Ordinal(house)} house",
                Meta = Capitalise(Lookup(_c.HouseAreas, house.ToString(CultureInfo.InvariantCulture), "")),
                Text = text,
            });
        }
        return new DailySection { Heading = "Where its life is lived", Items = items };
    }

    // One aspect inside the chart, between two of its points.
    private sealed record Link(NatalPoint A, NatalPoint B, AspectType Type, double Orb, double Allowed)
    {
        public double Score => (Weight(A) + Weight(B)) * (Allowed > 0 ? Math.Max(0, 1 - Orb / Allowed) : 0);
    }

    // What a point counts for when choosing which aspects to write up; nothing for the
    // points the reading leaves alone.
    private static double Weight(NatalPoint p) => p.Kind switch
    {
        NatalPointKind.Ascendant => 8,
        NatalPointKind.Midheaven => 6,
        NatalPointKind.Body => p.Body switch
        {
            Planet.Sun or Planet.Moon => 10,
            Planet.Venus or Planet.Mars => 8,
            Planet.Mercury or Planet.Saturn => 6,
            Planet.Jupiter => 5,
            Planet.Uranus or Planet.Neptune or Planet.Pluto => 3,
            _ => 0,
        },
        _ => 0,
    };

    // The closest major aspects among the planets and angles. Two of Uranus, Neptune and
    // Pluto in aspect belong to the years, not to the relationship, and without both
    // birth times the Moon's aspects cannot be trusted.
    private DailySection Aspects(NatalChart d)
    {
        var links = d.Aspects.Select(x => new Link(NatalPoint.Of(x.PlanetA), NatalPoint.Of(x.PlanetB), x.Type, x.Orb, x.Allowed))
            .Concat(d.AngleAspects.Select(x => new Link(NatalPoint.Of(x.Planet), x.Angle, x.Type, x.Orb, x.Allowed)))
            .Where(x => x.Type.IsMajor() && Weight(x.A) > 0 && Weight(x.B) > 0)
            .Where(x => !(Slow(x.A) && Slow(x.B)))
            .Where(x => d.Timed || (x.A != NatalPoint.Of(Planet.Moon) && x.B != NatalPoint.Of(Planet.Moon)))
            .OrderByDescending(x => x.Score).ToList();

        // An aspect the corpus has no line of its own for is written from building
        // blocks, which read thinly: only the best couple of those are let in.
        var picked = new List<Link>();
        int assembled = 0;
        foreach (var x in links)
        {
            if (picked.Count == MaxAspects) break;
            bool bespoke = HasLine(x);
            if (!bespoke && assembled == MaxAssembled) continue;
            if (picked.Count(p => p.A == x.A || p.B == x.A) < MaxPerPoint &&
                picked.Count(p => p.A == x.B || p.B == x.B) < MaxPerPoint)
            {
                picked.Add(x);
                if (!bespoke) assembled++;
            }
        }

        var items = picked.Select(x => new DailyItem
        {
            Title = $"{Label(x.A)} {x.Type.Verb()} {Label(x.B)}",
            Meta = $"{x.Type.Name()} {x.Type.Symbol()}  ·  orb {x.Orb.ToString("0.0", CultureInfo.InvariantCulture)}°",
            Text = AspectText(x),
        }).ToList();
        if (items.Count == 0)
            items.Add(new DailyItem { Text = Lookup(_c.Notes, "noAspects", "") });
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "How it works", Items = items };
    }

    private static bool Slow(NatalPoint p) => p.Kind == NatalPointKind.Body && p.Body is Planet.Uranus or Planet.Neptune or Planet.Pluto;

    private static string Key(Link x)
    {
        var (lo, hi) = Order(x.B) < Order(x.A) ? (x.B, x.A) : (x.A, x.B);
        return $"{lo.Name}|{x.Type.Tone()}|{hi.Name}";
    }

    private bool HasLine(Link x) => _c.Aspects.TryGetValue(Key(x), out var line) && !string.IsNullOrWhiteSpace(line);

    // The bespoke line for the pair and tone if the corpus has one; otherwise a plainer
    // sentence from the building blocks.
    private string AspectText(Link x)
    {
        var (lo, hi) = Order(x.B) < Order(x.A) ? (x.B, x.A) : (x.A, x.B);
        if (HasLine(x)) return _c.Aspects[Key(x)];

        return Lookup(_c.ToneLinks, x.Type.Tone().ToString(), "The relationship's {first} meets its {second}.")
            .Replace("{first}", Lookup(_c.PointThemes, lo.Name, lo.Name))
            .Replace("{second}", Lookup(_c.PointThemes, hi.Name, hi.Name));
    }

    // Each person's own planets that stand, within ContactOrb, on a main point of the
    // Davison chart. (The Davison chart is a real sky and not an average of the two
    // charts, so nothing makes these happen: they are worth remarking where they do.)
    private DailySection Contacts(NatalChart d, (string Name, NatalChart Chart)[] people)
    {
        var targets = new List<(NatalPoint Point, double Longitude)>();
        foreach (var body in new[] { Planet.Sun, Planet.Moon, Planet.Venus, Planet.Mars, Planet.Saturn })
        {
            if (body == Planet.Moon && !d.Timed) continue;
            if (d.GetPlanet(body) is { } p) targets.Add((NatalPoint.Of(body), p.Longitude));
        }
        if (d.Timed)
        {
            targets.Add((NatalPoint.Ascendant, d.Ascendant));
            targets.Add((NatalPoint.Midheaven, d.Midheaven));
        }

        var found = new List<(double Orb, string Who, NatalPoint On, DailyItem Item)>();
        foreach (var (name, chart) in people)
        {
            var own = new List<(NatalPoint Point, double Longitude)>();
            foreach (var body in HouseBodies)
            {
                if (body == Planet.Moon && !chart.Timed) continue;
                if (chart.GetPlanet(body) is { } p) own.Add((NatalPoint.Of(body), p.Longitude));
            }
            if (chart.Timed) own.Add((NatalPoint.Ascendant, chart.Ascendant));

            foreach (var (point, longitude) in own)
                foreach (var (target, at) in targets)
                {
                    double orb = Separation(longitude, at);
                    string line = Lookup(_c.Contacts, target.Name, "");
                    if (orb > ContactOrb || line.Length == 0) continue;
                    found.Add((orb, name, target, new DailyItem
                    {
                        Title = $"{name}'s {Label(point)} on the Davison {Label(target)}",
                        Meta = $"Conjunction ☌  ·  orb {orb.ToString("0.0", CultureInfo.InvariantCulture)}°",
                        Text = line.Replace("{name}", name).Replace("{theme}", Lookup(_c.PersonThemes, point.Name, point.Name)),
                    }));
                }
        }

        var items = new List<DailyItem>();
        if (found.Count == 0)
            items.Add(new DailyItem { Text = Lookup(_c.Notes, "noContacts", "") });
        else
        {
            items.Add(new DailyItem { Text = Lookup(_c.Notes, "contactsIntro", "") });
            // One line for each person at each point: the closest of their planets there.
            items.AddRange(found.GroupBy(x => (x.Who, x.On)).Select(g => g.MinBy(x => x.Orb))
                .OrderBy(x => x.Orb).Take(MaxContacts).Select(x => x.Item));
        }
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "Each of you in it", Items = items };
    }

    // Stated, not read: where everything in the chart stands.
    private static DailySection Positions(NatalChart d)
    {
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
            Heading = "Where everything stands",
            Items =
            [
                new DailyItem
                {
                    Meta = d.Timed ? d.HouseSystemLabel : "No Ascendant, Midheaven, houses or Moon: a birth time is missing",
                    Text = string.Join("\n", lines),
                },
            ],
        };
    }

    // ── Wording helpers ───────────────────────────────────────────────────────

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

    private static string Position(double longitude) =>
        $"{ZodiacSignExtensions.FormatDegreeInSign(longitude)} {ZodiacSignExtensions.FromLongitude(longitude).Name()}";

    private static string Coordinate(double value, char positive, char negative)
    {
        int total = (int)Math.Round(Math.Abs(value) * 60);
        return $"{total / 60}°{total % 60:D2}'{(value < 0 ? negative : positive)}";
    }

    private static double Separation(double a, double b)
    {
        double diff = Math.Abs(a - b) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
