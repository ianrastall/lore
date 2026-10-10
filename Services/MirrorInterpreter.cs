using Lore.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lore.Services;

// Writes the reading that sets a person's inventory profile beside their chart, from an
// editable corpus (Data\mirror.json). Two parts:
//
//   • how each planet is being lived — the facets that measure the nearest thing to what
//     a planet stands for are grouped into "dials", each low, mid or high, and the
//     pattern of the dials picks one of a few named expressions for that planet;
//   • where the chart and the answers part company — the chart's expectation is the
//     planet's dignity (DignityService: a well-placed planet "works easily", a badly
//     placed one "with strain") and the answers supply how it is lived; the two cases
//     where they disagree are the ones written up.
//
// The chart is never altered and no number is taken from one side into the other. The
// pairing of planets with facets is Lore's own invention and the reading says so.
// Deterministic and offline: every sentence is from the corpus.
public sealed class MirrorInterpreter
{
    private sealed class Corpus
    {
        public Dictionary<string, string> Notes { get; init; } = new();
        public Dictionary<string, string> Comparison { get; init; } = new();
        public Dictionary<string, PlanetEntry> Planets { get; init; } = new();
    }

    private sealed class PlanetEntry
    {
        public string Stands { get; init; } = "";
        // Dial name → facet keys; a leading minus counts the facet the other way up.
        public Dictionary<string, List<string>> Dials { get; init; } = new();
        public List<Expression> Expressions { get; init; } = [];
        public string? Unlived { get; init; }
        public string? HardWon { get; init; }
        // Lines for a transit to this planet: lived (strain | well) → kind (hard | easy).
        public Dictionary<string, Dictionary<string, string>> Daily { get; init; } = new();
    }

    private sealed class Expression
    {
        public string Name { get; init; } = "";
        public Dictionary<string, string> When { get; init; } = new();   // dial → low | mid | high
        public string Lived { get; init; } = "plain";                    // well | strain | plain
        public string? Report { get; init; }                             // added to the Report's paragraph
        public string Text { get; init; } = "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // The planets read, in order.
    private static readonly Planet[] Read =
        [Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars, Planet.Jupiter, Planet.Saturn, Planet.Neptune];

    // A dial is high or low when its facets average more than this many standard
    // deviations from the reference group's mean. Less than the one a single facet
    // needs, because an average of several facets strays less far than any one of them.
    public const double DialThreshold = 0.5;

    // A planet "works easily" in the chart at this dignity or above, and "with strain"
    // at StrainedAt or below: roughly the top and bottom thirds of every planet in the
    // timed figures of the library.
    public const int EasyAt = 5, StrainedAt = -1;

    private readonly Corpus _c;
    private readonly ChartInterpreter? _report;

