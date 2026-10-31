using Lore.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lore.Services;

// Everything Lore says that depends on a person's answers to the personality inventory,
// written from an editable corpus (Data\mirror.json): the Chart and answers reading, and
// the sentences the Report, the Daily and the Forecast take from it.
//
// One idea runs through all of it: the chart supplies an expectation and the answers a
// measurement, and the reading says where the two agree and where they part.
//
//   • Each planet read has "dials", groups of inventory facets that measure the nearest
//     thing to what it stands for, each low, mid or high. Their pattern picks one of a
//     few named expressions: how the planet is being lived.
//   • The sign a planet is in says what level of each dial to expect (a Mars in Aries,
//     push; a Mars in Libra, restraint). Each dial is compared with that.
//   • The planet's dignity says whether it should work easily or with strain, and that
//     is compared with whether the expression counts as lived well or with strain.
//   • An aspect of the birth chart is read by how each of its two ends is lived, and
//     the chart's balance of elements and modes against groups of facets of its own.
//
//   • The character scales say how far a planet has been grown into: its higher side, its
//     lower side, or neither. They alone read Pluto, the North Node, Chiron and Lilith.
//   • A single facet at the very end of the profile is spoken of even where its dial's
//     average hides it, and a planet lived well or with strain is read in its house.
//   • The life-area ratings mark out houses, which the Daily, Forecast and Timing use.
//
// The chart is never altered. The pairing of chart factors with facets is Lore's own
// invention, and the reading says so. Deterministic and offline: the rules and every
// sentence are in the corpus.
public sealed class MirrorInterpreter
{
    private sealed class Corpus
    {
        public Dictionary<string, string> Notes { get; init; } = new();
        public Dictionary<string, string> Comparison { get; init; } = new();
        public Dictionary<string, PlanetEntry> Planets { get; init; } = new();
        public RisingEntry? Rising { get; init; }
        public CompareWords Compare { get; init; } = new();
        // Tone → ("strain|well", "strain", …) → sentence.
        public Dictionary<string, Dictionary<string, string>> Aspects { get; init; } = new();
        public Dictionary<string, BalanceEntry> Elements { get; init; } = new();
        public Dictionary<string, BalanceEntry> Modalities { get; init; } = new();
        // How the planet is lived (strain | well) → kind of transit (hard | easy) → sentence.
        public Dictionary<string, Dictionary<string, string>> Forecast { get; init; } = new();
        // Facet key → its two extremes.
        public Dictionary<string, Noun> Extremes { get; init; } = new();
        // Pattern type → strain | well → sentence.
        public Dictionary<string, Dictionary<string, string>> Patterns { get; init; } = new();
        public TimingWords Timing { get; init; } = new();
    }

    private sealed class TimingWords
    {
        // How the Lord of the Year is lived (strain | well) → sentence.
        public Dictionary<string, string> Lord { get; init; } = new();
    }

    private sealed class PlanetEntry
    {
        public string Stands { get; init; } = "";
        // Dial name → facet keys; a leading minus counts the facet the other way up.
        public Dictionary<string, List<string>> Dials { get; init; } = new();
        public Dictionary<string, Noun> Nouns { get; init; } = new();
        // Sign name → dial → the level the sign leads one to expect.
        public Dictionary<string, Dictionary<string, string>> Signs { get; init; } = new();
        public List<Expression> Expressions { get; init; } = [];
        public List<Expression> Also { get; init; } = [];
        public string? Unlived { get; init; }
        public string? HardWon { get; init; }
        // Character scales saying how far the planet has been grown into, and the words.
        public List<string> Reach { get; init; } = [];
        public string? Higher { get; init; }
        public string? Lower { get; init; }
        // House → strain | well → sentence.
        public Dictionary<string, Dictionary<string, string>> Houses { get; init; } = new();
    }

    private sealed class Expression
    {
        public string Name { get; init; } = "";
        public Dictionary<string, string> When { get; init; } = new();   // dial → low | mid | high
        public string Lived { get; init; } = "plain";                    // well | strain | plain
        public string? Report { get; init; }                             // added to the Report
        public string? Hard { get; init; }                               // added to a hard transit to the planet
        public string? Easy { get; init; }                               // and to an easy one
        public string Text { get; init; } = "";
    }

    private sealed class Noun
    {
        public string High { get; init; } = "";
        public string Low { get; init; } = "";
    }

    private sealed class RisingEntry
    {
        public List<string> Dial { get; init; } = [];
        public Noun Nouns { get; init; } = new();
        public Dictionary<string, string> Signs { get; init; } = new();
    }

