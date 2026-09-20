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
        public Dictionary<string, string> HouseAreas { get; init; } = new();
        public Dictionary<string, string> AspectDynamics { get; init; } = new();
        public Dictionary<string, string> AspectNotes { get; init; } = new();
        public Dictionary<string, string> Elements { get; init; } = new();
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

    public IReadOnlyList<ReportSection> Interpret(NatalChart chart)
    {
        var sections = new List<ReportSection>
        {
            Overview(chart),
            Planets(chart),
            Aspects(chart),
            Balance(chart),
        };
        return sections;
    }

    private ReportSection Overview(NatalChart chart)
    {
        string name = FirstName(chart.Celebrity.Name);
        var paras = new List<string>();

        var sun = chart.GetPlanet(Planet.Sun);
        var moon = chart.GetPlanet(Planet.Moon);
        var risingSign = ZodiacSignExtensions.FromLongitude(chart.Ascendant);

        if (sun is not null)
            paras.Add(Frame("Sun", name, sun.Sign));
        if (moon is not null)
            paras.Add(Frame("Moon", name, moon.Sign));
        paras.Add(Frame("Rising", name, risingSign));

        if (!chart.Celebrity.BirthTimeKnown)
            paras.Add("Note: the birth time is unknown, so the chart uses noon. The Ascendant, " +
                      "house placements, and the Moon's exact degree may not be reliable.");

        return new ReportSection { Heading = "Overview", Paragraphs = paras };
    }

    private ReportSection Planets(NatalChart chart)
    {
        var paras = new List<string>();
        foreach (var p in chart.Planets)
        {
            string theme = Lookup(_c.PlanetThemes, p.PlanetName, "this energy");
            string style = Lookup(_c.SignStyles, p.Sign.Name(), "in its own way");
            int house = chart.GetHouseForLongitude(p.Longitude);
            string area = Lookup(_c.HouseAreas, house.ToString(), "this area of life");
            string retro = p.IsRetrograde ? " Retrograde here, its lessons turn inward before they express outward." : "";

            paras.Add($"{p.PlanetSymbol} {p.PlanetName} in {p.Sign.Name()} ({Ordinal(house)} house): " +
                      $"{theme} expressed {style}, colouring {area}.{retro}");
        }
        return new ReportSection { Heading = "The Planets", Paragraphs = paras };
    }

    private ReportSection Aspects(NatalChart chart)
    {
        var paras = new List<string>();
        if (chart.Aspects.Count == 0)
        {
            paras.Add("No major aspects fall within orb — the planetary energies operate fairly independently.");
            return new ReportSection { Heading = "Major Aspects", Paragraphs = paras };
        }

        foreach (var a in chart.Aspects.OrderBy(a => a.Orb))
        {
            string dynamic = Lookup(_c.AspectDynamics, a.Type.ToString(), "connects with");
            string note = Lookup(_c.AspectNotes, a.Type.ToString(), "");
            string line = $"{a.PlanetA.Name()} {dynamic} {a.PlanetB.Name()} ({a.Type} {a.Type.Symbol()}, orb {a.Orb:F1}°).";
            if (!string.IsNullOrEmpty(note)) line += " " + note;
            paras.Add(line);
        }
        return new ReportSection { Heading = "Major Aspects", Paragraphs = paras };
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

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", 21 => "21st", 22 => "22nd", 23 => "23rd",
        _ => $"{n}th"
    };
}
