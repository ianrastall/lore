using Lore.Models;
using System.Globalization;
using System.Text;

namespace Lore.Services;

// Builds the worksheet for a chart: the calculation laid bare, with nothing interpreted.
// Pure arithmetic on an already-calculated NatalChart (plus the time-zone lookup that
// explains how the birth time became Universal Time).
public static class WorksheetService
{
    public static Worksheet Build(NatalChart chart)
    {
        bool timed = chart.Timed;

        var metrics = NatalMetricsService.Compute(chart);

        // The bodies, then the angles, then the points worked out from them. (The South
        // Node needs no birth time; the rest of the derived points do.)
        var positions = chart.Planets.Select(p => BodyRow(chart, p)).ToList();
        if (timed)
        {
            positions.Add(PointRow(chart, "↑", "Ascendant", chart.Ascendant, house: false));
            positions.Add(PointRow(chart, "MC", "Midheaven", chart.Midheaven, house: false));
        }
        foreach (var point in metrics.Points)
            positions.Add(PointRow(chart, point.Symbol, point.Name, point.Longitude,
                house: timed && point.Name is not ("Descendant" or "Imum Coeli")));
        // Last, for comparison: the node and Lilith of the kind not in use.
        foreach (var other in chart.Alternates)
            positions.Add(BodyRow(chart, other.Position) with { Name = other.Name });

        var points = chart.Planets.Select(p => NatalPoint.Of(p.Planet)).ToList();
        if (timed)
        {
            points.Add(NatalPoint.Ascendant);
            points.Add(NatalPoint.Midheaven);
            points.Add(NatalPoint.Vertex);
            if (chart.LongitudeOf(NatalPoint.Fortune) is not null) points.Add(NatalPoint.Fortune);
        }

        return new Worksheet
        {
            Name = chart.Celebrity.Name,
            Facts = Facts(chart),
            Positions = positions,
            Cusps = timed
                ? chart.Houses.Select(h => new WorksheetCusp(h.House.ToString(), Dms(h.Longitude))).ToList()
                : [],
            Points = points,
            Aspects = Aspects(chart),
            Sections = Sections(chart, metrics),
        };
    }

    // ── How it was calculated ─────────────────────────────────────────────────

