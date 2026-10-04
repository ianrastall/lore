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

        var positions = chart.Planets.Select(p => BodyRow(chart, p)).ToList();
        if (timed)
        {
            positions.Add(PointRow(chart, "↑", "Ascendant", chart.Ascendant, house: false));
            positions.Add(PointRow(chart, "MC", "Midheaven", chart.Midheaven, house: false));
            if (PartOfFortune(chart) is { } fortune)
                positions.Add(PointRow(chart, "⊗", "Part of Fortune", fortune, house: true));
        }

        var points = chart.Planets.Select(p => NatalPoint.Of(p.Planet)).ToList();
        if (timed)
        {
            points.Add(NatalPoint.Ascendant);
            points.Add(NatalPoint.Midheaven);
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
                ? $"Old Style (Julian) date; in the Gregorian calendar it is {BirthTimeResolver.GregorianDate(c):yyyy-MM-dd}"
                : "Gregorian"),
            new("Place", $"{c.BirthPlace}  ·  {Coordinate(c.Latitude, 'N', 'S')}, {Coordinate(c.Longitude, 'E', 'W')}"),
            new("Reliability", string.IsNullOrWhiteSpace(c.RoddenRating)
                ? "Not rated"
                : $"Rodden rating {RoddenRating.Describe(c.RoddenRating)}"),
            new("Source", string.IsNullOrWhiteSpace(c.Source) ? "Not recorded" : c.Source),
            new("Time conversion", offset),
            new("Universal Time", $"{utc:yyyy-MM-dd HH:mm:ss} UT"),
            new("Zodiac", "Tropical, geocentric"),
            new("Houses", chart.Timed ? chart.HouseSystemLabel : "None — they need a birth time"),
            new("North Node", chart.Settings.Node.Name()),
            new("Lilith", "Mean lunar apogee (Black Moon)"),
        };
        if (chart.Timed)
        {
            facts.Add(new("Sect", chart.IsDayChart
                ? "Day chart — the Sun is above the horizon"
                : "Night chart — the Sun is below the horizon"));
            facts.Add(new("Part of Fortune", chart.IsDayChart
                ? "Ascendant + Moon − Sun (day formula)"
                : "Ascendant + Sun − Moon (night formula)"));
        }
        facts.Add(new("Aspect orbs", "8° conjunction, square, trine and opposition; 6° sextile"));
        facts.Add(new("Engine", "Swiss Ephemeris 2.10.03"));
        return facts;
    }

    // ── Positions ─────────────────────────────────────────────────────────────

    private static WorksheetRow BodyRow(NatalChart chart, PlanetPosition p) => new(
        p.PlanetSymbol, p.PlanetName, Dms(p.Longitude),
        Signed(p.Latitude), Signed(p.Declination),
        p.SpeedLongitude.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture) + "°",
        chart.Timed ? chart.GetHouseForLongitude(p.Longitude).ToString() : "",
        p.IsRetrograde ? "℞" : "");

    private static WorksheetRow PointRow(NatalChart chart, string symbol, string name, double longitude, bool house) => new(
        symbol, name, Dms(longitude), "", "", "",
        house ? chart.GetHouseForLongitude(longitude).ToString() : "", "");

    // The Lot of Fortune, reversed by sect as the traditional sources give it: by day
    // Ascendant + Moon − Sun, by night Ascendant + Sun − Moon.
    public static double? PartOfFortune(NatalChart chart)
    {
        if (!chart.Timed ||
            chart.GetPlanet(Planet.Sun) is not { } sun || chart.GetPlanet(Planet.Moon) is not { } moon)
            return null;

        double arc = chart.IsDayChart ? moon.Longitude - sun.Longitude : sun.Longitude - moon.Longitude;
        return Normalize(chart.Ascendant + arc);
    }

    // ── Aspects ───────────────────────────────────────────────────────────────

    // The chart's own planet-to-planet aspects, plus each planet's aspects to the
    // Ascendant and Midheaven on the same orbs. (The two angles are not aspected to
    // each other.) Closest first.
    private static List<WorksheetAspect> Aspects(NatalChart chart)
    {
        var aspects = chart.Aspects
            .Select(a => new WorksheetAspect(NatalPoint.Of(a.PlanetA), NatalPoint.Of(a.PlanetB), a.Type, a.Orb, a.IsApplying))
            .ToList();

        if (chart.Timed)
        {
            foreach (var p in chart.Planets)
                foreach (var (angle, lon) in new[] { (NatalPoint.Ascendant, chart.Ascendant), (NatalPoint.Midheaven, chart.Midheaven) })
                    foreach (var type in Enum.GetValues<AspectType>())
                    {
                        double orb = Math.Abs(AngleBetween(p.Longitude, lon) - type.Angle());
                        if (orb > type.Orb()) continue;

                        // The angle is held still; the planet's own motion decides whether
                        // the aspect is closing or opening.
                        double next = Math.Abs(AngleBetween(p.Longitude + p.SpeedLongitude / 24.0, lon) - type.Angle());
                        aspects.Add(new WorksheetAspect(NatalPoint.Of(p.Planet), angle, type, orb, next < orb));
                        break; // one aspect per pair
                    }
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
        sb.AppendLine($"{"",-17}{"Position",-25}{"Latitude",-11}{"Declin.",-11}{"Speed/day",-11}{"House",-6}");
        foreach (var r in w.Positions)
            sb.AppendLine($"{r.Name,-17}{r.Position,-25}{r.Latitude,-11}{r.Declination,-11}{r.Speed,-11}{r.House,-6}{(r.Motion.Length > 0 ? "retrograde" : "")}".TrimEnd());

        if (w.HasCusps)
        {
            sb.AppendLine();
            sb.AppendLine("HOUSE CUSPS");
            foreach (var c in w.Cusps)
                sb.AppendLine($"{c.House,-4}{c.Position}");
        }

        sb.AppendLine();
        sb.AppendLine("ASPECTS (closest first; a = applying, s = separating)");
        foreach (var a in w.Aspects)
            sb.AppendLine($"{a.A.Name,-12}{a.Type,-13}{a.B.Name,-12}{a.OrbText}");

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
