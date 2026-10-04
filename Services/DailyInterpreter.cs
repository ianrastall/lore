using Lore.Models;
using NodaTime;
using System.Text.Json;

namespace Lore.Services;

// Writes the daily horoscope from a day's transits (TransitService) and an editable
// corpus (Data\daily.json). Three steps: rank the transits, choose a few that fit in a
// readable page, then put the matching authored text under each.
//
// Everything here is deterministic and offline: the same chart and date always give
// the same reading, and every sentence comes from the corpus, selected by a calculated
// transit — nothing is generated at run time.
public sealed class DailyInterpreter
{
    private sealed class Corpus
    {
        // Bespoke lines keyed "Mover|Tone|Target" (e.g. "Mars|Tension|Midheaven"), where
        // Tone is Conjunction, Flow (sextile/trine) or Tension (square/opposition).
        public Dictionary<string, string> Transits { get; init; } = new();

        // Building blocks for any pair without a bespoke line.
        public Dictionary<string, string> MoverThemes { get; init; } = new();
        public Dictionary<string, string> TargetThemes { get; init; } = new();
        public Dictionary<string, string> ToneLinks { get; init; } = new();

        public Dictionary<string, string> MoonInSign { get; init; } = new();
        public Dictionary<string, string> MoonInHouse { get; init; } = new();
        public Dictionary<string, string> SunInHouse { get; init; } = new();
        public Dictionary<string, string> MoonPhases { get; init; } = new();
        public Dictionary<string, string> Retrogrades { get; init; } = new();
        public Dictionary<string, string> Stations { get; init; } = new();
        public Dictionary<string, string> DayTones { get; init; } = new();
        public Dictionary<string, string> Houses { get; init; } = new();
        public Dictionary<string, string> Notes { get; init; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // How much fits on the page: the day's foreground, then the slower backdrop.
    private const int MaxHighlights = 3;
    private const int MaxPerMover = 2;   // no one planet (the Moon especially) gets to fill a list
    private const int MaxBackground = 3;

    private readonly Corpus _c;

    public DailyInterpreter(string dailyJsonPath)
    {
        if (File.Exists(dailyJsonPath))
        {
            using var stream = File.OpenRead(dailyJsonPath);
            _c = JsonSerializer.Deserialize<Corpus>(stream, JsonOpts) ?? new Corpus();
        }
        else
        {
            _c = new Corpus();
        }
    }

    public DailyReading Compose(NatalChart natal, DaySky sky, DateTimeZone zone)
    {
        foreach (var e in sky.Events)
        {
            e.Score = Score(e);
            e.Shown = false;
        }
        var ranked = sky.Events.OrderByDescending(e => e.Score).ToList();

        var highlights = Pick(ranked.Where(e => !e.IsBackground), MaxHighlights);
        var background = Pick(ranked.Where(e => e.IsBackground), MaxBackground);
        foreach (var e in highlights.Concat(background)) e.Shown = true;

        // Read in the order things happen, not the order they were ranked.
        highlights.Sort((a, b) => a.PeakUtc.CompareTo(b.PeakUtc));

        DayTone tone = ToneOf(highlights, background);
        bool timed = natal.Celebrity.BirthTimeKnown;

        var sections = new List<DailySection> { Glance(natal, sky, zone, tone, timed) };

        sections.Add(new DailySection
        {
            Heading = "Today's highlights",
            Items = highlights.Count > 0
                ? highlights.Select(e => Item(e, sky, zone)).ToList()
                : [new DailyItem { Text = Note("quietDay",
                    "None of the faster planets makes a close contact to this chart today. " +
                    "Nothing in particular is being stirred, which leaves the day free for whatever is already under way.") }]
        });

        if (background.Count > 0)
            sections.Add(new DailySection
            {
                Heading = "Longer-running themes",
                Items = background.Select(e => Item(e, sky, zone)).ToList()
            });

        var skyNotes = SkyNotes(natal, sky, timed);
        if (skyNotes.Count > 0)
            sections.Add(new DailySection { Heading = "Also in the sky", Items = skyNotes });

        return new DailyReading
        {
            Name = natal.Celebrity.Name,
            Date = sky.Date,
            ZoneId = sky.ZoneId,
            Tone = tone,
            Sections = sections,
            Events = ranked,
            Trace = ranked.Select(e => TraceLine(e, sky, zone)).ToList(),
        };
    }

    // ── Ranking ───────────────────────────────────────────────────────────────
    // An editorial priority — which transits the reading should spend its few slots
    // on — not a measure of how likely anything is to happen. Closer contacts, heavier
    // movers, more personal natal points and harder-edged aspects rank higher, and a
    // contact that is exact today gets a lift.

    // The best-ranked few, with a cap on how many any single mover contributes.
    private static List<TransitEvent> Pick(IEnumerable<TransitEvent> ranked, int max)
    {
        var picked = new List<TransitEvent>();
        foreach (var e in ranked)
        {
            if (picked.Count == max) break;
            if (picked.Count(p => p.Mover == e.Mover) < MaxPerMover)
                picked.Add(e);
        }
        return picked;
    }

    private static double Score(TransitEvent e)
    {
        double closeness = 1 - 0.7 * Math.Min(1, e.MinOrb / TransitService.Orb);
        double score = MoverWeight(e.Mover) * TargetWeight(e.Target) * AspectWeight(e.Aspect) * closeness;
        if (e.Phase == TransitPhase.Exact)
            score += e.IsBackground ? 0.5 : 0.25;
        return score;
    }

    private static double MoverWeight(Planet p) => p switch
    {
        // foreground: the Moon is the quickest and commonest, so it counts least
        Planet.Sun => 1.0, Planet.Mars => 0.95, Planet.Venus => 0.9, Planet.Mercury => 0.85, Planet.Moon => 0.7,
        // background
        Planet.Saturn => 1.0, Planet.Jupiter => 0.95, Planet.Pluto => 0.95,
        Planet.Uranus => 0.9, Planet.Neptune => 0.9,
        Planet.Chiron => 0.7, Planet.NorthNode => 0.7,
        _ => 0.5
    };

    private static double TargetWeight(NatalPoint t) => t.Kind switch
    {
        NatalPointKind.Ascendant => 1.0,
        NatalPointKind.Midheaven => 0.9,
        _ => t.Body switch
        {
            Planet.Sun or Planet.Moon => 1.0,
            Planet.Mercury or Planet.Venus or Planet.Mars => 0.8,
            Planet.Jupiter or Planet.Saturn => 0.6,
            Planet.NorthNode or Planet.Chiron => 0.5,
            _ => 0.4
        }
    };

    private static double AspectWeight(AspectType a) => a switch
    {
        AspectType.Conjunction => 1.0,
        AspectType.Opposition or AspectType.Square => 0.9,
        AspectType.Trine => 0.8,
        _ => 0.6
    };

    // The day's weather: how the chosen transits split between easy and hard. The
    // slower background counts half, since it is there every day for weeks.
    private static DayTone ToneOf(List<TransitEvent> highlights, List<TransitEvent> background)
    {
        if (highlights.Count == 0 && background.Count == 0) return DayTone.Quiet;

        double flow = 0, tension = 0;
        void Add(TransitEvent e, double weight)
        {
            // The ranking favours hard aspects (they are the more noticeable); take that
            // back out here, or the same bias would tip every day toward Demanding.
            weight *= e.Score / AspectWeight(e.Aspect);
            switch (e.Tone)
            {
                case TransitTone.Flow: flow += weight; break;
                case TransitTone.Tension: tension += weight; break;
                default:
                    // A conjunction takes its colour from the planet arriving.
                    if (e.Mover is Planet.Venus or Planet.Jupiter) flow += weight;
                    else if (e.Mover is Planet.Mars or Planet.Saturn or Planet.Pluto) tension += weight;
                    break;
            }
        }
        foreach (var e in highlights) Add(e, 1.0);
        foreach (var e in background) Add(e, 0.5);

        double total = flow + tension;
        if (total <= 0) return DayTone.Focused;
        double share = flow / total;
        return share >= 0.62 ? DayTone.Flowing : share <= 0.38 ? DayTone.Demanding : DayTone.Mixed;
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private DailySection Glance(NatalChart natal, DaySky sky, DateTimeZone zone, DayTone tone, bool timed)
    {
        var items = new List<DailyItem>
        {
            new() { Text = Lookup(_c.DayTones, tone.ToString(), "") }
        };

        string moon = Lookup(_c.MoonInSign, sky.MoonSign.Name(), "");
        if (sky.MoonEnters is { } next && sky.MoonIngressUtc is { } ingress)
        {
            // The corpus lines read "The Moon in Cancer turns the mood …"; after a sign
            // change that becomes "… it moves into Cancer and turns the mood …". A line
            // edited into some other shape is simply appended as its own sentence.
            string line = Lookup(_c.MoonInSign, next.Name(), "");
            string lead = $"The Moon in {next.Name()} ";
            moon += line.StartsWith(lead, StringComparison.Ordinal)
                ? $" Around {Time(ingress, zone)} it moves into {next.Name()} and {line[lead.Length..]}"
                : $" Around {Time(ingress, zone)} it moves into {next.Name()}. {line}";
        }
        string phase = Lookup(_c.MoonPhases, sky.Phase.ToString(), "");
        if (sky.PhaseExactUtc is { } exact)
            phase = $"The {sky.Phase.Name()} is exact at {Time(exact, zone)}. " + phase;
        items.Add(new DailyItem
        {
            Title = $"☽ Moon in {sky.MoonSign.Name()}  ·  {sky.Phase.Name()}",
            Text = $"{moon} {phase}".Trim()
        });

        if (timed)
        {
            if (sky.Body(Planet.Moon) is { } m)
            {
                int house = natal.GetHouseForLongitude(m.Longitude);
                items.Add(new DailyItem
                {
                    Title = $"Moon in your {ChartInterpreter.Ordinal(house)} house",
                    Meta = Capitalise(Lookup(_c.Houses, house.ToString(), "")),
                    Text = Lookup(_c.MoonInHouse, house.ToString(), "")
                });
            }
            if (sky.Body(Planet.Sun) is { } s)
            {
                int house = natal.GetHouseForLongitude(s.Longitude);
                items.Add(new DailyItem
                {
                    Title = $"☉ Sun in your {ChartInterpreter.Ordinal(house)} house",
                    Meta = Capitalise(Lookup(_c.Houses, house.ToString(), "")),
                    Text = Lookup(_c.SunInHouse, house.ToString(), "")
                });
            }
        }
        else
        {
            items.Add(new DailyItem { Text = Note("unknownBirthTime",
                "The birth time is unknown, so this reading leaves out the houses, the Ascendant and " +
                "Midheaven, and contacts to the natal Moon — all of which depend on the hour of birth.") });
        }

        items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
        return new DailySection { Heading = "The day at a glance", Items = items };
    }

    private DailyItem Item(TransitEvent e, DaySky sky, DateTimeZone zone)
    {
        string target = e.Target.IsAngle ? e.Target.Name : $"{e.Target.Symbol} {e.Target.Name}";

        var meta = new List<string> { Timing(e, sky, zone) };
        if (e.TargetHouse is { } house)
            meta.Add($"natal {e.Target.Name} is in your {ChartInterpreter.Ordinal(house)} house " +
                     $"({Lookup(_c.Houses, house.ToString(), "this area of life")})");
        if (e.MoverRetrograde)
            meta.Add($"{e.Mover.Name()} is retrograde");

        return new DailyItem
        {
            Title = $"{e.Mover.Symbol()} {e.Mover.Name()} {e.Aspect.Verb()} your natal {target}",
            Meta = string.Join("  ·  ", meta),
            Text = TransitText(e)
        };
    }

    // The bespoke line for this mover, tone and natal point if the corpus has one;
    // otherwise a plainer sentence assembled from the three building blocks.
    private string TransitText(TransitEvent e) => TransitText(e.Mover, e.Tone, e.Target);

    private string TransitText(Planet moving, TransitTone tone, NatalPoint natalPoint)
    {
        string key = $"{moving.Name()}|{tone}|{natalPoint.Name}";
        if (_c.Transits.TryGetValue(key, out var bespoke) && !string.IsNullOrWhiteSpace(bespoke))
            return bespoke;

        string mover = Lookup(_c.MoverThemes, moving.Name(), moving.Name());
        string link = Lookup(_c.ToneLinks, tone.ToString(), "meets");
        string target = Lookup(_c.TargetThemes, natalPoint.Name, $"natal {natalPoint.Name}");
        return $"{Capitalise(mover)} {link} your {target}.";
    }

    // ── Forecast ──────────────────────────────────────────────────────────────
    // The passes found by TransitService.Forecast, written out month by month in the
    // order they peak, each with the same line the Daily view would give it.
    public ForecastReading ComposeForecast(
        NatalChart natal, IReadOnlyList<TransitPass> passes, DateOnly start, int days, DateTimeZone zone)
    {
        LocalDateTime Local(DateTime utc) => Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
        string Day(DateTime utc) => Local(utc).ToString("d MMM", null);
        string Moment(DateTime utc) => Local(utc).ToString("d MMM, HH:mm", null);

        // A pass that is never exact inside the forecast either turns back short of it,
        // or was exact before the forecast began, or will be after it ends.
        var first = new LocalDate(start.Year, start.Month, start.Day);
        DateTime startUtc = zone.AtStartOfDay(first).ToDateTimeUtc(), endUtc = zone.AtStartOfDay(first.PlusDays(days)).ToDateTimeUtc();
        string Closest(TransitPass p) =>
            p.EnterUtc is null && (p.PeakUtc - startUtc).TotalHours < 1
                ? $"Already past exact as this forecast begins ({FormatOrb(p.MinOrb)} from it and easing)"
            : p.LeaveUtc is null && (endUtc - p.PeakUtc).TotalHours < 1
                ? $"Still building as this forecast ends ({FormatOrb(p.MinOrb)} from exact)"
            : $"Closest {Day(p.PeakUtc)}, {FormatOrb(p.MinOrb)} from exact, then it turns back";

        DailyItem Item(TransitPass p)
        {
            string target = p.Target.IsAngle ? p.Target.Name : $"{p.Target.Symbol} {p.Target.Name}";

            var meta = new List<string>
            {
                p.IsExact
                    ? "Exact " + string.Join(" and ", p.ExactUtc.Select(Moment))
                    : Closest(p),
                (p.EnterUtc, p.LeaveUtc) switch
                {
                    ({ } e, { } l) => $"in effect {Day(e)} to {Day(l)}",
                    (null, { } l)  => $"already in effect, until {Day(l)}",
                    ({ } e, null)  => $"in effect from {Day(e)}, past the end of this forecast",
                    _              => "in effect throughout",
                },
            };
            if (p.TargetHouse is { } house)
                meta.Add($"natal {p.Target.Name} is in your {ChartInterpreter.Ordinal(house)} house " +
                         $"({Lookup(_c.Houses, house.ToString(), "this area of life")})");
            if (p.MoverRetrograde)
                meta.Add($"{p.Mover.Name()} is retrograde");

            return new DailyItem
            {
                Title = $"{p.Mover.Symbol()} {p.Mover.Name()} {p.Aspect.Verb()} your natal {target}",
                Meta = string.Join("  ·  ", meta),
                Text = TransitText(p.Mover, p.Tone, p.Target),
            };
        }

        var sections = passes
            .GroupBy(p => { var d = Local(p.PeakUtc); return (d.Year, d.Month); })
            .OrderBy(g => g.Key)
            .Select(g => new DailySection
            {
                Heading = new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMMM yyyy"),
                Items = g.Select(Item).ToList(),
            })
            .ToList();

        if (sections.Count == 0)
            sections.Add(new DailySection
            {
                Heading = "Nothing in this stretch",
                Items = [new DailyItem { Text = "No planet comes within a degree of an aspect to this chart in these days. Try a longer stretch, or include the faster planets." }],
            });

        return new ForecastReading
        {
            Name = natal.Celebrity.Name,
            Start = start,
            Days = days,
            ZoneId = zone.Id,
            Sections = sections,
            Passes = passes,
        };
    }

    // "0°23'"
    private static string FormatOrb(double orb)
    {
        int minutes = (int)Math.Round(orb * 60);
        return $"{minutes / 60}°{minutes % 60:D2}'";
    }

    // The forecast as plain text, for saving or pasting into notes.
    public static byte[] ForecastToText(ForecastReading f)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{f.Name} — Transits ahead");
        sb.AppendLine(f.RangeText);
        foreach (var section in f.Sections)
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
        sb.AppendLine($"Generated by Lore on {DateTime.Now:yyyy-MM-dd} — a prompt for reflection, not a prediction.");
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private List<DailyItem> SkyNotes(NatalChart natal, DaySky sky, bool timed)
    {
        var items = new List<DailyItem>();

        foreach (var station in sky.Stations)
        {
            string direction = station.TurnsRetrograde ? "Retrograde" : "Direct";
            string text = Lookup(_c.Stations, direction, "").Replace("{planet}", station.Planet.Name());
            if (text.Length == 0) continue;
            items.Add(new DailyItem
            {
                Title = $"{station.Planet.Symbol()} {station.Planet.Name()} turns {direction.ToLowerInvariant()} today",
                Text = text
            });
        }

        // Retrograde periods of the three personal planets are the ones people feel
        // and ask about; the outer planets are retrograde for months of every year.
        foreach (var planet in new[] { Planet.Mercury, Planet.Venus, Planet.Mars })
        {
            if (sky.Body(planet) is not { IsRetrograde: true } p) continue;
            if (sky.Stations.Any(s => s.Planet == planet)) continue;
            string text = Lookup(_c.Retrogrades, planet.Name(), "");
            if (text.Length == 0) continue;

            string? meta = null;
            if (timed)
            {
                int house = natal.GetHouseForLongitude(p.Longitude);
                meta = $"Passing through your {ChartInterpreter.Ordinal(house)} house " +
                       $"({Lookup(_c.Houses, house.ToString(), "this area of life")})";
            }
            items.Add(new DailyItem
            {
                Title = $"{planet.Symbol()} {planet.Name()} is retrograde in {p.Sign.Name()}",
                Meta = meta,
                Text = text
            });
        }

        return items;
    }

    // ── Wording helpers ───────────────────────────────────────────────────────

    // "Exact at 5:06 AM", "Building — exact around 14 October", "Easing, within 0.4°".
    private static string Timing(TransitEvent e, DaySky sky, DateTimeZone zone)
    {
        if (e.Phase == TransitPhase.Exact)
            return e.IsBackground ? "Exact today" : $"Exact at {Time(e.PeakUtc, zone)}";

        string phase = e.Phase == TransitPhase.Building ? "Building" : "Easing";
        if (e.ExactUtc is not { } exact)
            return $"{phase}, within {e.MinOrb:0.0}°";

        var local = Local(exact, zone);
        string day = local.Year == sky.Date.Year ? local.ToString("d MMMM") : local.ToString("d MMMM yyyy");
        string when = e.IsBackground ? $"around {day}" : $"{day}, {local:t}";
        return e.Phase == TransitPhase.Building ? $"Building — exact {when}" : $"Easing — was exact {when}";
    }

    private static string TraceLine(TransitEvent e, DaySky sky, DateTimeZone zone)
    {
        string shown = e.Shown ? "   ✓ in the reading" : "";
        return $"{e.Mover.Symbol()} {e.Aspect.Symbol()} {e.Target.Symbol}   " +
               $"{e.Mover.Name()} {e.Aspect.Verb()} natal {e.Target.Name}  ·  " +
               $"closest {e.MinOrb:0.00}°  ·  {Timing(e, sky, zone)}{shown}";
    }

    private static DateTime Local(DateTime utc, DateTimeZone zone) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(zone).ToDateTimeUnspecified();

    private static string Time(DateTime utc, DateTimeZone zone) => Local(utc, zone).ToString("t");

    private string Note(string key, string fallback) => Lookup(_c.Notes, key, fallback);

    private static string Lookup(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
