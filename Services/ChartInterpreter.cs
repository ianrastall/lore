using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Turns the raw numbers of a NatalChart into a readable natural-language report by
// combining a small, editable corpus (Data\interpretations.json) generatively.
public sealed class ChartInterpreter
{
    private sealed class Corpus
    {
        public Dictionary<string, string> SignTraits { get; init; } = new();
        public Dictionary<string, string> RoleFraming { get; init; } = new();
        public Dictionary<string, string> PlanetThemes { get; init; } = new();
        public Dictionary<string, string> SignStyles { get; init; } = new();
        // Bespoke "Planet in Sign" lines keyed "Planet|Sign" (e.g. "Sun|Aries").
        // Preferred when present; otherwise PlanetThemes + SignStyles are combined.
        public Dictionary<string, string> PlanetInSign { get; init; } = new();
        public Dictionary<string, string> HouseAreas { get; init; } = new();
        public Dictionary<string, string> AspectDynamics { get; init; } = new();
        public Dictionary<string, string> AspectNotes { get; init; } = new();
        public Dictionary<string, string> Elements { get; init; } = new();
        public Dictionary<string, string> Modalities { get; init; } = new();

        // Overview paragraphs keyed by sign; SunMoonBlend is keyed "SunElement|MoonElement".
        public Dictionary<string, string> SunSigns { get; init; } = new();
        public Dictionary<string, string> MoonSigns { get; init; } = new();
        public Dictionary<string, string> RisingSigns { get; init; } = new();
        public Dictionary<string, string> SunMoonBlend { get; init; } = new();

        // The sentence after each Planet-in-Sign line, keyed "Planet|House" (e.g. "Venus|7").
        public Dictionary<string, string> PlanetInHouse { get; init; } = new();
        public Dictionary<string, string> Retrogrades { get; init; } = new();

        // Bespoke lines for the major aspects keyed "Point|Tone|Point" with the two points
        // in standard order (e.g. "Moon|Tension|Saturn"), and the blocks for the rest.
        public Dictionary<string, string> Aspects { get; init; } = new();
        public Dictionary<string, string> AngleThemes { get; init; } = new();
        public Dictionary<string, string> AspectToneLinks { get; init; } = new();

        public Dictionary<string, string> ElementStrong { get; init; } = new();
        public Dictionary<string, string> ElementWeak { get; init; } = new();
        public Dictionary<string, string> ModalityStrong { get; init; } = new();
        public Dictionary<string, string> ModalityWeak { get; init; } = new();
        public Dictionary<string, string> BalanceNotes { get; init; } = new();