    private sealed class CompareWords
    {
        public string Above { get; init; } = "{far}more {noun} than {body} in {sign} would suggest";
        public string Below { get; init; } = "{far}less {noun} than {body} in {sign} would suggest";
        public string Match { get; init; } = "the {noun} that {body} in {sign} describes";
        public string Far { get; init; } = "far ";
        public Dictionary<string, string> After { get; init; } = new();
        public Dictionary<string, string> Alone { get; init; } = new();
        public string Also { get; init; } = "There is also {clause}.";
        public string InReading { get; init; } = "Against the sign: {clauses}.";
        public string AsExpected { get; init; } = "";
    }

    private sealed class BalanceEntry
    {
        public List<string> Dial { get; init; } = [];
        public string StrongHigh { get; init; } = "";
        public string StrongLow { get; init; } = "";
        public string WeakHigh { get; init; } = "";
        public string WeakLow { get; init; } = "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // The planets read, in order.
    private static readonly Planet[] Read =
    [
        Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars,
        Planet.Jupiter, Planet.Saturn, Planet.Uranus, Planet.Neptune,
        Planet.Pluto, Planet.NorthNode, Planet.Chiron, Planet.Lilith,
    ];

    // A dial is high or low when its facets average more than this many standard
    // deviations from the reference group's mean. Less than the one a single facet
    // needs, because an average of several facets strays less far than any one of them.
    public const double DialThreshold = 0.5;

    // A planet "works easily" in the chart at this dignity or above, and "with strain"
    // at StrainedAt or below: roughly the top and bottom thirds of every planet in the
    // timed figures of the library.
    public const int EasyAt = 5, StrainedAt = -1;

    // How many aspects of the birth chart are given a sentence: the closest ones.
    private const int MaxAspects = 6;

    // How many planets the Report's closing section speaks of for how far they have been
    // grown into: those the character answers mark out most.
    private const int MaxReach = 5;

    // A facet must move by this many standard deviations between sittings, and a
    // character scale by this much of a point, to be more than the mood of the day.
    public const double FacetChange = 1.25, ScaleChange = 0.5;

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

    // `day`: for a chart with no birth time, the scan of its whole day (see
    // TimeSensitivityService.AnalyseDay), which says which bodies changed sign. A sign
    // that is not known is not read: the planet is still set against the answers, but
    // not against a sign, and the reading names the signs it may have been in. Without
    // a scan the Moon's sign is taken to be in doubt, never read as the noon guess.
    public MirrorReading Compose(NatalChart chart, InventoryProfile profile, TimeSensitivity? day = null) =>
        Compose(chart, new SelfReport { Personality = profile }, day);

