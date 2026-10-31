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
            if (chart.LongitudeOf(NatalPoint.Spirit) is not null) points.Add(NatalPoint.Spirit);
        }

        return new Worksheet
        {
            Name = chart.Celebrity.Name,
            Facts = Facts(chart, metrics),
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

    private static List<WorksheetFact> Facts(NatalChart chart, NatalMetrics metrics)
    {
        var c = chart.Celebrity;
        var (utc, offset) = BirthTimeResolver.Explain(c);

        // A Davison chart is not a birth: it has no clock time to convert, and its moment
        // is the one it was cast for, whatever the noon rule would say.
        bool davison = c.Category == ChartService.DavisonCategory;
        if (davison)
        {
            utc = chart.CalculatedForUtc;
            offset = "None: the moment is worked out in Universal Time";
        }

        var facts = new List<WorksheetFact>
        {
            new("Born", davison
                ? "Not a birth: the moment halfway between two births" +
                  (chart.Timed ? "" : " (a birth time is missing, so it may be hours out)")
                : chart.Timed
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
            new("Julian day", SwissEphemeris.DateTimeToJulianDay(utc).ToString("0.00000", CultureInfo.InvariantCulture) +
                              " (days counted from noon on 1 January 4713 BC, in Universal Time)"),
            new("Zodiac", "Tropical, geocentric"),
            new("Houses", chart.Timed ? chart.HouseSystemLabel : "None — they need a birth time"),
            new("North Node", chart.Settings.Node.Name()),
            new("Lilith", chart.Settings.Lilith == LilithType.True
                ? "True (osculating) lunar apogee (Black Moon)"
                : "Mean lunar apogee (Black Moon)"),
        };
        if (chart.Timed)
        {
            string height = metrics.Sky.FirstOrDefault(s => s.Planet == Planet.Sun)?.Altitude is { } altitude
                ? Arc(altitude) + " " : "";
            facts.Add(new("Sect", chart.IsDayChart
                ? $"Day chart — the Sun is {height}above the horizon"
                : $"Night chart — the Sun is {height}below the horizon"));
            facts.Add(new("Part of Fortune", chart.IsDayChart
                ? "Ascendant + Moon − Sun (day formula)"
                : "Ascendant + Sun − Moon (night formula)"));
            facts.Add(new("Part of Spirit", chart.IsDayChart
                ? "Ascendant + Sun − Moon (day formula)"
                : "Ascendant + Moon − Sun (night formula)"));
            facts.Add(new("Sidereal time", SiderealTime(chart.Armc) + " at the birthplace"));
            if (chart.Events?.Hour is { } hour)
                facts.Add(new("Planetary hour",
                    $"Day of {(hour.DayRuler is Planet.Sun or Planet.Moon ? "the " : "")}{hour.DayRuler.Name()}, " +
                    $"hour of {(hour.HourRuler is Planet.Sun or Planet.Moon ? "the " : "")}{hour.HourRuler.Name()} — " +
                    $"the {ChartInterpreter.Ordinal(hour.ByDay ? hour.Hour : hour.Hour - 12)} hour of the {(hour.ByDay ? "day" : "night")}, " +
                    "counted from sunrise at the birthplace"));
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
        p.SpeedLongitude.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) + "°",
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
            if (chart.Events is { } events)
            {
                string When(Lunation l) =>
                    $"{l.Utc.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)} UT, at {Place(l.Longitude)}" +
                    (l.Eclipse is { } kind ? $" ({An(kind)} {kind} {(l.Full ? "lunar" : "solar")} eclipse)" : "") +
                    $" — {Days(l.DaysBefore)} before" +
                    (ReferenceEquals(l, events.Syzygy) ? "; the lunation before birth" : "");
                if (events.NewMoonBefore is { } newMoon)
                {
                    rows.Add(Line("Age", $"{Days(newMoon.DaysBefore)} since the New Moon"));
                    rows.Add(Line("New Moon before", When(newMoon)));
                }
                if (events.FullMoonBefore is { } fullMoon)
                    rows.Add(Line("Full Moon before", When(fullMoon)));
            }
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

        // How fast each planet is going, and how near it was to standing still.
        if (m.Motion.Count > 0)
        {
            var stations = chart.Events?.Stations;
            string Rate(double perDay) => perDay.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) + "°";
            string Station(Planet planet) => stations?.FirstOrDefault(s => s.Planet == planet)?.Nearest is not { } s ? ""
                : s == stations!.First(x => x.Planet == planet).Before
                    ? $"turned {(s.TurnsRetrograde ? "retrograde" : "direct")} {Days(s.Days)} before"
                    : $"turns {(s.TurnsRetrograde ? "retrograde" : "direct")} {Days(s.Days)} after";
            var headers = new List<string> { "", "Per day", "Of its average", "In latitude", "In declination" };
            if (stations is not null) headers.Add("Nearest station");
            sections.Add(new("Motion", headers,
                m.Motion.Select(pace =>
                {
                    var p = chart.GetPlanet(pace.Planet)!;
                    var row = new List<string>
                    {
                        Body(pace.Planet), Rate(pace.Speed),
                        pace.Ratio is { } ratio ? $"{ratio * 100:0}% — {(ratio > 1 ? "swift" : "slow")}" : "",
                        Rate(p.SpeedLatitude), p.HasEquatorial ? Rate(p.SpeedDeclination) : "",
                    };
                    if (stations is not null) row.Add(Station(pace.Planet));
                    return (IReadOnlyList<string>)row;
                }).ToList(),
                "Degrees a day along the zodiac, and for the Sun to Saturn that speed beside the planet's average (whichever way it is going): " +
                "above 100% it is swift, below it slow. The averages are the traditional ones: 0°59' a day for the Sun, Mercury and Venus, 13°11' for the Moon, " +
                "0°31' for Mars, 0°05' for Jupiter and 0°02' for Saturn. " +
                "The next two columns are its motion north (+) or south (−) of the ecliptic and of the equator." +
                (stations is null ? "" : " A station is the moment a planet stands still before turning back or forward; the nearer of the last one before birth and the first one after is given.")));
        }

        // Where each body is by the equator and by the horizon.
        if (m.Sky.Any(s => s.RightAscension is not null))
        {
            bool horizon = m.Sky.Any(s => s.Altitude is not null);
            var headers = new List<string> { "", "Right ascension", "Distance" };
            if (horizon) { headers.Add("Altitude"); headers.Add("Azimuth"); }
            sections.Add(new("Equator and horizon", headers,
                m.Sky.Select(s =>
                {
                    var row = new List<string>
                    {
                        Body(s.Planet),
                        s.RightAscension is { } ra ? HoursMinutes(ra) : "",
                        s.Distance is { } au ? au.ToString(au < 0.1 ? "0.00000" : "0.0000", CultureInfo.InvariantCulture) + " AU" : "",
                    };
                    if (horizon)
                    {
                        row.Add(s.Altitude is { } altitude ? Signed(altitude) : "");
                        row.Add(s.Azimuth is { } azimuth ? $"{Arc(azimuth)} {Compass(azimuth)}" : "");
                    }
                    return (IReadOnlyList<string>)row;
                }).ToList(),
                "Right ascension is a body's place round the celestial equator, in hours (24 to the circle), as declination is its distance from it. " +
                "Distance is from the Earth, in astronomical units; one is the Earth's mean distance from the Sun." +
                (horizon ? " Altitude is height above (+) or below (−) the horizon of the place the chart is cast for, and azimuth the compass bearing, " +
                           "from north round through east. Both are reckoned as the Ascendant is, from the centre of the Earth and without the bending of light " +
                           "by the air, so a body within a degree of the horizon may have been seen on the other side of it; for the Moon the difference can reach a degree."
                         : "")));
        }

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
                rows.Add(Line("Quadrants", Spread(d.Quadrants)));
                rows.Add(Line("By house", string.Join("  ·  ", d.PerHouse
                    .Select((count, i) => (House: i + 1, count)).Where(x => x.count > 0)
                    .Select(x => $"{ChartInterpreter.Ordinal(x.House)} {x.count}"))));
            }
            if (m.Spread is { } spread)
                rows.Add(Line("Spread", $"the ten planets lie within {Arc(spread.Arc)} of the zodiac, from {spread.First.Name()} round to {spread.Last.Name()}, " +
                                        $"in {spread.SignsOccupied} signs; the widest empty stretch is {Arc(spread.LargestGap)}"));
            sections.Add(new("Balance", [], rows,
                $"Counted over the {d.Total} bodies in the chart, each counting once: the ten planets, the North Node, Chiron and Lilith — the same tally the Report reads." +
                (d.HouseTypes.Count > 0 ? " The halves and quadrants of the chart are taken by house, so they follow the house system chosen." : "") +
                (m.Spread is null ? "" : " The spread is measured over the ten planets, Sun to Pluto, going forward through the signs.")));
        }

        // Which planet and which signs carry most weight.
        {
            var d = m.Dominants;
            string Points(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);
            sections.Add(new("Dominant planets", ["", "Points", "Made up of"],
                d.Planets.Select(p => (IReadOnlyList<string>)
                    [Body(p.Planet), Points(p.Score), p.Parts.Count == 0 ? "—" : string.Join("  ·  ", p.Parts)]).ToList(),
                "A weighted reckoning of which planet stands out, strongest first. The Sun and Moon start with 3 each. Points then come from four things: standing within 10° of an angle " +
                "(up to 10 at the Ascendant, 8 at the Midheaven, 6 at the Descendant or IC); each major aspect to another planet, more for a closer one " +
                "(conjunction up to 4, opposition, square and trine 3, sextile 2; half between two of Uranus, Neptune and Pluto); being in its own sign (5) " +
                "or exalted (4); and ruling the rising sign (8), the Sun's sign (5), the Moon's (4) or the Midheaven's (3), with half as much for a modern ruler. " +
                "The weights are Lore's own; other programs weigh these things differently and may name another planet." +
                (chart.Timed ? "" : " With no birth time the angles are unknown and take no part.")));

            string Spread(IReadOnlyList<Weight> weights) => string.Join("  ·  ",
                weights.Select(w => $"{w.Name} {w.Points:0} ({(d.TotalWeight == 0 ? 0 : 100 * w.Points / d.TotalWeight):0}%)"));
            sections.Add(new("Weighted balance", [],
                [
                    Line("Elements", Spread(d.Elements)),
                    Line("Modes", Spread(d.Modalities)),
                    Line("Signs", Spread(d.Signs.Take(4).ToList())),
                ],
                "The same count as under Balance, but with the points that matter most counting for more: the Sun, Moon" +
                (chart.Timed ? " and Ascendant" : "") + " 3 each, Mercury, Venus and Mars 2, Jupiter to Pluto" +
                (chart.Timed ? " and the Midheaven" : "") + " 1. The North Node, Chiron and Lilith are left out. The four heaviest signs are shown." +
                (chart.Timed ? "" : " With no birth time the Ascendant and Midheaven are left out too.")));
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

        // Midpoints: who stands halfway between whom.
        if (m.Midpoints.Count > 0)
        {
            static string Name(NatalPoint point) => point.IsAngle ? point.Name : $"{point.Symbol} {point.Name}";
            var sunMoon = m.Midpoints.FirstOrDefault(x => x.A == NatalPoint.Of(Planet.Sun) && x.B == NatalPoint.Of(Planet.Moon));
            sections.Add(new("Midpoints", ["", "Stands midway between", "From exact"],
                m.MidpointContacts.Count == 0
                    ? [["None within 1°", "", ""]]
                    : m.MidpointContacts.Select(c => (IReadOnlyList<string>)
                        [Name(c.Point), $"{c.A.Name} and {c.B.Name}", Arc(c.Orb)]).ToList(),
                "A midpoint is the degree halfway between two points, and the degree opposite it; a third point within 1° of either is listed, closest first. " +
                "Taken between the ten planets, the North Node" + (chart.Timed ? ", the Ascendant and the Midheaven" : "") + "." +
                (sunMoon is null ? "" : $" The Sun/Moon midpoint is at {Place(sunMoon.Longitude)}.") +
                (chart.Timed ? "" : " With no birth time the Moon is placed for noon and its midpoints may be 3° out either way.")));
        }

        // Antiscia: reflections in the solstice and equinox axes.
        if (m.Antiscia.Count > 0)
        {
            static string Name(NatalPoint point) => point.IsAngle ? point.Name : $"{point.Symbol} {point.Name}";
            string Meets(NatalPoint point) => string.Join("; ", m.AntisciaContacts
                .Where(c => c.A == point || c.B == point)
                .Select(c => $"{(c.A == point ? c.B : c.A).Name} by {(c.Contra ? "contra-antiscion" : "antiscion")}, {Arc(c.Orb)}"));
            sections.Add(new("Antiscia", ["", "Antiscion", "Contra-antiscion", "Meets"],
                m.Antiscia.Select(a => (IReadOnlyList<string>)
                    [Name(a.Point), Place(a.Longitude), Place(a.Contra), Meets(a.Point)]).ToList(),
                "A point's antiscion is its reflection across the line from 0° Cancer to 0° Capricorn: the degree where the day is as long as at its own. " +
                "The contra-antiscion is the point opposite that, its reflection across 0° Aries to 0° Libra. Two points meet when one stands within 1° of the other's reflection, " +
                "which the tradition reads like a conjunction (antiscion) or an opposition (contra-antiscion)."));
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

            sections.Add(new("Triplicity rulers", ["", "Element", "By day", "By night", "Sharing"],
                r.Dispositions.Select(d => (IReadOnlyList<string>)
                [
                    Body(d.Planet), d.Sign.GetElement().ToString(),
                    d.Triplicity[0].Name(), d.Triplicity[1].Name(), d.Triplicity[2].Name(),
                ]).ToList(),
                "Each element has three rulers, as Dorotheus gives them: one that leads by day, one by night, and a third that shares with both." +
                (chart.Timed ? $" This is a {(chart.IsDayChart ? "day" : "night")} chart, so the {(chart.IsDayChart ? "day" : "night")} ruler leads." : "")));

            if (m.Almutens.Count > 0)
                sections.Add(new("Almutens", ["Place", "At", "Almuten", "Points"],
                    m.Almutens.Select(a => (IReadOnlyList<string>)
                    [
                        a.Point, Place(a.Longitude), string.Join(" and ", a.Rulers.Select(p => p.Name())), a.Points.ToString(),
                    ]).ToList(),
                    "The almuten of a degree is the planet with most say over it: 5 points for ruling its sign, 4 for exaltation there, " +
                    "3 for ruling its triplicity (the day ruler in a day chart, the night ruler in a night chart), 2 for its term and 1 for its face — " +
                    "the scale the dignity score uses. Planets level on points are named together. Other authors give the triplicity to all three rulers, " +
                    "or weigh the five differently, and may name another planet."));
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

    // A degree of the zodiac, to the minute: 21°33' Pisces.
    private static string Place(double longitude) =>
        $"{ZodiacSignExtensions.FormatDegreeInSign(longitude)} {ZodiacSignExtensions.FromLongitude(longitude).Name()}";

    // A stretch of days: to a tenth under ten days, whole days beyond.
    private static string Days(double days) =>
        days < 10 ? $"{days.ToString("0.0", CultureInfo.InvariantCulture)} days" : $"{days:0} days";

    private static string An(string word) => "aeiou".Contains(word[0]) ? "an" : "a";

    // An angle round the equator in hours and minutes of time: 15° to the hour.
    private static string HoursMinutes(double degrees)
    {
        int total = (int)(Normalize(degrees) / 15 * 60);
        return $"{total / 60}h {total % 60:D2}m";
    }

    // The nearest of the sixteen points of the compass to a bearing.
    private static string Compass(double azimuth)
    {
        string[] names = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];
        return names[(int)Math.Round(Normalize(azimuth) / 22.5) % 16];
    }

    // The sidereal time, given in degrees, as hours, minutes and seconds.
    private static string SiderealTime(double armc)
    {
        int total = (int)(Normalize(armc) / 15 * 3600);
        return $"{total / 3600}h {total / 60 % 60:D2}m {total % 60:D2}s";
    }

    // ── Aspects ───────────────────────────────────────────────────────────────

    // The bodies' aspects to the Vertex and the Parts of Fortune and Spirit, on the
    // chart's orbs. Found here, for the Worksheet and the data exports alone: the report
    // and the wheel do not read them. Empty without a birth time.
    public static List<AngleAspect> PointAspects(NatalChart chart)
    {
        if (!chart.Timed) return [];
        var extra = new List<(NatalPoint, double)> { (NatalPoint.Vertex, chart.Vertex) };
        if (chart.LongitudeOf(NatalPoint.Fortune) is { } fortune) extra.Add((NatalPoint.Fortune, fortune));
        if (chart.LongitudeOf(NatalPoint.Spirit) is { } spirit) extra.Add((NatalPoint.Spirit, spirit));
        return ChartService.AspectsToPoints(chart.Planets, extra, chart.Settings.Orbs);
    }

    // The chart's planet-to-planet aspects and its aspects to the Ascendant, Midheaven,
    // Vertex and Parts of Fortune and Spirit, closest first.
    private static List<WorksheetAspect> Aspects(NatalChart chart)
    {
        var aspects = chart.Aspects
            .Select(a => new WorksheetAspect(NatalPoint.Of(a.PlanetA), NatalPoint.Of(a.PlanetB), a.Type, a.Orb, a.IsApplying, a.OutOfSign))
            .ToList();

        aspects.AddRange(chart.AngleAspects.Select(a =>
            new WorksheetAspect(NatalPoint.Of(a.Planet), a.Angle, a.Type, a.Orb, a.IsApplying, a.OutOfSign)));

        aspects.AddRange(PointAspects(chart).Select(a =>
            new WorksheetAspect(NatalPoint.Of(a.Planet), a.Angle, a.Type, a.Orb, a.IsApplying, a.OutOfSign)));

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
