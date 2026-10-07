using Lore.Models;
using NodaTime;
using System.Globalization;
using System.Text;

namespace Lore.Services;

// Two ways of carrying a birth chart forward in time, both standard practice:
//
//   • the solar return — the chart for the moment each year when the Sun comes back to
//     its exact place at birth, read as a picture of the year to the next birthday;
//   • secondary progressions — "a day for a year": the sky as it stood as many days
//     after birth as the person is years old, read against the birth chart.
//
// This is the astronomy and the plain statement of it. Nothing here is interpreted.
public sealed class TimingService
{
    // A progressed point is "in contact" with a natal one within this many degrees. A
    // progressed Sun moves about a degree a year, so this is roughly a year either side.
    public const double Orb = 1.0;

    private const double TropicalYearDays = 365.2422;

    // The points that are progressed: the faster bodies, whose progressed motion over a
    // lifetime is large enough to mean something, and the two angles.
    private static readonly Planet[] Progressed =
        [Planet.Sun, Planet.Moon, Planet.Mercury, Planet.Venus, Planet.Mars];

    private readonly ChartService _charts;

    public TimingService(ChartService charts) => _charts = charts;

    // ── Solar return ──────────────────────────────────────────────────────────

    // The solar return that falls in the given calendar year, cast for the birthplace
    // or, if one is given, for another place. Null for a chart with no birth time: its
    // Sun is known only to within half a degree, which leaves the moment of return
    // uncertain by half a day and the return chart's angles meaningless.
    public SolarReturn? SolarReturn(NatalChart natal, int year, ReturnPlace? place = null)
    {
        if (!natal.Timed || natal.GetPlanet(Planet.Sun) is not { } sun) return null;

        // The Sun is back within a day of the birthday; bracket it generously.
        var born = natal.CalculatedForUtc;
        var near = new DateTime(year, born.Month, Math.Min(born.Day, DateTime.DaysInMonth(year, born.Month)),
            born.Hour, born.Minute, 0, DateTimeKind.Utc);
        double lo = SwissEphemeris.DateTimeToJulianDay(near.AddDays(-3));
        double hi = SwissEphemeris.DateTimeToJulianDay(near.AddDays(3));

        double Off(double jd) =>
            _charts.CalculateBody(jd, Planet.Sun) is { } p ? Signed(p.Longitude - sun.Longitude) : double.NaN;
        if (double.IsNaN(Off(lo)) || double.IsNaN(Off(hi)) || (Off(lo) < 0) == (Off(hi) < 0)) return null;

        for (int i = 0; i < 50; i++)
        {
            double mid = (lo + hi) / 2;
            if ((Off(mid) < 0) == (Off(lo) < 0)) lo = mid; else hi = mid;
        }
        var utc = DateTime.SpecifyKind(SwissEphemeris.JulianDayToDateTime((lo + hi) / 2), DateTimeKind.Utc);
        var chart = place is null
            ? _charts.CalculateAt(natal.Celebrity, utc)
            : _charts.CalculateAt(natal.Celebrity, utc, place.Latitude, place.Longitude);
        return new SolarReturn { Year = year, Utc = utc, Chart = chart, Place = place };
    }

    // The return in force on a date: the latest one on or before it. The date is a day
    // on the reader's own calendar, the one the return's time is shown in.
    public SolarReturn? SolarReturnInForce(NatalChart natal, DateOnly asOf, DateTimeZone zone, ReturnPlace? place = null)
    {
        var dayEnds = zone.AtStartOfDay(new LocalDate(asOf.Year, asOf.Month, asOf.Day).PlusDays(1)).ToDateTimeUtc();
        var thisYear = SolarReturn(natal, asOf.Year, place);
        return thisYear is not null && thisYear.Utc < dayEnds ? thisYear : SolarReturn(natal, asOf.Year - 1, place);
    }

    // ── Secondary progressions ────────────────────────────────────────────────

    public Progression Progress(NatalChart natal, DateOnly asOf)
    {
        var born = natal.CalculatedForUtc;
        double ageYears = (asOf.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc) - born).TotalDays / TropicalYearDays;
        var progressedUtc = born.AddDays(ageYears); // a day for a year
        double jd = SwissEphemeris.DateTimeToJulianDay(progressedUtc);
        var sky = _charts.CalculateSky(jd);

        var points = new List<ProgressedPoint>();
        foreach (var body in Progressed)
        {
            // Without a birth time the progressed Moon could be several degrees out.
            if (body == Planet.Moon && !natal.Timed) continue;
            if (sky.FirstOrDefault(p => p.Planet == body) is { } now && natal.GetPlanet(body) is { } then)
                points.Add(new ProgressedPoint(NatalPoint.Of(body), now.Longitude, then.Longitude, now.IsRetrograde));
        }