    // `today`: for how old the life-area ratings are. Any part of the report may be
    // missing, and the reading is of whatever there is.
    public MirrorReading Compose(NatalChart chart, SelfReport report, TimeSensitivity? day = null, DateOnly? today = null)
    {
        IReadOnlyList<ZodiacSign>? Doubt(Planet planet) => TimeSensitivity.SignsInDoubt(chart, day, planet);
        var profile = report.Personality;

        // Every facet and every trait by its key, as standard deviations from the mean,
        // and every character scale and dimension in the same terms.
        var facets = profile?.Domains.SelectMany(d => d.Facets).ToDictionary(f => f.Facet.Key) ?? [];
        var scores = facets.ToDictionary(f => f.Key, f => f.Value.Z);
        foreach (var d in profile?.Domains ?? []) scores[d.Domain.Key] = d.Z;
        var character = report.Character;
        var scales = character?.Dimensions.SelectMany(d => d.Scales).ToDictionary(s => s.Scale.Key) ?? [];
        foreach (var (key, scale) in scales) scores[key] = scale.Z;

        var dignity = DignityService.ComputeIfTimed(chart);
        string name = chart.Celebrity.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? chart.Celebrity.Name;

        var planets = new List<MirrorPlanet>();
        var overview = new Dictionary<string, string>();
        var allLevels = new Dictionary<string, string>();
        foreach (var planet in Read)
        {
            if (chart.GetPlanet(planet) is not { } position ||
                !_c.Planets.TryGetValue(planet.Name(), out var entry)) continue;
            // Nothing has been answered that speaks of this one.
            if (!entry.Dials.Values.SelectMany(keys => keys).Any(k => scores.ContainsKey(k.TrimStart('-')))) continue;

            var levels = entry.Dials.ToDictionary(d => d.Key, d => LevelOf(d.Value, scores));
            foreach (var (dial, level) in levels) allLevels[$"{planet.Name()}.{dial}"] = level;
            bool Fits(Expression e) => e.When.All(w => levels.TryGetValue(w.Key, out var level) && level == w.Value);
            var expression = entry.Expressions.FirstOrDefault(Fits);
            if (expression is null) continue;
            var also = entry.Also.Where(Fits).ToList();

            // Each dial against what the planet's sign would lead one to expect.
            string sign = position.Sign.Name(), body = Body(planet);
            var comparisons = new List<MirrorComparison>();
            var doubt = Doubt(planet);
            Dictionary<string, string>? expected = null;
            bool hasSigns = doubt is null && entry.Signs.TryGetValue(sign, out expected);
            if (hasSigns)
                foreach (var (dial, level) in levels)
                    if (entry.Nouns.TryGetValue(dial, out var noun) &&
                        Comparison(dial, expected!.GetValueOrDefault(dial, "mid"), level, noun, body, sign) is { } c)
                        comparisons.Add(c);

            // What the Report takes: how the planet is lived, then how that sits with its
            // sign. Only a dial the answers mark out (high or low) is spoken of there: that
            // an ordinary answer falls short of what a sign would suggest is left to the
            // Chart and answers reading. And "That is…" may only follow the sentence it is
            // about, so the comparison chosen is of a dial the expression itself rests on.
            var marked = comparisons.Where(c => c.Actual != "mid").OrderByDescending(c => Math.Abs(c.Gap)).ToList();
            var related = marked.FirstOrDefault(c => expression.When.ContainsKey(c.Dial));
            string lived = Fill(expression.Report, name);
            string extra = string.Concat(also.Select(a => Fill(a.Report, name)).Where(s => s.Length > 0).Select(s => " " + s));

            // What it comes to in its house, for a planet the answers mark out.
            string liveWord = expression.Lived is "strain" or "well" ? expression.Lived : "";
            string inHouse = chart.Timed && doubt is null && liveWord.Length > 0 && expression.When.Count > 0 &&
                             entry.Houses.GetValueOrDefault(chart.GetHouseForLongitude(position.Longitude).ToString())
                                 ?.GetValueOrDefault(liveWord) is { Length: > 0 } there ? there : "";

            // A facet at the end of the profile that this planet's expression says
            // nothing of: its dial's average has hidden it, or the dial is not in play.
            var extremes = entry.Dials
                .Where(d => !expression.When.ContainsKey(d.Key))
                .SelectMany(d => d.Value)
                .Select(k => k.TrimStart('-'))
                .Select(k => (Key: k, Score: facets.GetValueOrDefault(k)))
                .Where(x => x.Score is { Band: ScoreBand.VeryHigh or ScoreBand.VeryLow } && _c.Extremes.ContainsKey(x.Key))
                .Select(x => Fill(x.Score!.Band == ScoreBand.VeryHigh ? _c.Extremes[x.Key].High : _c.Extremes[x.Key].Low, name))
                .ToList();

            // How far it has been grown into, by the character answers.
            double? reach = entry.Reach.Count == 0 ? null : MeanOf(entry.Reach, scores);
            var side = reach is null ? MirrorReach.Unmarked
                : reach > DialThreshold ? MirrorReach.Higher : reach < -DialThreshold ? MirrorReach.Lower : MirrorReach.Unmarked;

            string line = "";
            if (lived.Length > 0)
            {
                if (planet is Planet.Sun or Planet.Moon)
                {
                    // The two lights are read twice in the Report. How they are lived goes
                    // with their paragraph under The Planets; the sign, in the Overview.
                    line = lived;
                    if (marked.Count > 0) overview[planet.Name()] = Alone(marked[0], name);
                }
                else
                {
                    line = lived + (related is not null ? " " + After(related)
                        : marked.Count > 0 && marked[0].Gap != 0 ? " " + _c.Compare.Also.Replace("{clause}", marked[0].Clause) : "");
                }
                line += extra;
                if (inHouse.Length > 0) line += " " + inHouse;
            }
            if (extremes.Count > 0) line = (line + " " + string.Join(" ", extremes)).Trim();

            int? score = dignity?.Planets.FirstOrDefault(p => p.Planet == planet)?.Total;
            planets.Add(new MirrorPlanet(planet, position, expression.Name, ParseLived(expression.Lived), score,
                score is null ? MirrorExpectation.Unknown
                : score >= EasyAt ? MirrorExpectation.Easy
                : score <= StrainedAt ? MirrorExpectation.Strained : MirrorExpectation.Middling)
            {
                Text = expression.Text + string.Concat(also.Select(a => " " + a.Text)),
                Facets = entry.Dials.Values.SelectMany(keys => keys)
                    .Select(k => facets.GetValueOrDefault(k.TrimStart('-'))).OfType<FacetScore>().ToList(),
                Scales = entry.Dials.Values.SelectMany(keys => keys)
                    .Select(k => scales.GetValueOrDefault(k)).OfType<CharacterScore>().ToList(),
                Reach = side,
                ReachZ = reach ?? 0,
                ReachText = (side == MirrorReach.Higher ? entry.Higher : side == MirrorReach.Lower ? entry.Lower : null) ?? "",
                Extremes = extremes,
                HouseLine = inHouse,
                Stands = entry.Stands,
                DignityNotes = dignity?.Planets.FirstOrDefault(p => p.Planet == planet)?.Notes ?? "",
                IsOrdinary = expression.When.Count == 0,
                Comparisons = comparisons,
                SignsInDoubt = doubt,
                AgainstSign = doubt is not null ? SignUnsure(body, doubt)
                    : !hasSigns ? ""
                    : comparisons.Count == 0 ? _c.Compare.AsExpected.Replace("{body}", body).Replace("{sign}", sign)
                    : _c.Compare.InReading.Replace("{clauses}", string.Join("; ", comparisons.Select(c => c.Clause))),
                ReportLine = line,
                DailyHard = expression.Hard ?? "",
                DailyEasy = expression.Easy ?? "",
                ForecastHard = ForForecast(expression, planet, "hard"),
                ForecastEasy = ForForecast(expression, planet, "easy"),
            });
        }

        // The Ascendant, against how outgoing the answers are on the whole.
        if (chart.Timed && _c.Rising is { } rising)
        {
            string sign = ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name();
            // As for the planets, the Report speaks only where the answers mark it out.
            if (Comparison("outgoing", rising.Signs.GetValueOrDefault(sign, "mid"), LevelOf(rising.Dial, scores),
                    rising.Nouns, "the Ascendant", sign) is { Actual: not "mid" } c)
                overview["Rising"] = Alone(c, name);
        }

        var aspects = Aspects(chart, planets, name);
        // The balance counts every body by its sign, so one sign in doubt puts the count
        // in doubt, and nothing is said of it.
        bool counted = !chart.Planets.Any(p => Doubt(p.Planet) is not null);
        var elements = counted ? Balance(NatalMetricsService.ElementCounts(chart), _c.Elements, scores, name) : [];
        var modalities = counted ? Balance(NatalMetricsService.ModalityCounts(chart), _c.Modalities, scores, name) : [];
        string summary = profile is null ? "" : Summary(profile, name);

        // The life-area ratings count for as long as they are recent, and only where
        // there are houses to attach them to.
        var life = report.LifeAreas;
        var now = today ?? DateOnly.FromDateTime(DateTime.Now);
        bool lifeInForce = life is not null && chart.Timed && life.IsFreshOn(now);
        var houses = lifeInForce ? life!.Ratings.Where(r => r.IsMarked).ToDictionary(r => r.Area.House) : [];

        var sections = new List<DailySection> { Opening(chart, report), Lived(chart, planets) };
        if (chart.Timed && profile is not null) sections.Add(Compared(planets));
        sections.Add(Between(aspects));
        sections.Add(Whole(overview, elements.Values.Concat(modalities.Values), summary));
        if (character is not null) sections.Add(Character(character, planets));
        if (life is not null) sections.Add(LifeSection(chart, life, lifeInForce));
        if (Moved(report) is { } moved) sections.Add(moved);
        sections.Add(Closing());
        sections.RemoveAll(s => s.Items.Count == 0);

        DateOnly[] dates = [.. new[] { profile?.TakenOn, character?.TakenOn, life?.TakenOn }.OfType<DateOnly>()];

        return new MirrorReading
        {
            ChartId = chart.Celebrity.Id,
            ReportParagraphs = ForReport(chart, planets, name, report, houses),
            OverviewLines = overview,
            Summary = summary,
            AspectLines = aspects.ToDictionary(a => a.Key, a => a.Line),
            ElementLines = elements,
            ModalityLines = modalities,
            Levels = allLevels,
            PatternLines = PatternLines(chart, planets, name),
            LordLines = LordLines(planets, name),
            Houses = houses,
            LifeForecast = life?.Forecast ?? new LifeForecast(),
            Name = chart.Celebrity.Name,
            TakenOn = dates.Length == 0 ? now : dates.Max(),
            Planets = planets,
            Sections = sections,
        };
    }