    // `report`: the natal interpreter, for the Report's own words on a planet in its sign.
    public MirrorInterpreter(string mirrorJsonPath, ChartInterpreter? report = null)
    {
        _report = report;
        if (File.Exists(mirrorJsonPath))
        {
            using var stream = File.OpenRead(mirrorJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else
        {
            _c = new Corpus();
        }
    }

    public bool IsAvailable => _c.Planets.Count > 0;

    public MirrorReading Compose(NatalChart chart, InventoryProfile profile)
    {
        var facets = profile.Domains.SelectMany(d => d.Facets).ToDictionary(f => f.Facet.Key);
        var dignity = DignityService.ComputeIfTimed(chart);
        string name = chart.Celebrity.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? chart.Celebrity.Name;

        var planets = new List<MirrorPlanet>();
        foreach (var planet in Read)
        {
            if (chart.GetPlanet(planet) is not { } position ||
                !_c.Planets.TryGetValue(planet.Name(), out var entry)) continue;

            var levels = entry.Dials.ToDictionary(d => d.Key, d => LevelOf(d.Value, facets));
            var expression = entry.Expressions.FirstOrDefault(e =>
                e.When.All(w => levels.TryGetValue(w.Key, out var level) && level == w.Value));
            if (expression is null) continue;

            int? score = dignity?.Planets.FirstOrDefault(p => p.Planet == planet)?.Total;
            planets.Add(new MirrorPlanet(planet, position, expression.Name, ParseLived(expression.Lived), score,
                score is null ? MirrorExpectation.Unknown
                : score >= EasyAt ? MirrorExpectation.Easy
                : score <= StrainedAt ? MirrorExpectation.Strained : MirrorExpectation.Middling)
            {
                Text = expression.Text,
                Facets = entry.Dials.Values.SelectMany(keys => keys)
                    .Select(k => facets.GetValueOrDefault(k.TrimStart('-'))).OfType<FacetScore>().ToList(),
                Stands = entry.Stands,
                DignityNotes = dignity?.Planets.FirstOrDefault(p => p.Planet == planet)?.Notes ?? "",
                ReportLine = (expression.Report ?? "").Replace("{name}", name),
                DailyHard = entry.Daily.GetValueOrDefault(expression.Lived)?.GetValueOrDefault("hard") ?? "",
                DailyEasy = entry.Daily.GetValueOrDefault(expression.Lived)?.GetValueOrDefault("easy") ?? "",
            });
        }

        var sections = new List<DailySection> { Opening(chart), Lived(chart, planets) };
        if (chart.Timed) sections.Add(Compared(planets));
        sections.Add(Closing());
        sections.RemoveAll(s => s.Items.Count == 0);

        return new MirrorReading
        {
            ChartId = chart.Celebrity.Id,
            ReportParagraphs = ForReport(chart, planets, name, profile.TakenOn),
            Name = chart.Celebrity.Name,
            TakenOn = profile.TakenOn,
            Planets = planets,
            Sections = sections,
        };
    }

    // ── Dials ─────────────────────────────────────────────────────────────────

    private static string LevelOf(List<string> keys, Dictionary<string, FacetScore> facets)
    {
        var values = keys.Select(k => facets.TryGetValue(k.TrimStart('-'), out var f)
            ? (k.StartsWith('-') ? -f.Z : f.Z) : (double?)null).OfType<double>().ToList();
        if (values.Count == 0) return "mid";
        // A dial of one facet is held to the line the profile itself draws between
        // typical and low or high, so that the two never say different things of it.
        double mean = values.Average(), threshold = values.Count == 1 ? 1.0 : DialThreshold;
        return mean > threshold ? "high" : mean < -threshold ? "low" : "mid";
    }

    private static MirrorLived ParseLived(string lived) => lived switch
    {
        "well" => MirrorLived.Well,
        "strain" => MirrorLived.Strain,
        _ => MirrorLived.Plain,
    };

    // ── Sections ──────────────────────────────────────────────────────────────

    private DailySection Opening(NatalChart chart)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Note("intro") },
            new() { Text = Note("invented") },
            new() { Text = Note("covered") },
        };
        if (!chart.Timed) items.Add(new DailyItem { Text = Note("untimed") });
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "The chart and the answers", Items = items };
    }

    // For each planet: what the chart says of it, then what the answers say.
    private DailySection Lived(NatalChart chart, List<MirrorPlanet> planets) => new()
    {
        Heading = "How each planet is being lived",
        Items = planets.Select(p =>
        {
            string where = $"{p.Planet.Name()} in {p.Position.Sign.Name()}" +
                           (chart.Timed ? $", {ChartInterpreter.Ordinal(chart.GetHouseForLongitude(p.Position.Longitude))} house" : "");
            string report = _report?.PlanetInSign(p.Position) ?? "";
            return new DailyItem
            {
                Title = $"{p.Planet.Symbol()} {p.Planet.Name()}: {p.Expression}",
                Meta = string.Join("  ·  ", p.Facets.Select(f => $"{f.Name} {f.BandLabel.ToLowerInvariant()}")),
                Text = $"The chart: {where}, the planet of {p.Stands}." + (report.Length > 0 ? $" {report}." : "") +
                       $"\nThe answers: {p.Text}",
            };
        }).ToList(),
    };

    // The seven traditional planets, by whether the chart's expectation and the answers agree.
    private DailySection Compared(List<MirrorPlanet> planets)
    {
        string Names(IEnumerable<MirrorPlanet> list) => string.Join(", ", list.Select(p => $"{p.Planet.Symbol()} {p.Planet.Name()}"));
        string Word(string key, string fallback) => _c.Comparison.GetValueOrDefault(key, fallback);

        var scored = planets.Where(p => p.Expectation != MirrorExpectation.Unknown).ToList();
        var items = new List<DailyItem>();

        foreach (var p in scored.Where(p => p.Verdict is MirrorVerdict.Unlived or MirrorVerdict.HardWon))
        {
            var entry = _c.Planets[p.Planet.Name()];
            string? text = p.Verdict == MirrorVerdict.Unlived ? entry.Unlived : entry.HardWon;
            if (string.IsNullOrWhiteSpace(text)) continue;
            items.Add(new DailyItem
            {
                Title = $"{p.Planet.Symbol()} {p.Planet.Name()}: {(p.Verdict == MirrorVerdict.Unlived ? Word("unlived", "unlived") : Word("hardWon", "hard-won"))}",
                Meta = $"In the chart it {(p.Expectation == MirrorExpectation.Easy ? Word("easy", "works easily") : Word("strained", "works with strain"))} " +
                       $"(dignity {p.Dignity:+#;−#;0}: {p.DignityNotes})  ·  in the answers, {p.Expression}",
                Text = text,
            });
        }
        if (items.Count == 0) items.Add(new DailyItem { Text = Note("none") });

        void Group(string title, MirrorVerdict verdict, string note)
        {
            var list = scored.Where(p => p.Verdict == verdict).ToList();
            if (list.Count > 0 && Note(note).Length > 0)
                items.Add(new DailyItem { Title = title, Meta = Names(list), Text = Note(note) });
        }
        Group("As the chart has it", MirrorVerdict.AsWritten, "asWritten");
        Group("As the chart has it, the hard way", MirrorVerdict.AsWrittenHard, "asWrittenHard");
        Group("Nothing to set against anything", MirrorVerdict.NoContrast, "noContrast");

        return new DailySection { Heading = "Where the chart and the answers part company", Items = items };
    }

    // What the Report says at its end when the answers have shaped it.
    private List<string> ForReport(NatalChart chart, List<MirrorPlanet> planets, string name, DateOnly takenOn)
    {
        var paragraphs = new List<string>
        {
            Note("reportIntro").Replace("{name}", name)
                .Replace("{date}", takenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)),
        };
        if (chart.Timed)
        {
            var differing = planets.Where(p => p.Verdict is MirrorVerdict.Unlived or MirrorVerdict.HardWon).ToList();
            foreach (var p in differing)
            {
                var entry = _c.Planets[p.Planet.Name()];
                string? text = p.Verdict == MirrorVerdict.Unlived ? entry.Unlived : entry.HardWon;
                if (!string.IsNullOrWhiteSpace(text))
                    paragraphs.Add($"{p.Planet.Symbol()} {p.Planet.Name()}, " +
                                   $"{(p.Verdict == MirrorVerdict.Unlived ? _c.Comparison.GetValueOrDefault("unlived", "unlived") : _c.Comparison.GetValueOrDefault("hardWon", "hard-won"))}: {text}");
            }
            if (differing.Count == 0) paragraphs.Add(Note("reportNone"));
        }
        paragraphs.RemoveAll(string.IsNullOrWhiteSpace);
        return paragraphs;
    }

    private DailySection Closing() => new()
    {
        Heading = "Reading the difference",
        Items = Note("closing").Length == 0 ? [] : [new DailyItem { Text = Note("closing") }],
    };

    private string Note(string key) => _c.Notes.GetValueOrDefault(key, "");

    // The reading as plain text, for saving or pasting into notes.
    public static byte[] ToText(MirrorReading r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Name} — The chart and the answers");
        sb.AppendLine($"From the personality inventory answered on {r.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}");
        foreach (var section in r.Sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.Heading.ToUpperInvariant());
            foreach (var item in section.Items)
            {
                sb.AppendLine();
                if (item.HasTitle) sb.AppendLine(item.Title);
                if (item.HasMeta) sb.AppendLine($"({item.Meta})");
                sb.AppendLine(item.Text);
            }
        }
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Generated by Lore on {DateTime.Now:yyyy-MM-dd}."));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