        // The angles, by the usual "solar arc" method: the Midheaven moves on by exactly
        // as far as the progressed Sun has, and the Ascendant is the one that goes with
        // that Midheaven at the birthplace's latitude.
        if (natal.Timed && points.FirstOrDefault(p => p.Point == NatalPoint.Of(Planet.Sun)) is { } progressedSun)
        {
            double arc = Normalize(progressedSun.Longitude - progressedSun.NatalLongitude);
            double mc = Normalize(natal.Midheaven + arc);
            points.Add(new ProgressedPoint(NatalPoint.Midheaven, mc, natal.Midheaven, false));
            if (_charts.AscendantFor(mc, jd, natal.Celebrity.Latitude) is { } asc)
                points.Add(new ProgressedPoint(NatalPoint.Ascendant, asc, natal.Ascendant, false));
        }

        var targets = natal.Planets
            .Where(p => natal.Timed || p.Planet != Planet.Moon)
            .Select(p => (Point: NatalPoint.Of(p.Planet), p.Longitude)).ToList();
        if (natal.Timed)
        {
            targets.Add((NatalPoint.Ascendant, natal.Ascendant));
            targets.Add((NatalPoint.Midheaven, natal.Midheaven));
        }

        var aspects = new List<ProgressedAspect>();
        foreach (var p in points)
            foreach (var (target, lon) in targets)
            {
                if (p.Point.IsAngle && target.IsAngle) continue; // the angles move together
                double separation = Math.Abs(Signed(p.Longitude - lon));
                foreach (var type in AspectTypeExtensions.Majors)
                {
                    double orb = Math.Abs(separation - type.Angle());
                    if (orb <= Orb) { aspects.Add(new ProgressedAspect(p.Point, target, type, orb)); break; }
                }
            }
        aspects.Sort((a, b) => a.Orb.CompareTo(b.Orb));

        double sunLon = sky.FirstOrDefault(p => p.Planet == Planet.Sun)?.Longitude ?? 0;
        double moonLon = sky.FirstOrDefault(p => p.Planet == Planet.Moon)?.Longitude ?? 0;
        double elongation = Normalize(moonLon - sunLon);