    // ── Dials and expectations ────────────────────────────────────────────────

    private static List<double> ValuesOf(List<string> keys, Dictionary<string, double> scores) =>
        keys.Select(k => scores.TryGetValue(k.TrimStart('-'), out double z)
            ? (k.StartsWith('-') ? -z : z) : (double?)null).OfType<double>().ToList();

    private static double? MeanOf(List<string> keys, Dictionary<string, double> scores) =>
        ValuesOf(keys, scores) is { Count: > 0 } values ? values.Average() : null;

    private static string LevelOf(List<string> keys, Dictionary<string, double> scores)
    {
        var values = ValuesOf(keys, scores);
        if (values.Count == 0) return "mid";
        // A dial of one facet (or one whole trait) is held to the line the profile itself
        // draws between typical and low or high, so that the two never say different
        // things of it.
        double mean = values.Average(), threshold = values.Count == 1 ? 1.0 : DialThreshold;
        return mean > threshold ? "high" : mean < -threshold ? "low" : "mid";
    }

    // "By your answers your natal Mars is held in. Expect this stretch to press on that."
    private string ForForecast(Expression expression, Planet planet, string kind) =>
        (_c.Forecast.GetValueOrDefault(expression.Lived)?.GetValueOrDefault(kind) ?? "")
            .Replace("{planet}", planet.Name()).Replace("{expression}", expression.Name);