        public Dictionary<string, string> StelliumSigns { get; init; } = new();
        public Dictionary<string, string> GrandTrines { get; init; } = new();
        public Dictionary<string, string> TSquares { get; init; } = new();
        public Dictionary<string, string> GrandCrosses { get; init; } = new();
        public Dictionary<string, string> ApexPoints { get; init; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly Corpus _c;

    public ChartInterpreter(string interpretationsJsonPath)
    {
        if (File.Exists(interpretationsJsonPath))
        {
            using var stream = File.OpenRead(interpretationsJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else
        {
            _c = new Corpus();
        }
    }

    // `moonSigns`: for a chart with no birth time, the signs the Moon passed through
    // that day (see TimeSensitivityService.AnalyseDay). More than one means the Moon's
    // sign is not known, and the report says so rather than reading the noon guess.
    public IReadOnlyList<ReportSection> Interpret(NatalChart chart, IReadOnlyList<ZodiacSign>? moonSigns = null)
    {
        var sections = new List<ReportSection>
        {
            Overview(chart, moonSigns),
            Planets(chart),
            Aspects(chart),
            Patterns(chart),
            Balance(chart),
            ModalBalance(chart),
        };
        return sections;
    }

    private ReportSection Overview(NatalChart chart, IReadOnlyList<ZodiacSign>? moonSigns)
    {
        string name = FirstName(chart.Celebrity.Name);
        var paras = new List<string>();

        var sun = chart.GetPlanet(Planet.Sun);
        var moon = chart.GetPlanet(Planet.Moon);
        var risingSign = ZodiacSignExtensions.FromLongitude(chart.Ascendant);

        if (sun is not null)
            paras.Add(Frame("Sun", _c.SunSigns, name, sun.Sign));
        bool moonUnsure = !chart.Timed && moonSigns is { Count: > 1 };
        if (moonUnsure)
            paras.Add($"The Moon changed sign on the day {name} was born — it was in " +
                      $"{string.Join(" or ", moonSigns!.Select(s => s.Name()))} depending on the hour — so without a " +
                      "birth time the Moon sign can't be given. The Moon line under The Planets below is for noon.");
        else if (moon is not null)
        {
            paras.Add(Frame("Moon", _c.MoonSigns, name, moon.Sign));
            // How the two fit together, by element.
            if (sun is not null)
            {
                string blend = Lookup(_c.SunMoonBlend, $"{sun.Sign.GetElement()}|{moon.Sign.GetElement()}", "");
                if (blend.Length > 0) paras.Add(blend);
            }
        }
        if (chart.Timed)
            paras.Add(Frame("Rising", _c.RisingSigns, name, risingSign));
        else
            paras.Add("Note: the birth time is unknown, so the planets are placed for noon. The Rising " +
                      "sign and the houses can't be known without a time and are left out, and the Moon " +
                      "may be up to seven degrees from where it is shown.");

        return new ReportSection { Heading = "Overview", Paragraphs = paras };
    }

    private ReportSection Planets(NatalChart chart)
    {
        var paras = new List<string>();
        foreach (var p in chart.Planets)
        {
            // Houses need a birth time; without one the line stops at the sign.
            int house = chart.Timed ? chart.GetHouseForLongitude(p.Longitude) : 0;
            string inHouse = chart.Timed
                ? " " + Lookup(_c.PlanetInHouse, $"{p.PlanetName}|{house}",
                    $"This colours {Lookup(_c.HouseAreas, house.ToString(), "this area of life")}.")
                : "";
            string retro = p.IsRetrograde
                ? " " + Lookup(_c.Retrogrades, p.PlanetName, "Retrograde here, its lessons turn inward before they express outward.")
                : "";

            // Prefer a bespoke Planet-in-Sign line; fall back to the templated blend.
            string core;
            if (_c.PlanetInSign.TryGetValue($"{p.PlanetName}|{p.Sign.Name()}", out var bespoke)
                && !string.IsNullOrWhiteSpace(bespoke))
            {
                core = $"{bespoke.TrimEnd('.')}.{inHouse}";
            }
            else
            {
                string theme = Lookup(_c.PlanetThemes, p.PlanetName, "this energy");
                string style = Lookup(_c.SignStyles, p.Sign.Name(), "in its own way");
                core = $"{Capitalise(theme)} expressed {style}.{inHouse}";
            }

            string pos = ZodiacSignExtensions.FormatDegreeInSign(p.Longitude);
            string where = chart.Timed ? $" ({Ordinal(house)} house)" : "";
            paras.Add($"{p.PlanetSymbol} {p.PlanetName} in {p.Sign.Name()} {pos}{where}: {core}{retro}");
        }
        return new ReportSection { Heading = "The Planets", Paragraphs = paras };
    }

    private ReportSection Aspects(NatalChart chart)
    {
        var paras = new List<string>();
        if (chart.Aspects.Count == 0 && chart.AngleAspects.Count == 0)
        {
            paras.Add("No major aspects fall within orb — the planetary energies operate fairly independently.");
            return new ReportSection { Heading = "Major Aspects", Paragraphs = paras };
        }

        // Planet to planet, and planet to the Ascendant or Midheaven, closest first.
        string SignOf(NatalPoint p) => ZodiacSignExtensions.FromLongitude(chart.LongitudeOf(p) ?? 0).Name();
        var all = chart.Aspects.Select(a => (A: NatalPoint.Of(a.PlanetA), B: NatalPoint.Of(a.PlanetB), a.Type, a.Orb, a.OutOfSign))
            .Concat(chart.AngleAspects.Select(a => (A: NatalPoint.Of(a.Planet), B: a.Angle, a.Type, a.Orb, a.OutOfSign)));
        foreach (var a in all.OrderBy(a => a.Orb))
        {
            string dynamic = Lookup(_c.AspectDynamics, a.Type.ToString(), "connects with");
            string line = $"{Named(a.A)} {dynamic} {Named(a.B)} ({a.Type.Name()} {a.Type.Symbol()}, orb {a.Orb:F1}°).";
            string text = AspectText(a.A, a.B, a.Type);
            if (text.Length > 0) line += " " + text;
            if (a.OutOfSign)
                line += $" Out of sign: the two are in {SignOf(a.A)} and {SignOf(a.B)}, which are not in this aspect to each other, so tradition reads it as weaker.";
            paras.Add(line);
        }
        return new ReportSection { Heading = "Major Aspects", Paragraphs = paras };
    }

    // What an aspect means for this particular pair. A major aspect gets the bespoke line
    // for the two points and its tone if the corpus has one; failing that, a plainer
    // sentence assembled from the building blocks. Minor aspects, and anything the blocks
    // can't cover, get the general note for that kind of aspect.
    private string AspectText(NatalPoint a, NatalPoint b, AspectType type)
    {
        string note = Lookup(_c.AspectNotes, type.ToString(), "");
        if (!type.IsMajor()) return note;

        // Corpus keys name the two points in standard order.
        var (lo, hi) = Order(b) < Order(a) ? (b, a) : (a, b);
        if (_c.Aspects.TryGetValue($"{lo.Name}|{type.Tone()}|{hi.Name}", out var bespoke) && !string.IsNullOrWhiteSpace(bespoke))
            return bespoke;

        string first = Theme(a), second = Theme(b), link = Lookup(_c.AspectToneLinks, type.Tone().ToString(), "");
        if (first.Length == 0 || second.Length == 0 || link.Length == 0) return note;
        return $"{Capitalise(first)} {link} {second}. {note}".TrimEnd();
    }

    private string Theme(NatalPoint p) =>
        Lookup(p.IsAngle ? _c.AngleThemes : _c.PlanetThemes, p.Name, "");

    // Sun, Moon … Lilith, then the Ascendant and Midheaven.
    private static int Order(NatalPoint p) => p.Kind switch
    {
        NatalPointKind.Ascendant => 13,
        NatalPointKind.Midheaven => 14,
        _ => (int)p.Body
    };

    private ReportSection Patterns(NatalChart chart)
    {
        var patterns = AspectPatternService.Detect(chart);
        var paras = new List<string>();

        if (patterns.Count == 0)
        {
            paras.Add("No major configuration (stellium, grand trine, T-square, grand cross, or yod) " +
                      "stands out — the aspects act more as individual links than a single locked figure.");
            return new ReportSection { Heading = "Chart Patterns", Paragraphs = paras };
        }

        foreach (var p in patterns)
        {
            string names = JoinNames(p.Points);
            switch (p.Type)
            {
                case PatternType.Stellium:
                    paras.Add($"Stellium in {p.Sign!.Value.Name()} — {p.Points.Count} bodies " +
                              $"({names}) gather in one sign, concentrating its themes into a dominant focus of the chart." +
                              More(_c.StelliumSigns, p.Sign.Value.Name()));
                    break;
                case PatternType.GrandTrine:
                    paras.Add($"Grand Trine in {p.Element} — {names} form a closed triangle of trines, " +
                              "an easy, self-reinforcing circuit of talent that flows so naturally it can be taken for granted." +
                              More(_c.GrandTrines, p.Element?.ToString()));
                    break;
                case PatternType.TSquare:
                    var ends = p.Points.Where(x => x != p.Apex).Select(Named);
                    paras.Add($"T-Square in {p.Modality} signs — {Named(p.Apex!.Value)} stands at the apex, " +
                              $"squaring the opposition between {string.Join(" and ", ends)}. " +
                              "A focal point of dynamic tension that pushes hard toward action and achievement." +
                              More(_c.TSquares, p.Modality?.ToString()) + More(_c.ApexPoints, p.Apex.Value.Name));
                    break;
                case PatternType.Yod:
                    var feet = p.Points.Where(x => x != p.Apex).Select(Named);
                    paras.Add($"Yod — {string.Join(" and ", feet)}, in sextile, both stand quincunx to {Named(p.Apex!.Value)}. " +
                              "Sometimes called the Finger of God: a point of persistent adjustment, where two compatible " +
                              "drives keep pressing on a third that fits neither." +
                              More(_c.ApexPoints, p.Apex.Value.Name));
                    break;
                case PatternType.GrandCross:
                    paras.Add($"Grand Cross in {p.Modality} signs — {names} form two oppositions locked by four squares, " +
                              "a demanding but powerful figure that seeks balance on all four fronts." +
                              More(_c.GrandCrosses, p.Modality?.ToString()));
                    break;
            }
        }
        return new ReportSection { Heading = "Chart Patterns", Paragraphs = paras };
    }

    // "Mars", but "the Ascendant".
    private static string Named(NatalPoint p) => p.IsAngle ? "the " + p.Name : p.Name;

    private static string JoinNames(IReadOnlyList<NatalPoint> ps)
    {
        var names = ps.Select(Named).ToList();
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1]
        };
    }

    private ReportSection Balance(NatalChart chart)
    {
        var counts = new Dictionary<Element, int>
        {
            [Element.Fire] = 0, [Element.Earth] = 0, [Element.Air] = 0, [Element.Water] = 0
        };
        // Weight the ten traditional bodies plus the two points already in Planets.
        foreach (var p in chart.Planets)
            counts[p.Sign.GetElement()]++;

        var paras = Spread(counts, "elementsEven", _c.Elements, _c.ElementStrong, _c.ElementWeak,
            e => e.ToString(),
            e => $"There is little or no {e}, which may be an area that needs conscious cultivation.");
        if (paras.Count > 0)
            paras.Add($"Element tally — Fire: {counts[Element.Fire]}, Earth: {counts[Element.Earth]}, " +
                      $"Air: {counts[Element.Air]}, Water: {counts[Element.Water]}.");
        return new ReportSection { Heading = "Elemental Balance", Paragraphs = paras };
    }

    // The paragraphs for one tally (elements or modalities): what the chart leans toward —
    // more than one where they tie — then every one that is empty. An even spread gets a
    // single note instead.
    private List<string> Spread<T>(Dictionary<T, int> counts, string evenNote,
        Dictionary<string, string> labels, Dictionary<string, string> strong, Dictionary<string, string> weak,
        Func<T, string> labelFallback, Func<T, string> weakFallback) where T : notnull
    {
        var paras = new List<string>();
        int total = counts.Values.Sum();
        if (total == 0) return paras;

        int max = counts.Values.Max(), min = counts.Values.Min();
        string even = Lookup(_c.BalanceNotes, evenNote, "");
        if (max - min <= 1 && even.Length > 0)
        {
            paras.Add(even);
            return paras;
        }

        var top = counts.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
        foreach (var key in top)
        {
            string lead = top.Count == 1 ? "The chart leans toward" : paras.Count == 0 ? "The chart leans equally toward" : "And toward";
            paras.Add($"{lead} {key} ({max} of {total} bodies) — {Lookup(labels, key.ToString()!, labelFallback(key))}." +
                      More(strong, key.ToString()));
        }
        foreach (var key in counts.Where(kv => kv.Value == 0).Select(kv => kv.Key))
            paras.Add(Lookup(weak, key.ToString()!, weakFallback(key)));
        return paras;
    }

    private ReportSection ModalBalance(NatalChart chart)
    {
        var counts = new Dictionary<Modality, int>
        {
            [Modality.Cardinal] = 0, [Modality.Fixed] = 0, [Modality.Mutable] = 0
        };
        foreach (var p in chart.Planets)
            counts[p.Sign.GetModality()]++;

        var paras = Spread(counts, "modalitiesEven", _c.Modalities, _c.ModalityStrong, _c.ModalityWeak,
            ModalityFallback,
            m => $"There is little or no {m} energy, which may be an area that needs conscious cultivation.");
        if (paras.Count > 0)
            paras.Add($"Modality tally — Cardinal: {counts[Modality.Cardinal]}, " +
                      $"Fixed: {counts[Modality.Fixed]}, Mutable: {counts[Modality.Mutable]}.");
        return new ReportSection { Heading = "Modal Balance", Paragraphs = paras };
    }

    private static string ModalityFallback(Modality m) => m switch
    {
        Modality.Cardinal => "an initiating temperament — active, enterprising, and quick to begin",
        Modality.Fixed => "a steadfast temperament — persistent, determined, and resistant to change",
        _ => "an adaptable temperament — flexible, versatile, and at ease with change"
    };

    // The authored paragraph for this sign in this role if the corpus has one; otherwise
    // the one-line framing of the sign's traits.
    private string Frame(string role, Dictionary<string, string> paragraphs, string name, ZodiacSign sign)
    {
        string text = Lookup(paragraphs, sign.Name(), "");
        if (text.Length == 0)
        {
            string trait = Lookup(_c.SignTraits, sign.Name(), "distinctive in their own way");
            text = Lookup(_c.RoleFraming, role, "{name} carries {trait}.").Replace("{trait}", trait);
        }
        return text.Replace("{name}", name) + $" ({role} in {sign.Name()}.)";
    }

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    // A further sentence from the corpus, with its leading space, or nothing.
    private static string More(Dictionary<string, string> map, string? key) =>
        key is not null && map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? " " + v : "";

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string FirstName(string full)
    {
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : full;
    }

    internal static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", 21 => "21st", 22 => "22nd", 23 => "23rd",
        _ => $"{n}th"
    };
}
