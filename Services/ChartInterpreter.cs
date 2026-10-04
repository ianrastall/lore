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
            paras.Add(Frame("Sun", name, sun.Sign));
        bool moonUnsure = !chart.Timed && moonSigns is { Count: > 1 };
        if (moonUnsure)
            paras.Add($"The Moon changed sign on the day {name} was born — it was in " +
                      $"{string.Join(" or ", moonSigns!.Select(s => s.Name()))} depending on the hour — so without a " +
                      "birth time the Moon sign can't be given. The Moon line under The Planets below is for noon.");
        else if (moon is not null)
            paras.Add(Frame("Moon", name, moon.Sign));
        if (chart.Timed)
            paras.Add(Frame("Rising", name, risingSign));
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
            string colouring = chart.Timed
                ? $", colouring {Lookup(_c.HouseAreas, house.ToString(), "this area of life")}."
                : ".";
            string retro = p.IsRetrograde ? " Retrograde here, its lessons turn inward before they express outward." : "";

            // Prefer a bespoke Planet-in-Sign line; fall back to the templated blend.
            string core;
            if (_c.PlanetInSign.TryGetValue($"{p.PlanetName}|{p.Sign.Name()}", out var bespoke)
                && !string.IsNullOrWhiteSpace(bespoke))
            {
                core = $"{bespoke.TrimEnd('.')}{colouring}";
            }
            else
            {
                string theme = Lookup(_c.PlanetThemes, p.PlanetName, "this energy");
                string style = Lookup(_c.SignStyles, p.Sign.Name(), "in its own way");
                core = $"{theme} expressed {style}{colouring}";
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
        var all = chart.Aspects.Select(a => (First: a.PlanetA.Name(), Second: a.PlanetB.Name(), a.Type, a.Orb, a.OutOfSign,
                Signs: $"{SignOf(NatalPoint.Of(a.PlanetA))} and {SignOf(NatalPoint.Of(a.PlanetB))}"))
            .Concat(chart.AngleAspects.Select(a => (First: a.Planet.Name(), Second: "the " + a.Angle.Name, a.Type, a.Orb, a.OutOfSign,
                Signs: $"{SignOf(NatalPoint.Of(a.Planet))} and {SignOf(a.Angle)}")));
        foreach (var a in all.OrderBy(a => a.Orb))
        {
            string dynamic = Lookup(_c.AspectDynamics, a.Type.ToString(), "connects with");
            string note = Lookup(_c.AspectNotes, a.Type.ToString(), "");
            string line = $"{a.First} {dynamic} {a.Second} ({a.Type.Name()} {a.Type.Symbol()}, orb {a.Orb:F1}°).";
            if (!string.IsNullOrEmpty(note)) line += " " + note;
            if (a.OutOfSign)
                line += $" Out of sign: the two are in {a.Signs}, which are not in this aspect to each other, so tradition reads it as weaker.";
            paras.Add(line);
        }
        return new ReportSection { Heading = "Major Aspects", Paragraphs = paras };
    }

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
                              $"({names}) gather in one sign, concentrating its themes into a dominant focus of the chart.");
                    break;
                case PatternType.GrandTrine:
                    paras.Add($"Grand Trine in {p.Element} — {names} form a closed triangle of trines, " +
                              "an easy, self-reinforcing circuit of talent that flows so naturally it can be taken for granted.");
                    break;
                case PatternType.TSquare:
                    var ends = p.Points.Where(x => x != p.Apex).Select(Named);
                    paras.Add($"T-Square in {p.Modality} signs — {Named(p.Apex!.Value)} stands at the apex, " +
                              $"squaring the opposition between {string.Join(" and ", ends)}. " +
                              "A focal point of dynamic tension that pushes hard toward action and achievement.");
                    break;
                case PatternType.Yod:
                    var feet = p.Points.Where(x => x != p.Apex).Select(Named);
                    paras.Add($"Yod — {string.Join(" and ", feet)}, in sextile, both stand quincunx to {Named(p.Apex!.Value)}. " +
                              "Sometimes called the Finger of God: a point of persistent adjustment, where two compatible " +
                              "drives keep pressing on a third that fits neither.");
                    break;
                case PatternType.GrandCross:
                    paras.Add($"Grand Cross in {p.Modality} signs — {names} form two oppositions locked by four squares, " +
                              "a demanding but powerful figure that seeks balance on all four fronts.");
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

        int total = counts.Values.Sum();
        var paras = new List<string>();
        if (total > 0)
        {
            var dominant = counts.OrderByDescending(kv => kv.Value).First();
            var lacking = counts.OrderBy(kv => kv.Value).First();

            string domText = Lookup(_c.Elements, dominant.Key.ToString(), dominant.Key.ToString());
            paras.Add($"The chart leans toward {dominant.Key} ({dominant.Value} of {total} bodies) — {domText}.");

            if (lacking.Value == 0)
                paras.Add($"There is little or no {lacking.Key}, which may be an area that needs conscious cultivation.");

            paras.Add($"Element tally — Fire: {counts[Element.Fire]}, Earth: {counts[Element.Earth]}, " +
                      $"Air: {counts[Element.Air]}, Water: {counts[Element.Water]}.");
        }
        return new ReportSection { Heading = "Elemental Balance", Paragraphs = paras };
    }

    private ReportSection ModalBalance(NatalChart chart)
    {
        var counts = new Dictionary<Modality, int>
        {
            [Modality.Cardinal] = 0, [Modality.Fixed] = 0, [Modality.Mutable] = 0
        };
        foreach (var p in chart.Planets)
            counts[p.Sign.GetModality()]++;

        int total = counts.Values.Sum();
        var paras = new List<string>();
        if (total > 0)
        {
            var dominant = counts.OrderByDescending(kv => kv.Value).First();
            var lacking = counts.OrderBy(kv => kv.Value).First();

            string domText = Lookup(_c.Modalities, dominant.Key.ToString(), ModalityFallback(dominant.Key));
            paras.Add($"The chart leans toward {dominant.Key} ({dominant.Value} of {total} bodies) — {domText}.");

            if (lacking.Value == 0)
                paras.Add($"There is little or no {lacking.Key} energy, which may be an area that needs conscious cultivation.");

            paras.Add($"Modality tally — Cardinal: {counts[Modality.Cardinal]}, " +
                      $"Fixed: {counts[Modality.Fixed]}, Mutable: {counts[Modality.Mutable]}.");
        }
        return new ReportSection { Heading = "Modal Balance", Paragraphs = paras };
    }

    private static string ModalityFallback(Modality m) => m switch
    {
        Modality.Cardinal => "an initiating temperament — active, enterprising, and quick to begin",
        Modality.Fixed => "a steadfast temperament — persistent, determined, and resistant to change",
        _ => "an adaptable temperament — flexible, versatile, and at ease with change"
    };

    private string Frame(string role, string name, ZodiacSign sign)
    {
        string trait = Lookup(_c.SignTraits, sign.Name(), "distinctive in their own way");
        string framing = Lookup(_c.RoleFraming, role, "{name} carries {trait}.");
        return framing.Replace("{name}", name).Replace("{trait}", trait)
               + $" ({role} in {sign.Name()}.)";
    }

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

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