    private static int Rank(string level) => level switch { "high" => 1, "low" => -1, _ => 0 };

    // One dial against what was expected of it. Null when both are middling: there is
    // nothing to say.
    private MirrorComparison? Comparison(string dial, string expected, string actual, Noun noun, string body, string sign)
    {
        int gap = Rank(actual) - Rank(expected);
        if (gap == 0 && actual == "mid") return null;

        string clause = (gap == 0 ? _c.Compare.Match : gap > 0 ? _c.Compare.Above : _c.Compare.Below)
            .Replace("{far}", Math.Abs(gap) == 2 ? _c.Compare.Far : "")
            .Replace("{noun}", gap == 0 && actual == "low" ? noun.Low : noun.High)
            .Replace("{body}", body).Replace("{sign}", sign);
        return new MirrorComparison(dial, expected, actual, gap, clause);
    }

    // "That is far more temper than Mars in Libra would suggest."
    private string After(MirrorComparison c) =>
        _c.Compare.After.GetValueOrDefault(c.Gap == 0 ? "match" : "differs", "That is {clause}.").Replace("{clause}", c.Clause);

    // "By Ann's answers there is less push than Mars in Aries would suggest."
    private string Alone(MirrorComparison c, string name) =>
        Fill(_c.Compare.Alone.GetValueOrDefault(c.Gap == 0 ? "match" : "differs", "By {name}'s answers there is {clause}."), name)
            .Replace("{clause}", c.Clause);

    // "Aquarius or Pisces"
    private static string Either(IReadOnlyList<ZodiacSign> signs) => string.Join(" or ", signs.Select(s => s.Name()));

    // "Without a birth time the sign the Moon was in is not known (Aquarius or Pisces,
    // depending on the hour), so the answers are not set against a sign."
    private string SignUnsure(string body, IReadOnlyList<ZodiacSign> signs) =>
        _c.Notes.GetValueOrDefault("signUnsure",
                "Without a birth time the sign {body} was in is not known{signs}, so the answers are not set against a sign.")
            .Replace("{body}", body)
            .Replace("{signs}", signs.Count > 1 ? $" ({Either(signs)}, depending on the hour)" : "");

    private static string Body(Planet planet) => planet is Planet.Sun or Planet.Moon ? "the " + planet.Name() : planet.Name();

    private static string Fill(string? line, string name) => (line ?? "").Replace("{name}", name);

    private static MirrorLived ParseLived(string lived) => lived switch
    {
        "well" => MirrorLived.Well,
        "strain" => MirrorLived.Strain,
        _ => MirrorLived.Plain,
    };

    // ── Aspects of the birth chart ────────────────────────────────────────────

    private sealed record AspectLine(string Key, string Title, string Line);

    // A sentence for each major aspect between two planets the answers both speak of
    // (neither with only its catch-all expression), keyed as MirrorReading.AspectKey has it.
    private List<AspectLine> Aspects(NatalChart chart, List<MirrorPlanet> planets, string name)
    {
        var lines = new List<AspectLine>();
        static string Word(MirrorLived lived) => lived switch { MirrorLived.Well => "well", MirrorLived.Strain => "strain", _ => "plain" };
        var dwelt = new HashSet<Planet>();

        foreach (var x in chart.Aspects.Where(x => x.Type.IsMajor()).OrderBy(x => x.Orb))
        {
            // Placed for noon, the Moon may be seven degrees from where it was: an aspect
            // to it may not have been one. (The other readings leave it out likewise.)
            if (!chart.Timed && (x.PlanetA == Planet.Moon || x.PlanetB == Planet.Moon)) continue;
            if (!_c.Aspects.TryGetValue(x.Type.Tone().ToString(), out var templates)) continue;
            if (planets.FirstOrDefault(p => p.Planet == x.PlanetA) is not { IsOrdinary: false } a ||
                planets.FirstOrDefault(p => p.Planet == x.PlanetB) is not { IsOrdinary: false } b) continue;
            if (lines.Count == MaxAspects) break;

            // Where one end only is marked out (lived well or with strain) the sentence
            // dwells on it; a planet is dwelt on once, and after that simply named.
            string key = $"{Word(a.Lived)}|{Word(b.Lived)}";
            var marked = (a.Lived != MirrorLived.Plain) != (b.Lived != MirrorLived.Plain)
                ? (a.Lived != MirrorLived.Plain ? a : b).Planet : (Planet?)null;
            if (marked is { } one && !dwelt.Add(one)) key = "plain|plain";
            if (templates.GetValueOrDefault(key) is not { Length: > 0 } template) continue;

            NatalPoint pa = NatalPoint.Of(a.Planet), pb = NatalPoint.Of(b.Planet);
            lines.Add(new AspectLine(MirrorReading.AspectKey(pa, pb, x.Type), $"{Label(pa)} {x.Type.Verb()} {Label(pb)}",
                Fill(template, name)
                    .Replace("{a}", Body(a.Planet)).Replace("{ea}", a.Expression)
                    .Replace("{b}", Body(b.Planet)).Replace("{eb}", b.Expression)));
        }
        return lines;
    }