        return new Progression
        {
            AsOf = asOf,
            AgeYears = ageYears,
            ProgressedUtc = progressedUtc,
            Points = points,
            Aspects = aspects,
            // Eight phases of 45°, each centred on its named point (New Moon at 0°).
            MoonPhase = (MoonPhase)((int)Math.Floor(Normalize(elongation + 22.5) / 45) % 8),
            Elongation = elongation,
        };
    }

    // ── Written out ───────────────────────────────────────────────────────────

    // `place`: where the solar return is cast for, if not the birthplace. The
    // progressions do not depend on it.
    public TimingReading Compose(NatalChart natal, DateOnly asOf, DateTimeZone zone, ReturnPlace? place = null)
    {
        var solar = SolarReturnInForce(natal, asOf, zone, place);
        var progression = Progress(natal, asOf);
        return new TimingReading
        {
            Name = natal.Celebrity.Name,
            AsOf = asOf,
            Return = solar,
            Progression = progression,
            Sections = [ReturnSection(natal, solar, zone), ProgressionSection(natal, progression)],
        };
    }

    private static DailySection ReturnSection(NatalChart natal, SolarReturn? solar, DateTimeZone zone)
    {
        if (solar is null)
            return new DailySection
            {
                Heading = "Solar return",
                Items =
                [
                    new DailyItem
                    {
                        Text = "A solar return needs a birth time. Without one the Sun's place at birth is known only to " +
                               "within half a degree, which leaves the moment it returns uncertain by half a day — and the " +
                               "return chart's Ascendant and houses, which are the point of it, turn completely in that time."
                    }
                ]
            };

        var chart = solar.Chart;
        var local = Instant.FromDateTimeUtc(solar.Utc).InZone(zone).LocalDateTime;
        var items = new List<DailyItem>
        {
            new()
            {
                Title = $"The Sun returns on {local.ToString("d MMMM yyyy 'at' HH:mm", null)}",
                Meta = $"your clock time  ·  {solar.Utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UT  ·  " +
                       (solar.Place is { } place
                           ? $"cast for {place.Name}, not the birthplace ({natal.Celebrity.BirthPlace})"
                           : $"cast for the birthplace, {natal.Celebrity.BirthPlace}"),
                Text = "The solar return is the chart for the moment the Sun comes back to exactly where it stood at birth. " +
                       "It is read as a picture of the year from this birthday to the next, and its Ascendant and houses " +
                       "matter most. The wheel beside this is the return chart." +
                       (solar.Place is null ? "" :
                           " The moment is the same wherever the return is cast for; the Ascendant, Midheaven and houses " +
                           "below are those of the place chosen.")
            },
            new()
            {
                Title = "The return chart's angles",
                Meta = chart.HouseSystemLabel,
                Text = $"Ascendant {Position(chart.Ascendant)}, which falls in the {Ordinal(natal.GetHouseForLongitude(chart.Ascendant))} house of the birth chart.\n" +
                       $"Midheaven {Position(chart.Midheaven)}, in the {Ordinal(natal.GetHouseForLongitude(chart.Midheaven))} house of the birth chart."
            },
            new()
            {
                Title = "Where the planets fall in the return chart",
                Text = string.Join("\n", chart.Planets.Select(p =>
                    $"{p.PlanetSymbol} {p.PlanetName} {Position(p.Longitude)}{(p.IsRetrograde ? " ℞" : "")} — " +
                    $"{Ordinal(chart.GetHouseForLongitude(p.Longitude))} house"))
            },
        };
        return new DailySection { Heading = $"Solar return for {solar.Utc.Year}–{solar.Utc.Year + 1}", Items = items };
    }

    private static DailySection ProgressionSection(NatalChart natal, Progression p)
    {
        string Name(NatalPoint point) => point.IsAngle ? point.Name : $"{point.Symbol} {point.Name}";

        var items = new List<DailyItem>
        {
            new()
            {
                Title = "A day for a year",
                Meta = $"{p.AgeYears.ToString("0.0", CultureInfo.InvariantCulture)} years after birth  ·  the sky of " +
                       $"{p.ProgressedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UT",
                Text = "Secondary progressions move the birth chart forward one day for each year of life: at thirty, the " +
                       "progressed chart is the sky thirty days after birth. The progressed Sun moves about a degree a " +
                       "year and the progressed Moon about a degree a month, going round the whole chart in some " +
                       "twenty-seven years."
            },
            new()
            {
                Title = "Progressed positions",
                Meta = natal.Timed
                    ? "the Midheaven by solar arc, and the Ascendant that goes with it at the birthplace"
                    : "no birth time: the Moon and the angles are left out",
                Text = string.Join("\n", p.Points.Select(x =>
                    $"{Name(x.Point)} {Position(x.Longitude)}{(x.Retrograde ? " ℞" : "")}   (at birth {Position(x.NatalLongitude)})" +
                    (natal.Timed && !x.Point.IsAngle ? $" — {Ordinal(natal.GetHouseForLongitude(x.Longitude))} house of the birth chart" : "")))
            },
        };

        if (natal.Timed)
            items.Add(new DailyItem
            {
                Title = $"Progressed lunar phase: {p.MoonPhase.Name()}",
                Meta = $"the progressed Moon is {p.Elongation.ToString("0", CultureInfo.InvariantCulture)}° ahead of the progressed Sun",
                Text = "The progressed Moon and Sun make a full cycle, New Moon to New Moon, about every thirty years. " +
                       "Many astrologers read it as the long rhythm of a life: beginnings at the New Moon, culmination " +
                       "at the Full, release in the last quarter."
            });

        items.Add(new DailyItem
        {
            Title = $"Contacts to the birth chart (within {Orb:0}°)",
            Text = p.Aspects.Count == 0
                ? "No progressed point is within a degree of an aspect to the birth chart at this date."
                : string.Join("\n", p.Aspects.Select(a =>
                    $"Progressed {Name(a.Progressed)} {a.Type.Verb()} natal {Name(a.Natal)} — {FormatOrb(a.Orb)} from exact"))
        });

        return new DailySection { Heading = $"Secondary progressions for {p.AsOf:d MMMM yyyy}", Items = items };
    }

    // The reading as plain text, for saving or pasting into notes.
    public static byte[] ToText(TimingReading t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Name} — Solar return and progressions");
        sb.AppendLine($"As of {t.AsOf:d MMMM yyyy}");
        foreach (var section in t.Sections)
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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Position(double longitude) =>
        $"{ZodiacSignExtensions.FormatDegreeInSign(longitude)} {ZodiacSignExtensions.FromLongitude(longitude).Name()}";

    private static string Ordinal(int n) => ChartInterpreter.Ordinal(n);

    private static string FormatOrb(double orb)
    {
        int minutes = (int)Math.Round(orb * 60);
        return $"{minutes / 60}°{minutes % 60:D2}'";
    }

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    // Difference folded into −180…+180, so 359° and 1° are 2° apart rather than 358°.
    private static double Signed(double degrees) => Normalize(degrees + 180) - 180;
}