    private static List<WorksheetFact> Facts(NatalChart chart)
    {
        var c = chart.Celebrity;
        var (utc, offset) = BirthTimeResolver.Explain(c);

        var facts = new List<WorksheetFact>
        {
            new("Born", chart.Timed
                ? $"{c.BirthDate} at {c.BirthTime}, clock time at the birthplace"
                : $"{c.BirthDate}, time unknown — calculated for 12:00 noon"),
            new("Calendar", c.JulianCalendar
                ? $"Old Style (Julian) date; in the Gregorian calendar it is {BirthTimeResolver.GregorianDate(c).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                : "Gregorian"),
            new("Place", $"{c.BirthPlace}  ·  {Coordinate(c.Latitude, 'N', 'S')}, {Coordinate(c.Longitude, 'E', 'W')}"),
            new("Reliability", string.IsNullOrWhiteSpace(c.RoddenRating)
                ? "Not rated"
                : $"Rodden rating {RoddenRating.Describe(c.RoddenRating)}"),
            new("Source", string.IsNullOrWhiteSpace(c.Source) ? "Not recorded" : c.Source),
            new("Time conversion", offset),
            new("Universal Time", utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UT"),
            new("Zodiac", "Tropical, geocentric"),
            new("Houses", chart.Timed ? chart.HouseSystemLabel : "None — they need a birth time"),
            new("North Node", chart.Settings.Node.Name()),
            new("Lilith", chart.Settings.Lilith == LilithType.True
                ? "True (osculating) lunar apogee (Black Moon)"
                : "Mean lunar apogee (Black Moon)"),
        };
        if (chart.Timed)
        {
            facts.Add(new("Sect", chart.IsDayChart
                ? "Day chart — the Sun is above the horizon"
                : "Night chart — the Sun is below the horizon"));
            facts.Add(new("Part of Fortune", chart.IsDayChart
                ? "Ascendant + Moon − Sun (day formula)"
                : "Ascendant + Sun − Moon (night formula)"));
            facts.Add(new("Part of Spirit", chart.IsDayChart
                ? "Ascendant + Sun − Moon (day formula)"
                : "Ascendant + Moon − Sun (night formula)"));
            facts.Add(new("Sidereal time", SiderealTime(chart.Armc) + " at the birthplace"));
        }
        if (chart.Obliquity is { } obliquity)
        {
            int seconds = (int)(obliquity * 3600);
            facts.Add(new("Obliquity", $"{seconds / 3600}°{seconds / 60 % 60:D2}'{seconds % 60:D2}\" (the tilt of the Earth's axis, true of date)"));
        }
        facts.Add(new("Aspect orbs", chart.Settings.Orbs.Describe()));
        facts.Add(new("Engine", chart.EphemerisNote.Length == 0
            ? "Swiss Ephemeris 2.10.03"
            : $"Swiss Ephemeris 2.10.03 — {chart.EphemerisNote}"));
        return facts;
    }

    // ── Positions ─────────────────────────────────────────────────────────────

    private static WorksheetRow BodyRow(NatalChart chart, PlanetPosition p) => new(
        p.PlanetSymbol, p.PlanetName, Dms(p.Longitude),
        Signed(p.Latitude), Signed(p.Declination),
        p.SpeedLongitude.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture) + "°",
        chart.Timed ? chart.GetHouseForLongitude(p.Longitude).ToString() : "",
        p.MotionMark);

    private static WorksheetRow PointRow(NatalChart chart, string symbol, string name, double longitude, bool house) => new(
        symbol, name, Dms(longitude), "", "", "",
        house ? chart.GetHouseForLongitude(longitude).ToString() : "", "");

    // The Lot of Fortune, reversed by sect as the traditional sources give it: by day
    // Ascendant + Moon − Sun, by night Ascendant + Sun − Moon.
    public static double? PartOfFortune(NatalChart chart) => NatalMetricsService.Lot(chart, spirit: false);

    // ── Further measurements ──────────────────────────────────────────────────

    private static List<WorksheetSection> Sections(NatalChart chart, NatalMetrics m)
    {
        var sections = new List<WorksheetSection>();
        static IReadOnlyList<string> Line(string label, string value) => [label, value];
        static string Body(Planet p) => $"{p.Symbol()} {p.Name()}";

        // The Moon's phase, and each body's distance from the Sun.
        if (m.Moon is { } moon)
        {
            var rows = new List<IReadOnlyList<string>>
            {
                Line("Lunar phase", $"{moon.Phase.Name()} — the Moon is {Arc(moon.Elongation)} ahead of the Sun"),
                Line("Lit", $"about {moon.Illumination * 100:0}% of the disc, {(moon.Waxing ? "waxing" : "waning")}"),
            };
            sections.Add(new("The Moon's phase", [], rows, chart.Timed
                ? "The phase is named for the eighth of the cycle the Moon is in, each centred on its exact phase: Full Moon runs from 157°30' to 202°30' ahead of the Sun."
                : "With no birth time these are the figures for noon. The Moon moves about 13° in a day, so the angle may be 6° or 7° out either way and the phase may be the one before or after."));
        }

        if (m.Solar.Count > 0)
            sections.Add(new("Distance from the Sun", ["", "From the Sun", "Side", "Seen", "Condition"],
                m.Solar.Select(r => (IReadOnlyList<string>)
                [
                    Body(r.Planet), Arc(r.Elongation), r.EastOfSun ? "east" : "west", r.EastOfSun ? "evening" : "morning",
                    r.Condition switch
                    {
                        SolarCondition.Cazimi => "cazimi",
                        SolarCondition.Combust => "combust",
                        SolarCondition.UnderBeams => "under the beams",
                        _ => ""
                    },
                ]).ToList(),
                "Measured along the zodiac. A body east of the Sun follows it and sets after it; one to the west rises before it. For the Moon to Saturn: cazimi is within 0°17' of the Sun, combust within 8°30', under the beams within 17°."));

        // Declination: out of bounds, parallels and contra-parallels.
        if (chart.Planets.Any(p => p.HasEquatorial))
        {
            var rows = new List<IReadOnlyList<string>>
            {
                Line("Out of bounds", m.Obliquity is null ? "Not known"
                    : m.OutOfBounds.Count == 0 ? "None"
                    : string.Join("; ", m.OutOfBounds.Select(o =>
                        $"{o.Planet.Name()} at {Signed(o.Declination)}, {Arc(o.Excess)} beyond the Sun's limit"))),
            };
            rows.AddRange(m.Parallels.Select(c => Line(c.Contra ? "Contra-parallel" : "Parallel",
                $"{c.A.Name()} and {c.B.Name()}, {Arc(c.Orb)} {(c.Applying ? "a" : "s")}")));
            if (m.Parallels.Count == 0) rows.Add(Line("Parallels", "None within 1°"));
            sections.Add(new("Declination", [], rows,
                "A body is out of bounds when it is further from the celestial equator than the Sun ever gets (the obliquity, above). " +
                "Two bodies are parallel when they are within 1° of the same declination on the same side of the equator, and " +
                "contra-parallel when within 1° of equal declinations on opposite sides. a = applying, s = separating."));
        }

        if (m.Angularity.Count > 0)
            sections.Add(new("Angles and houses", ["", "House", "Past its cusp", "Nearest angle", "Away"],
                m.Angularity.Select(a => (IReadOnlyList<string>)
                    [Body(a.Planet), a.House.ToString(), Arc(a.IntoHouse), a.NearestAngle, Arc(a.Distance)]).ToList(),
                "Distances along the zodiac: how far each body is past the cusp of its house, and how far from the nearest of the Ascendant, Midheaven, Descendant and IC."));

        sections.Add(ByDegree(chart));

        // The balance of the chart.
        sections.Add(ElementsByMode(chart));
        {
            var d = m.Distribution;
            string Spread(IReadOnlyList<Tally> tallies) => string.Join("  ·  ",
                tallies.Select(t => $"{t.Name} {t.Count} ({(d.Total == 0 ? 0 : 100.0 * t.Count / d.Total):0}%)"));
            var rows = new List<IReadOnlyList<string>>
            {
                Line("Elements", Spread(d.Elements)),
                Line("Modes", Spread(d.Modalities)),
                Line("Polarity", Spread(d.Polarities)),
            };
            if (d.HouseTypes.Count > 0)
            {
                rows.Add(Line("Houses", Spread(d.HouseTypes)));
                rows.Add(Line("Above and below", Spread(d.Hemispheres.Take(2).ToList())));
                rows.Add(Line("East and west", Spread(d.Hemispheres.Skip(2).ToList())));
            }
            sections.Add(new("Balance", [], rows,
                $"Counted over the {d.Total} bodies in the chart, each counting once: the ten planets, the North Node, Chiron and Lilith — the same tally the Report reads." +
                (d.HouseTypes.Count > 0 ? " The halves of the chart are taken by house, so they follow the house system chosen." : "")));
        }

        // The aspects in sum.
        {
            var a = m.Aspects;
            var rows = new List<IReadOnlyList<string>>
            {
                Line("Between the bodies", a.ByType.Count == 0 ? "None"
                    : $"{a.Applying + a.Separating} in all  ·  " + string.Join("  ·  ", a.ByType.Select(t => $"{t.Name} {t.Count}"))),
                Line("Applying, separating", $"{a.Applying} applying  ·  {a.Separating} separating"),
            };
            if (a.Closest is { } closest)
                rows.Add(Line("Closest", $"{closest.PlanetA.Name()} {closest.Type.Name().ToLowerInvariant()} {closest.PlanetB.Name()}, {Arc(closest.Orb)} from exact"));
            rows.Add(Line("Most aspected", string.Join("  ·  ", a.PerBody.Take(3).Select(t => $"{t.Name} {t.Count}"))));
            rows.Add(Line("Unaspected", a.Unaspected.Count == 0 ? "None" : string.Join(", ", a.Unaspected.Select(p => p.Name()))));
            sections.Add(new("Aspects in sum", [], rows,
                "Aspects between two bodies, not those to the Ascendant and Midheaven. A planet (Sun to Pluto) is unaspected when it makes no major aspect to any other body on the orbs in force; wider orbs would leave fewer."));
        }

        // Rulers.
        {
            var r = m.Rulers;
            var rows = new List<IReadOnlyList<string>>();
            if (r.ChartRuler is { } ruler)
            {
                var at = chart.GetPlanet(ruler);
                rows.Add(Line("Chart ruler",
                    $"{ruler.Name()}, ruler of {ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name()} rising" +
                    (at is null ? "" : $", in {at.Sign.Name()} in house {chart.GetHouseForLongitude(at.Longitude)}") +
                    (r.ModernChartRuler is { } modern ? $" (modern ruler: {modern.Name()})" : "")));
            }
            rows.Add(Line("In its own sign", r.InDomicile.Count == 0 ? "None" : string.Join(", ", r.InDomicile.Select(p => p.Name()))));
            rows.Add(Line("Final dispositor", r.FinalDispositor is { } final
                ? final.Name()
                : "None — the chains below do not all end at one planet"));
            foreach (var loop in r.Loops)
                rows.Add(Line(loop.Count == 2 ? "Mutual reception" : "Ring of rulers",
                    string.Join(loop.Count == 2 ? " and " : " → ", loop.Select(p => p.Name())) +
                    (loop.Count == 2 ? " are each in the other's sign" : $" → {loop[0].Name()}")));
            sections.Add(new("Rulers", [], rows,
                "By the traditional rulerships: Mars rules Scorpio, Saturn Aquarius, Jupiter Pisces."));

            if (r.Houses.Count > 0)
                sections.Add(new("House rulers", ["House", "Sign on the cusp", "Ruler", "Ruler is in"],
                    r.Houses.Select(h => (IReadOnlyList<string>)
                    [
                        h.House.ToString(), h.CuspSign.Name(), Body(h.Ruler),
                        h.RulerSign is { } sign ? $"{sign.Name()}, house {h.RulerHouse}" : "",
                    ]).ToList()));

            sections.Add(new("Dispositors", ["", "Sign ruler", "Term", "Face", "Chain"],
                r.Dispositions.Select(d => (IReadOnlyList<string>)
                [
                    Body(d.Planet), d.SignRuler.Name(), d.TermRuler.Name(), d.FaceRuler.Name(),
                    // A chain that ends on a planet in its own sign names it once, not twice.
                    string.Join(" → ", (d.Chain.Count > 1 && d.Chain[^1] == d.Chain[^2] ? d.Chain.SkipLast(1) : d.Chain)
                        .Select(p => p.Name())),
                ]).ToList(),
                "The ruler of the sign a body is in is its dispositor; the chain follows dispositor to dispositor until it reaches a planet in its own sign or comes back on itself. Terms are the Egyptian bounds and faces the Chaldean decans, as in the dignity score."));
        }

        return sections;
    }

    // Everything in the chart in order of its degree within its sign, whatever the sign.
    // Points at nearly the same degree are in aspect by sign (or nearly so), which is
    // how an astrologer scans a chart for contacts by eye.
    private static WorksheetSection ByDegree(NatalChart chart)
    {
        var all = chart.Planets
            .Select(p => (Name: $"{p.PlanetSymbol} {p.PlanetName}", p.Longitude))
            .ToList();
        if (chart.Timed)
        {
            all.Add(("↑ Ascendant", chart.Ascendant));
            all.Add(("MC Midheaven", chart.Midheaven));
        }
        return new("By degree", ["Degree", "", "Sign"],
            all.OrderBy(x => ZodiacSignExtensions.DegreeInSign(x.Longitude))
               .Select(x => (IReadOnlyList<string>)
               [
                   ZodiacSignExtensions.FormatDegreeInSign(x.Longitude), x.Name,
                   ZodiacSignExtensions.FromLongitude(x.Longitude).Name(),
               ]).ToList(),
            "Every body" + (chart.Timed ? ", and the Ascendant and Midheaven," : "") +
            " in order of its degree within its sign, 0° to 30°, whatever the sign. Neighbours in this list are at " +
            "nearly the same degree of their signs, which is where the major aspects fall.");
    }

    // Which bodies are in each element and mode: the twelve signs as a grid of four by
    // three, one sign to a cell.
    private static WorksheetSection ElementsByMode(NatalChart chart)
    {
        var modes = Enum.GetValues<Modality>();
        var rows = Enum.GetValues<Element>().Select(element => (IReadOnlyList<string>)
        [
            element.ToString(),
            .. modes.Select(mode =>
            {
                var here = chart.Planets.Where(p => p.Sign.GetElement() == element && p.Sign.GetModality() == mode).ToList();
                return here.Count == 0 ? "·" : string.Join(" ", here.Select(p => p.PlanetSymbol));
            }),
        ]).ToList();
        return new("Elements and modes", ["", .. modes.Select(m => m.ToString())], rows,
            "Each cell is one sign (fire and cardinal is Aries, and so on) and holds the bodies in it. The counts are under Balance.");
    }

    // An arc as degrees and minutes, cut off at the minute: 2°18'.
    private static string Arc(double degrees)
    {
        int total = (int)(Math.Abs(degrees) * 60);
        return $"{total / 60}°{total % 60:D2}'";
    }

    // The sidereal time, given in degrees, as hours, minutes and seconds.
    private static string SiderealTime(double armc)
    {
        int total = (int)(Normalize(armc) / 15 * 3600);
        return $"{total / 3600}h {total / 60 % 60:D2}m {total % 60:D2}s";
    }

    // ── Aspects ───────────────────────────────────────────────────────────────

    // The chart's planet-to-planet aspects and its aspects to the Ascendant, Midheaven,
    // Vertex and Part of Fortune, closest first. (The last two are found here, for the
    // Worksheet alone: the report and the wheel do not read them.)
    private static List<WorksheetAspect> Aspects(NatalChart chart)
    {
        var aspects = chart.Aspects
            .Select(a => new WorksheetAspect(NatalPoint.Of(a.PlanetA), NatalPoint.Of(a.PlanetB), a.Type, a.Orb, a.IsApplying, a.OutOfSign))
            .ToList();

        aspects.AddRange(chart.AngleAspects.Select(a =>
            new WorksheetAspect(NatalPoint.Of(a.Planet), a.Angle, a.Type, a.Orb, a.IsApplying, a.OutOfSign)));

        if (chart.Timed)
        {
            var extra = new List<(NatalPoint, double)> { (NatalPoint.Vertex, chart.Vertex) };
            if (chart.LongitudeOf(NatalPoint.Fortune) is { } fortune) extra.Add((NatalPoint.Fortune, fortune));
            aspects.AddRange(ChartService.AspectsToPoints(chart.Planets, extra, chart.Settings.Orbs).Select(a =>
                new WorksheetAspect(NatalPoint.Of(a.Planet), a.Angle, a.Type, a.Orb, a.IsApplying, a.OutOfSign)));
        }

        aspects.Sort((a, b) => a.Orb.CompareTo(b.Orb));
        return aspects;
    }

    // ── Plain text (export, and copying into notes) ───────────────────────────

    public static byte[] ToText(Worksheet w, TimeSensitivity? sensitivity = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{w.Name} — Worksheet");

        sb.AppendLine();
        sb.AppendLine("HOW THIS WAS CALCULATED");
        foreach (var f in w.Facts)
            sb.AppendLine($"{f.Label + ":",-18}{f.Value}");

        sb.AppendLine();
        sb.AppendLine("POSITIONS");
        sb.AppendLine($"{"",-19}{"Position",-25}{"Latitude",-11}{"Declin.",-11}{"Speed/day",-11}{"House",-6}");
        foreach (var r in w.Positions)
            sb.AppendLine($"{r.Name,-19}{r.Position,-25}{r.Latitude,-11}{r.Declination,-11}{r.Speed,-11}{r.House,-6}{MotionWord(r.Motion)}".TrimEnd());

        if (w.HasCusps)
        {
            sb.AppendLine();
            sb.AppendLine("HOUSE CUSPS");
            foreach (var c in w.Cusps)
                sb.AppendLine($"{c.House,-4}{c.Position}");
        }

        sb.AppendLine();
        sb.AppendLine("ASPECTS (closest first; a = applying, s = separating; out of sign = within orb, but the signs are not in that aspect)");
        foreach (var a in w.Aspects)
            sb.AppendLine($"{a.A.Name,-12}{a.Type.Name(),-16}{a.B.Name,-17}{a.OrbText}{(a.OutOfSign ? "  out of sign" : "")}");

        foreach (var section in w.Sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.Title.ToUpperInvariant());
            // Each column as wide as its widest cell; the last runs free.
            var lines = section.Headers.Count > 0 ? section.Rows.Prepend(section.Headers).ToList() : section.Rows.ToList();
            int columns = lines.Max(l => l.Count);
            var widths = Enumerable.Range(0, columns)
                .Select(i => lines.Max(l => i < l.Count ? l[i].Length : 0) + 2).ToArray();
            foreach (var line in lines)
                sb.AppendLine(string.Concat(line.Select((cell, i) => i == line.Count - 1 ? cell : cell.PadRight(widths[i]))).TrimEnd());
            if (section.Note.Length > 0) sb.AppendLine($"({section.Note})");
        }

        if (sensitivity is not null)
        {
            sb.AppendLine();
            sb.AppendLine(sensitivity.WholeDay
                ? "WITHOUT A BIRTH TIME (the whole day, midnight to midnight at the birthplace)"
                : $"IF THE BIRTH TIME IS OFF BY UP TO {sensitivity.Minutes} MINUTES ({sensitivity.Window})");
            sb.AppendLine(sensitivity.Summary);
            foreach (var line in sensitivity.Changes) sb.AppendLine($"  changes  {line}");
            foreach (var line in sensitivity.Holds) sb.AppendLine($"  holds    {line}");
        }

        sb.AppendLine();
        sb.AppendLine($"Generated by Lore on {DateTime.Now:yyyy-MM-dd}.");

        // UTF-8 with a byte-order mark, so Notepad and friends show the symbols correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private static string MotionWord(string mark) => mark switch
    {
        "℞" => "retrograde",
        "S" => "stationary",
        "S℞" => "stationary, retrograde",
        _ => ""
    };

    // A longitude as degrees, minutes and seconds within its sign: 12°39'27" Taurus.
    // Cut off at the second, never rounded up into the next one.
    public static string Dms(double longitude)
    {
        double d = ZodiacSignExtensions.DegreeInSign(longitude);
        int total = Math.Min(30 * 3600 - 1, (int)(d * 3600));
        return $"{total / 3600}°{total / 60 % 60:D2}'{total % 60:D2}\" {ZodiacSignExtensions.FromLongitude(longitude).Name()}";
    }

    // A latitude or declination: +12°34' north of the ecliptic or equator, − south.
    private static string Signed(double degrees)
    {
        double abs = Math.Abs(degrees);
        int total = (int)(abs * 60);
        // Under half an arc-minute either way is simply zero, not "−0°00'".
        return $"{(total == 0 ? "" : degrees < 0 ? "−" : "+")}{total / 60}°{total % 60:D2}'";
    }

    private static string Coordinate(double value, char positive, char negative)
    {
        int total = (int)Math.Round(Math.Abs(value) * 60);
        return $"{total / 60}°{total % 60:D2}'{(value < 0 ? negative : positive)}";
    }

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    private static double AngleBetween(double lonA, double lonB)
    {
        double diff = Math.Abs(lonA - lonB) % 360;
        return diff > 180 ? 360 - diff : diff;
    }
}