    private static string Label(NatalPoint p) => p.IsAngle ? p.Name : $"{p.Symbol} {p.Name}";

    // ── The balance of the chart ──────────────────────────────────────────────

    // For each element (or mode) the chart leans toward or lacks, whether the facets
    // that go with it are high or low. Nothing where they are ordinary.
    private static Dictionary<T, string> Balance<T>(Dictionary<T, int> counts, Dictionary<string, BalanceEntry> entries,
        Dictionary<string, double> scores, string name) where T : notnull
    {
        var lines = new Dictionary<T, string>();
        var (strong, empty) = ChartInterpreter.Leaning(counts);
        foreach (var key in strong.Concat(empty))
        {
            if (!entries.TryGetValue(key.ToString()!, out var entry)) continue;
            bool isStrong = strong.Contains(key);
            string line = LevelOf(entry.Dial, scores) switch
            {
                "high" => isStrong ? entry.StrongHigh : entry.WeakHigh,
                "low" => isStrong ? entry.StrongLow : entry.WeakLow,
                _ => "",
            };
            if (line.Length > 0) lines[key] = Fill(line, name);
        }
        return lines;
    }

    // "By Ann's answers, taken as a whole: high on Extraversion, low on Neuroticism, and
    // near the ordinary on the rest."
    private string Summary(InventoryProfile profile, string name)
    {
        static string And(List<string> items) => items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
        };
        var high = profile.Domains.Where(d => d.Band is ScoreBand.High or ScoreBand.VeryHigh).Select(d => d.Name).ToList();
        var low = profile.Domains.Where(d => d.Band is ScoreBand.Low or ScoreBand.VeryLow).Select(d => d.Name).ToList();

        var parts = new List<string>();
        if (high.Count > 0) parts.Add("high on " + And(high));
        if (low.Count > 0) parts.Add("low on " + And(low));
        if (parts.Count == 0) parts.Add(Note("summaryAll"));
        else if (high.Count + low.Count < profile.Domains.Count) parts.Add(Note("summaryRest"));
        parts.RemoveAll(string.IsNullOrWhiteSpace);

