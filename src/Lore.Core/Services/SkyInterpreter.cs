using System.Globalization;
using System.Text.Json;
using Lore.Models;
using NodaTime;

namespace Lore.Services;

// Writes the Sky view's reading: the planetary mood of a day for everyone, from the
// aspects the planets make to one another (TransitService.ScanSky). Its own lines are
// in sky.json; the Moon, phases, stations, retrogrades, calendar events, planetary
// days and hours are the Daily view's, from daily.json. Deterministic: the same date
// and time zone always give the same reading.
public sealed class SkyInterpreter
{
    private sealed class Corpus
    {
        // "Mars|Tension|Saturn": the faster planet first, as SkyAspect names them.
        public Dictionary<string, string> Pairs { get; init; } = new();
        public Dictionary<string, string> PlanetThemes { get; init; } = new();
        public Dictionary<string, string> ToneLinks { get; init; } = new();
        public Dictionary<string, string> DayTones { get; init; } = new();
        public Dictionary<string, string> FastIngress { get; init; } = new();
        public Dictionary<string, string> Notes { get; init; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const int MaxHighlights = 4;
    private const int MaxPerPlanet = 2;   // no one planet fills the foreground
    private const int MaxBackground = 3;

    private readonly Corpus _c;
    private readonly DailyInterpreter _daily;

    public SkyInterpreter(string skyJsonPath, DailyInterpreter daily)
    {
        _daily = daily;
        if (File.Exists(skyJsonPath))
        {
            using var stream = File.OpenRead(skyJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else _c = new Corpus();
    }

    // Every pair and tone the scan can report, as corpus keys: each pair of the ten
    // planets bar the Sun and Moon, in each tone, except the five that cannot happen
    // (Mercury and Venus never stray far enough from the Sun, or from each other).
    public static IEnumerable<string> PossibleKeys()
    {
        var planets = TransitService.SkyPlanets;
        for (int i = 0; i < planets.Count; i++)
            for (int j = i + 1; j < planets.Count; j++)
                foreach (var tone in Enum.GetValues<TransitTone>())
                    if (CanOccur(planets[i], planets[j], tone))
                        yield return Key(planets[i], tone, planets[j]);
    }

    private static bool CanOccur(Planet a, Planet b, TransitTone tone)
    {
        if (a == Planet.Moon && b == Planet.Sun) return false;
        bool sunInner = a == Planet.Sun && b is Planet.Mercury or Planet.Venus;
        if (sunInner) return tone == TransitTone.Conjunction;
        if (a == Planet.Mercury && b == Planet.Venus) return tone != TransitTone.Tension;
        return true;
    }

    // Every pair, tone and day tone has its line.
    public bool IsAvailable =>
        PossibleKeys().All(k => _c.Pairs.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) &&
        Enum.GetValues<DayTone>().All(t => _c.DayTones.TryGetValue(t.ToString(), out var v) && !string.IsNullOrWhiteSpace(v));

    private static string Key(Planet first, TransitTone tone, Planet second) => $"{first.Name()}|{tone}|{second.Name()}";

    // The bespoke line for the pair, or a plainer one assembled from the planets' themes.
    public string PairText(Planet first, TransitTone tone, Planet second)
    {
        if (_c.Pairs.TryGetValue(Key(first, tone, second), out var line) && !string.IsNullOrWhiteSpace(line))
            return line;
        string a = Lookup(_c.PlanetThemes, first.Name(), first.Name());
        string link = Lookup(_c.ToneLinks, tone.ToString(), "meets");
        string b = Lookup(_c.PlanetThemes, second.Name(), second.Name());
        return $"{Capitalise(a)} {link} {b}.";
    }

    // ── Ranking and tone ──────────────────────────────────────────────────────

    private sealed record Selection(List<SkyAspect> Ranked, List<SkyAspect> Highlights, List<SkyAspect> Background, List<SkyAspect> Lunar);

    // Scores every aspect and picks the ones the reading writes up: an editorial
    // priority, as in the Daily view, not a likelihood of anything.
    private static Selection Select(IReadOnlyList<SkyAspect> aspects)
    {
        foreach (var a in aspects) { a.Score = Score(a); a.Shown = false; }
        var ranked = aspects.OrderByDescending(a => a.Score).ThenBy(a => a.PeakUtc).ToList();

        var highlights = new List<SkyAspect>();
        foreach (var a in ranked.Where(a => !a.IsLunar && !a.IsBackground))
        {
            if (highlights.Count == MaxHighlights) break;
            if (highlights.Count(h => h.First == a.First || h.Second == a.First) < MaxPerPlanet &&
                highlights.Count(h => h.First == a.Second || h.Second == a.Second) < MaxPerPlanet)
                highlights.Add(a);
        }
        var background = ranked.Where(a => a.IsBackground).Take(MaxBackground).ToList();
        // The Moon's contacts that are exact during the day, in the order they come.
        var lunar = aspects.Where(a => a.IsLunar && a.Phase == TransitPhase.Exact).OrderBy(a => a.PeakUtc).ToList();
        return new Selection(ranked, highlights, background, lunar);
    }

    private static double Score(SkyAspect a)
    {
        double closeness = 1 - 0.7 * Math.Min(1, a.MinOrb / TransitService.SkyOrb(a.First));
        double score = Weight(a.First) * Weight(a.Second) * AspectWeight(a.Aspect) * closeness;
        if (a.Phase == TransitPhase.Exact) score += a.IsBackground ? 0.5 : 0.25;
        return score;
    }

    private static double Weight(Planet p) => p switch
    {
        Planet.Sun => 1.0, Planet.Mars => 0.95, Planet.Venus => 0.9, Planet.Mercury => 0.85, Planet.Moon => 0.7,
        Planet.Saturn => 1.0, Planet.Jupiter => 0.95, Planet.Pluto => 0.95,
        Planet.Uranus => 0.9, Planet.Neptune => 0.9,
        _ => 0.5,
    };

    private static double AspectWeight(AspectType a) => a switch
    {
        AspectType.Conjunction => 1.0,
        AspectType.Opposition or AspectType.Square => 0.9,
        AspectType.Trine => 0.8,
        _ => 0.6,
    };

    // The day's tone for everyone, on the Daily view's five steps and thresholds. The
    // foreground counts fully, the slow background half and the Moon's contacts a fifth;
    // aspects between the generational planets, in orb for years, not at all. With
    // nothing else beyond the Moon in aspect the day is Quiet.
    public static DayTone ToneOf(SkyDay day) => ToneOf(Select(day.Aspects));

    private static DayTone ToneOf(Selection s)
    {
        var background = s.Background.Where(a => !a.IsGenerational).ToList();
        if (s.Highlights.Count == 0 && background.Count == 0) return DayTone.Quiet;

        double flow = 0, tension = 0;
        void Add(SkyAspect a, double weight)
        {
            weight *= a.Score / AspectWeight(a.Aspect);
            switch (a.Tone)
            {
                case TransitTone.Flow: flow += weight; break;
                case TransitTone.Tension: tension += weight; break;
                default:
                    // A conjunction takes its colour from the planets in it.
                    int lean = Lean(a.First) + Lean(a.Second);
                    if (lean > 0) flow += weight;
                    else if (lean < 0) tension += weight;
                    break;
            }
        }
        foreach (var a in s.Highlights) Add(a, 1.0);
        foreach (var a in background) Add(a, 0.5);
        foreach (var a in s.Lunar) Add(a, 0.2);

        double total = flow + tension;
        if (total <= 0) return DayTone.Focused;
        double share = flow / total;
        return share >= 0.62 ? DayTone.Flowing : share <= 0.38 ? DayTone.Demanding : DayTone.Mixed;
    }

    private static int Lean(Planet p) => p switch
    {
        Planet.Venus or Planet.Jupiter => 1,
        Planet.Mars or Planet.Saturn or Planet.Pluto => -1,
        _ => 0,
    };

    // ── The reading ───────────────────────────────────────────────────────────

    public SkyReading Compose(SkyDay sky, DateTimeZone zone)
    {
        // The Daily view's shared lines, with their alternative wordings chosen by date.
        var daily = _daily.Varied("sky|" + sky.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + zone.Id);
        var s = Select(sky.Aspects);
        foreach (var a in s.Highlights.Concat(s.Background).Concat(s.Lunar)) a.Shown = true;
        var tone = ToneOf(s);
        var day = sky.Day;

        var sections = new List<DailySection>();

        var glance = new List<DailyItem> { new() { Text = Lookup(_c.DayTones, tone.ToString(), "") } };
        var ruler = TransitService.DayRulers[(int)sky.Date.DayOfWeek];
        glance.Add(new DailyItem
        {
            Title = $"{ruler.Symbol()} {sky.Date:dddd}, {ruler.Name()}'s day",
            Text = daily.PlanetaryDayText(ruler) + (day.Hours is null ? " " + Note("planetaryDay", "") : ""),
        });
        glance.Add(daily.MoonItem(day, zone));
        glance.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        sections.Add(new DailySection { Heading = "The day at a glance", Items = glance });

        var foreground = s.Highlights.OrderBy(a => a.PeakUtc).ToList();
        sections.Add(new DailySection
        {
            Heading = "Today's aspects",
            Items = foreground.Count > 0
                ? foreground.Select(a => Item(a, day, zone)).ToList()
                : [new DailyItem { Text = Note("quietDay", "None of the Sun, Mercury, Venus or Mars is within two degrees of an aspect today.") }],
        });

        var also = new List<DailyItem>();
        foreach (var e in sky.Events)
            also.Add(e.Kind == SkyEventKind.Ingress && !e.Body.IsSlowMover() ? FastIngressItem(e, zone) : daily.SkyEventItem(e, zone));
        also.AddRange(daily.SkyNotes(day, zone, stations: false));
        also.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        if (also.Count > 0)
            sections.Add(new DailySection { Heading = "Also in the sky", Items = also });

        sections.Add(new DailySection
        {
            Heading = "The Moon's contacts",
            Items = s.Lunar.Count > 0
                ? [new DailyItem { Text = Note("moonContacts", "") }, .. s.Lunar.Select(a => Item(a, day, zone))]
                : [new DailyItem { Text = Note("noMoonContacts", "The Moon makes no exact aspect to the planets today.") }],
        });

        if (s.Background.Count > 0)
            sections.Add(new DailySection
            {
                Heading = "Longer-running themes",
                Items = s.Background.Select(a => Item(a, day, zone)).ToList(),
            });

        sections.Add(new DailySection
        {
            Heading = "Where the planets are",
            Items = [new DailyItem { Meta = Note("positions", ""), Text = Positions(day) }],
        });

        if (day.Hours is { } hours)
            sections.Add(daily.HoursSection(hours, zone));

        // A section whose note line is missing from the corpus keeps only its entries.
        sections = sections.Select(x => new DailySection
        {
            Heading = x.Heading,
            Items = x.Items.Where(i => !string.IsNullOrWhiteSpace(i.Text)).ToList(),
        }).Where(x => x.Items.Count > 0).ToList();

        return new SkyReading
        {
            Date = sky.Date,
            ZoneId = zone.Id,
            Tone = tone,
            Sections = sections,
            Aspects = s.Ranked,
            Trace = s.Ranked.Select(a => TraceLine(a, day, zone)).ToList(),
            Midday = day.Midday,
        };
    }

    private DailyItem Item(SkyAspect a, DaySky day, DateTimeZone zone)
    {
        var meta = new List<string> { Timing(a, day, zone) };
        if (day.Body(a.First) is { } p && day.Body(a.Second) is { } q)
            meta.Add(p.Sign == q.Sign ? $"both in {p.Sign.Name()}" : $"{a.First.Name()} in {p.Sign.Name()}, {a.Second.Name()} in {q.Sign.Name()}");
        var retro = new[] { (a.First, a.FirstRetrograde), (a.Second, a.SecondRetrograde) }
            .Where(x => x.Item2).Select(x => x.Item1.Name()).ToList();
        if (retro.Count > 0) meta.Add($"{string.Join(" and ", retro)} {(retro.Count == 1 ? "is" : "are")} retrograde");

        return new DailyItem
        {
            Title = Title(a),
            Meta = string.Join("  ·  ", meta),
            Text = PairText(a.First, a.Tone, a.Second),
        };
    }

    private static string Title(SkyAspect a) =>
        $"{a.First.Symbol()} {a.First.Name()} {a.Aspect.Verb()} {a.Second.Symbol()} {a.Second.Name()}";

    private DailyItem FastIngressItem(SkyEvent e, DateTimeZone zone)
    {
        string line = Lookup(_c.FastIngress, e.Retrograde ? "Retrograde" : e.Body.Name(), "")
            .Replace("{planet}", e.Body.Name()).Replace("{sign}", e.Sign.Name());
        return new DailyItem
        {
            Title = $"{e.Body.Symbol()} {e.Body.Name()} {(e.Retrograde ? "goes back into" : "enters")} {e.Sign.Name()}",
            Meta = $"At {Time(e.Utc, zone)}" + (e.Retrograde ? "  ·  retrograde" : ""),
            Text = line,
        };
    }

    private static string Positions(DaySky day) => string.Join("\n", TransitService.SkyPlanets
        .OrderBy(p => (int)p)
        .Select(day.Body).OfType<PlanetPosition>()
        .Select(p => $"{p.Planet.Symbol()} {p.Planet.Name()}  {ZodiacSignExtensions.FormatDegreeInSign(p.Longitude)} {p.Sign.Name()}" +
                     (p.IsRetrograde ? "  ℞" : "")));

    // "Exact at 3:12 PM", "Building — exact 8 November, 3:12 PM", "Easing — was exact around 2 October".
    private static string Timing(SkyAspect a, DaySky day, DateTimeZone zone)
    {
        if (a.Phase == TransitPhase.Exact)
            return a.IsBackground ? $"Exact today, at {Time(a.PeakUtc, zone)}" : $"Exact at {Time(a.PeakUtc, zone)}";

        string phase = a.Phase == TransitPhase.Building ? "Building" : "Easing";
        if (a.ExactUtc is not { } exact)
            return $"{phase}, within {a.MinOrb:0.0}°";

        var local = Local(exact, zone);
        string date = local.Year == day.Date.Year ? local.ToString("d MMMM") : local.ToString("d MMMM yyyy");
        string when = a.IsBackground ? $"around {date}" : $"{date}, {local:t}";
        return a.Phase == TransitPhase.Building ? $"Building — exact {when}" : $"Easing — was exact {when}";
    }

    private static string TraceLine(SkyAspect a, DaySky day, DateTimeZone zone)
    {
        string shown = a.Shown ? "   ✓ in the reading" : "";
        return $"{a.First.Symbol()} {a.Aspect.Symbol()} {a.Second.Symbol()}   " +
               $"{a.First.Name()} {a.Aspect.Verb()} {a.Second.Name()}  ·  " +
               $"closest {a.MinOrb:0.00}°  ·  {Timing(a, day, zone)}{shown}";
    }

    // ── Text ──────────────────────────────────────────────────────────────────

    // The reading as plain text, for saving or pasting.
    public static byte[] ToText(SkyReading reading)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Planetary mood");
        sb.AppendLine(reading.DateText);
        sb.AppendLine($"The day reads as {reading.Tone.Label()}");
        foreach (var section in reading.Sections)
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
        sb.AppendLine("ALL ASPECTS IN EFFECT");
        foreach (var line in reading.Trace) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine($"Generated by Lore on {DateTime.Now:yyyy-MM-dd} from the aspects the planets make to one another — a prompt for reflection, not a prediction.");
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static DateTime Local(DateTime utc, DateTimeZone zone) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(zone).ToDateTimeUnspecified();

    private static string Time(DateTime utc, DateTimeZone zone) => Local(utc, zone).ToString("t");

    private string Note(string key, string fallback) => Lookup(_c.Notes, key, fallback);

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