        string list = parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1];
        return Fill(Note("summary"), name).Replace("{list}", list);
    }

    // ── Sections of the Chart and answers reading ─────────────────────────────

    private DailySection Opening(NatalChart chart, SelfReport report)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Note("intro") },
            new() { Text = Note("invented") },
            new() { Text = report.Personality is null ? Note("coveredCharacter") : Note("covered") },
            new() { Text = report.Personality is null ? "" : Note("signs") },
        };
        if (!chart.Timed) items.Add(new DailyItem { Text = Note("untimed") });
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "The chart and the answers", Items = items };
    }

    // For each planet: what the chart says of it, what the answers say, and how the
    // answers sit with its sign.
    private DailySection Lived(NatalChart chart, List<MirrorPlanet> planets) => new()
    {
        Heading = "How each planet is being lived",
        Items = planets.Select(p =>
        {
            // A sign in doubt is named as the two it may be, and the Report's words for a
            // planet in one of them are not borrowed.
            string where = p.SignsInDoubt is { } doubt
                ? p.Planet.Name() + (doubt.Count > 1 ? $" in {Either(doubt)}, depending on the hour of birth" : ", its sign not known without a birth time")
                : $"{p.Planet.Name()} in {p.Position.Sign.Name()}" +
                  (chart.Timed ? $", {ChartInterpreter.Ordinal(chart.GetHouseForLongitude(p.Position.Longitude))} house" : "");
            string report = p.SignsInDoubt is null && _report is { IsAvailable: true } ? _report.PlanetInSign(p.Position) : "";
            return new DailyItem
            {
                Title = $"{p.Planet.Symbol()} {p.Planet.Name()}: {p.Expression}",
                Meta = string.Join("  ·  ", p.Facets.Select(f => $"{f.Name} {f.BandLabel.ToLowerInvariant()}")
                    .Concat(p.Scales.Select(s => $"{s.Name} {s.BandLabel.ToLowerInvariant()}"))),
                Text = $"The chart: {where}, the {(p.Planet > Planet.Pluto ? "point" : "planet")} of {p.Stands}." + (report.Length > 0 ? $" {report}." : "") +
                       $"\nThe answers: {p.Text}" +
                       (p.AgainstSign.Length > 0 ? $"\n{p.AgainstSign}" : "") +
                       (p.HouseLine.Length > 0 ? $"\nIn its house: {p.HouseLine}" : "") +
                       string.Concat(p.Extremes.Select(line => "\n" + line)) +
                       (p.ReachText.Length > 0 ? $"\n{p.ReachText}" : ""),
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

    private static DailySection Between(List<AspectLine> aspects) => new()
    {
        Heading = "Between the planets",
        Items = aspects.Select(a => new DailyItem { Title = a.Title, Text = a.Line }).ToList(),
    };

    private static DailySection Whole(Dictionary<string, string> overview, IEnumerable<string> balance, string summary)
    {
        var items = new List<DailyItem>();
        if (summary.Length > 0) items.Add(new DailyItem { Text = summary });
        if (overview.TryGetValue("Rising", out var rising)) items.Add(new DailyItem { Title = "↑ Ascendant", Text = rising });
        items.AddRange(balance.Select(line => new DailyItem { Text = line }));
        return new DailySection { Heading = "The chart as a whole", Items = items };
    }

    // The three dimensions, each with its scales, and the planets they mark out.
    private DailySection Character(CharacterProfile character, List<MirrorPlanet> planets)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Note("characterIntro") },
            new() { Text = Note("characterScale") },
        };
        items.AddRange(character.Dimensions.Select(d => new DailyItem
        {
            Title = $"{d.Name}: {d.BandLabel.ToLowerInvariant()}",
            Meta = string.Join("  ·  ", d.Scales.Select(s => $"{s.Name} {s.ScoreText}")),
            Text = d.Text,
        }));
        void Side(MirrorReach reach, string title)
        {
            var list = planets.Where(p => p.Reach == reach).ToList();
            if (list.Count > 0)
                items.Add(new DailyItem { Title = title, Text = string.Join(", ", list.Select(p => $"{p.Planet.Symbol()} {p.Planet.Name()}")) });
        }
        Side(MirrorReach.Higher, Note("reachHigher"));
        Side(MirrorReach.Lower, Note("reachLower"));
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = Note("sectionCharacter"), Items = items };
    }

    private DailySection LifeSection(NatalChart chart, LifeAreasReport life, bool inForce)
    {
        var items = new List<DailyItem> { new() { Text = Note("lifeHeading") } };
        if (!chart.Timed) items.Add(new DailyItem { Text = Note("lifeUntimed") });
        else if (!inForce) items.Add(new DailyItem { Text = Note("lifeStale") });

        foreach (var r in life.Ratings.Where(r => r.IsMarked).OrderBy(r => r.Rating))
            items.Add(new DailyItem
            {
                Title = $"{r.Area.Name}: {r.Choice.ToLowerInvariant()}",
                Meta = chart.Timed ? $"{ChartInterpreter.Ordinal(r.Area.House)} house" : null,
                Text = r.Lines!.Report,
            });
        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection
        {
            Heading = $"How life is going, as of {life.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}",
            Items = items,
        };
    }

    // What has moved between the two latest sittings, by more than a day's mood could.
    private DailySection? Moved(SelfReport report)
    {
        var items = new List<DailyItem>();

        if (report is { Personality: { } now, EarlierPersonality: { } then })
        {
            var before = then.Domains.SelectMany(d => d.Facets).ToDictionary(f => f.Facet.Key);
            var moved = now.Domains.SelectMany(d => d.Facets)
                .Select(f => (Facet: f, By: f.Z - before[f.Facet.Key].Z))
                .Where(x => Math.Abs(x.By) >= FacetChange)
                .OrderByDescending(x => Math.Abs(x.By))
                .ToList();
            string date = then.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            items.Add(new DailyItem
            {
                Title = "Personality",
                Meta = $"since {date}",
                Text = moved.Count == 0
                    ? Note("changeNone").Replace("{date}", date)
                    : Note("changeIntro").Replace("{date}", date) + "\n" + string.Join("\n", moved.Select(x =>
                        $"{x.Facet.Name}: {(x.By > 0 ? "up" : "down")}, now {x.Facet.BandLabel.ToLowerInvariant()} (was {before[x.Facet.Facet.Key].BandLabel.ToLowerInvariant()})")),
            });
        }

        if (report is { Character: { } current, EarlierCharacter: { } earlier })
        {
            var before = earlier.Dimensions.SelectMany(d => d.Scales).ToDictionary(s => s.Scale.Key);
            var moved = current.Dimensions.SelectMany(d => d.Scales)
                .Select(s => (Scale: s, By: s.Mean - before[s.Scale.Key].Mean))
                .Where(x => Math.Abs(x.By) >= ScaleChange)
                .OrderByDescending(x => Math.Abs(x.By))
                .ToList();
            string date = earlier.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            items.Add(new DailyItem
            {
                Title = "Character",
                Meta = $"since {date}",
                Text = moved.Count == 0
                    ? Note("changeNone").Replace("{date}", date)
                    : Note("changeIntro").Replace("{date}", date) + "\n" + string.Join("\n", moved.Select(x =>
                        $"{x.Scale.Name}: {(x.By > 0 ? "up" : "down")}, now {x.Scale.ScoreText} (was {before[x.Scale.Scale.Key].ScoreText})")),
            });
        }

        return items.Count == 0 ? null : new DailySection { Heading = "What has moved", Items = items };
    }

    // A sentence for each chart pattern most of whose planets the answers mark the same way.
    private Dictionary<string, string> PatternLines(NatalChart chart, List<MirrorPlanet> planets, string name)
    {
        var lines = new Dictionary<string, string>();
        foreach (var pattern in AspectPatternService.Detect(chart))
        {
            var read = pattern.Points.Where(p => p.Kind == NatalPointKind.Body)
                .Select(p => planets.FirstOrDefault(x => x.Planet == p.Body)).OfType<MirrorPlanet>()
                .Where(p => !p.IsOrdinary).ToList();
            int strain = read.Count(p => p.Lived == MirrorLived.Strain), well = read.Count(p => p.Lived == MirrorLived.Well);
            if (strain == well || Math.Max(strain, well) < 2) continue;

            bool hard = strain > well;
            var named = read.Where(p => p.Lived == (hard ? MirrorLived.Strain : MirrorLived.Well)).Select(p => Body(p.Planet)).ToList();
            string list = string.Join(", ", named.Take(named.Count - 1)) + " and " + named[^1];
            if (_c.Patterns.GetValueOrDefault(pattern.Type.ToString())?.GetValueOrDefault(hard ? "strain" : "well") is { Length: > 0 } line)
                lines[MirrorReading.PatternKey(pattern)] = Fill(line, name).Replace("{points}", list);
        }
        return lines;
    }

    private Dictionary<Planet, string> LordLines(List<MirrorPlanet> planets, string name) =>
        planets.Where(p => !p.IsOrdinary && p.Lived != MirrorLived.Plain)
            .ToDictionary(p => p.Planet, p =>
                Fill(_c.Timing.Lord.GetValueOrDefault(p.Lived == MirrorLived.Strain ? "strain" : "well", ""), name)
                    .Replace("{planet}", Body(p.Planet)).Replace("{expression}", p.Expression));

    // What the Report says at its end when the answers have shaped it.
    private List<string> ForReport(NatalChart chart, List<MirrorPlanet> planets, string name, SelfReport report,
        Dictionary<int, LifeAreaRating> houses)
    {
        static string Day(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var paragraphs = new List<string>();
        if (report.Personality is { } profile)
            paragraphs.Add(Fill(Note("reportIntro"), name).Replace("{date}", Day(profile.TakenOn)));
        if (report.Character is { } character)
        {
            paragraphs.Add(Fill(Note("reportCharacter"), name).Replace("{date}", Day(character.TakenOn))
                .Replace("{list}", string.Join(" ", character.Dimensions.Select(d => d.Text))));
            paragraphs.AddRange(planets.Where(p => p.Reach != MirrorReach.Unmarked)
                .OrderByDescending(p => Math.Abs(p.ReachZ)).Take(MaxReach)
                .Select(p => $"{p.Planet.Symbol()} {p.Planet.Name()}: {p.ReachText}"));
        }
        if (report.Personality is not null && chart.Timed)
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
        if (houses.Count > 0 && report.LifeAreas is { } life)
        {
            paragraphs.Add(Fill(Note("reportLifeAreas"), name).Replace("{date}", Day(life.TakenOn)));
            paragraphs.AddRange(houses.Values.OrderBy(r => r.Area.House).Select(r => r.Lines!.Report));
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
