using Lore.Models;

namespace Lore.Services;

public sealed class ChartService
{
    private static readonly (Planet planet, int sweBody)[] PlanetMap =
    [
        (Planet.Sun,       SwissEphemeris.SE_SUN),
        (Planet.Moon,      SwissEphemeris.SE_MOON),
        (Planet.Mercury,   SwissEphemeris.SE_MERCURY),
        (Planet.Venus,     SwissEphemeris.SE_VENUS),
        (Planet.Mars,      SwissEphemeris.SE_MARS),
        (Planet.Jupiter,   SwissEphemeris.SE_JUPITER),
        (Planet.Saturn,    SwissEphemeris.SE_SATURN),
        (Planet.Uranus,    SwissEphemeris.SE_URANUS),
        (Planet.Neptune,   SwissEphemeris.SE_NEPTUNE),
        (Planet.Pluto,     SwissEphemeris.SE_PLUTO),
        (Planet.NorthNode, SwissEphemeris.SE_MEAN_NODE), // or the true node: see SweBody
        (Planet.Chiron,    SwissEphemeris.SE_CHIRON),
        (Planet.Lilith,    SwissEphemeris.SE_MEAN_APOG),    // or the true apogee: see SweBody
    ];

    // The house system and node type every calculation uses. Replaced as a whole when
    // the user changes a setting; each calculation reads it once. (See With, for work
    // that is more than one calculation.)
    public ChartSettings Settings { get; set; } = ChartSettings.Default;

    private static int SweBody(Planet planet, NodeType node, LilithType lilith) =>
        planet == Planet.NorthNode && node == NodeType.True ? SwissEphemeris.SE_TRUE_NODE
        : planet == Planet.Lilith && lilith == LilithType.True ? SwissEphemeris.SE_OSCU_APOG
        : PlanetMap[(int)planet].sweBody;

    // Swiss Ephemeris (sweph.dll) keeps global internal state and is NOT thread-safe:
    // swe_calc_ut / swe_houses share buffers and the ephemeris-file cache. This app hits
    // the native layer from more than one thread — chart selection calculates on a
    // thread-pool thread (Task.Run), while startup/add/delete re-scoring runs its own
    // pass — so two calculations can otherwise overlap and corrupt each other's results
    // or crash. Every native call is funnelled through this single lock; the calls are
    // sub-millisecond, so serialising them costs nothing perceptible.
    private static readonly object SweLock = new();

    // The ephemeris path belongs to the whole process, not to one service: it is set
    // once, here, under the same lock as the calculations that read it.
    public ChartService(string ephemerisPath)
    {
        lock (SweLock)
        {
            SwissEphemeris.SetEphePath(ephemerisPath);
        }
    }

    private ChartService(ChartSettings settings) => Settings = settings;

    // This service with its settings fixed at the given ones, whatever is chosen in the
    // menu afterwards. Anything that makes many calculations for one result (a forecast,
    // a search of the library, the birth-time check) works through one of these, so a
    // setting changed half-way cannot give it a mixture — the first half of a forecast
    // with the mean Lilith and the rest with the true one, tens of degrees away.
    public ChartService With(ChartSettings settings) => new(settings);

    public NatalChart Calculate(Celebrity celebrity) =>
        CalculateAt(celebrity, BirthTimeResolver.ToUtc(celebrity));

    // The chart for this person's birthplace at a given instant rather than their
    // recorded birth time — what "if they were born ten minutes later" is tested with.
    public NatalChart CalculateAt(Celebrity celebrity, DateTime utc) =>
        CalculateAt(celebrity, utc, celebrity.Latitude, celebrity.Longitude);

    // The same, with the houses and angles taken for somewhere other than the birthplace
    // (a solar return cast for where the person was living).
    public NatalChart CalculateAt(Celebrity celebrity, DateTime utc, double latitude, double longitude)
    {
        var settings = Settings;
        double jd = SwissEphemeris.DateTimeToJulianDay(utc);

        List<PlanetPosition> planets;
        var alternates = new List<AlternatePoint>();
        List<HouseCusp> houses;
        double asc, mc, vertex, armc;
        double? obliquity;
        bool substituted;
        string problem;
        lock (SweLock)
        {
            planets = CalculatePlanets(jd, settings.Node, settings.Lilith);
            problem = _lastProblem ?? "";

            // The node and Lilith of the other kind, for the Worksheet.
            var otherNode = settings.Node == NodeType.True ? NodeType.Mean : NodeType.True;
            var otherLilith = settings.Lilith == LilithType.True ? LilithType.Mean : LilithType.True;
            if (CalculateOne(jd, Planet.NorthNode, otherNode, otherLilith) is { } n)
                alternates.Add(new($"North Node ({(otherNode == NodeType.True ? "true" : "mean")})", n));
            if (CalculateOne(jd, Planet.Lilith, otherNode, otherLilith) is { } l)
                alternates.Add(new($"Lilith ({(otherLilith == LilithType.True ? "true" : "mean")})", l));
            (houses, asc, mc, vertex, armc, substituted) = CalculateHouses(jd, latitude, longitude, settings.Houses);
            var ecl = new double[6];
            obliquity = SwissEphemeris.CalcUt(jd, SwissEphemeris.SE_ECL_NUT, 0, ecl, nint.Zero) < 0 ? null : ecl[0];
        }

        // Aspect detection is pure managed arithmetic on the results above — no native
        // state — so it stays outside the lock.
        var aspects = CalculateAspects(planets, settings.Orbs);
        var angleAspects = celebrity.BirthTimeKnown
            ? CalculateAngleAspects(planets, asc, mc, settings.Orbs)
            : [];

        return new NatalChart
        {
            Celebrity = celebrity,
            Planets = planets,
            Houses = houses,
            Aspects = aspects,
            AngleAspects = angleAspects,
            Alternates = alternates,
            Ascendant = asc,
            Midheaven = mc,
            Vertex = vertex,
            Armc = armc,
            Obliquity = obliquity,
            SunAboveHorizon = SunIsUp(planets, armc, latitude),
            GeoLatitude = latitude,
            GeoLongitude = longitude,
            Settings = settings,
            CalculatedForUtc = utc,
            EphemerisNote = problem.Trim(),
            HouseSystemLabel = substituted
                ? $"Porphyry houses ({settings.Houses.Name()} cannot be calculated at this latitude)"
                : $"{settings.Houses.Name()} houses",
        };
    }

    // Whether the Sun's centre is above the horizon of the place the chart is cast for,
    // from its altitude there. Null if the Sun's equatorial position could not be had.
    private static bool? SunIsUp(List<PlanetPosition> planets, double armc, double latitude)
    {
        if (planets.FirstOrDefault(p => p.Planet == Planet.Sun) is not { HasEquatorial: true } sun) return null;
        const double Rad = Math.PI / 180;
        double hourAngle = (armc - sun.RightAscension) * Rad;
        double sinAltitude = Math.Sin(latitude * Rad) * Math.Sin(sun.Declination * Rad) +
                             Math.Cos(latitude * Rad) * Math.Cos(sun.Declination * Rad) * Math.Cos(hourAngle);
        return sinAltitude >= 0;
    }

    // The Davison chart of two people: an ordinary chart, cast for the moment halfway
    // between their births at the place halfway between their birthplaces (the mean of
    // the latitudes, and the longitude midway round the shorter side of the globe). With
    // a birth time missing the midpoint could be hours out, so the chart is then marked
    // untimed and its angles, houses and Moon are not to be read.
    public NatalChart Davison(NatalChart first, NatalChart second)
    {
        var utc = first.CalculatedForUtc + (second.CalculatedForUtc - first.CalculatedForUtc) / 2;
        Celebrity a = first.Celebrity, b = second.Celebrity;
        double apart = ((b.Longitude - a.Longitude) % 360 + 540) % 360 - 180;   // −180…+180
        double longitude = ((a.Longitude + apart / 2) % 360 + 540) % 360 - 180;
        double latitude = (a.Latitude + b.Latitude) / 2;

        // Cast with the first chart's settings, like the comparison it sits beside.
        return With(first.Settings).CalculateAt(new Celebrity
        {
            Id = $"davison:{a.Id}:{b.Id}",
            Name = $"{a.Name} & {b.Name}",
            Category = "Davison",
            BirthDate = utc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            BirthTime = utc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            BirthTimeKnown = a.BirthTimeKnown && b.BirthTimeKnown,
            BirthPlace = $"midway between {a.BirthPlace} and {b.BirthPlace}",
            Latitude = latitude,
            Longitude = longitude,
            UtcOffsetHours = 0,
            UtcOffsetFixed = true,
        }, utc);
    }

    // Every body's position at an arbitrary instant (the "transit sky"), with the same
    // bodies, flags and lock as a natal calculation.
    public IReadOnlyList<PlanetPosition> CalculateSky(double jd)
    {
        var settings = Settings;
        lock (SweLock)
        {
            return CalculatePlanets(jd, settings.Node, settings.Lilith);
        }
    }

    // One body at one instant — what the transit solver refines exact times with.
    // Null if the ephemeris cannot supply it (same condition under which a natal
    // calculation skips the body).
    public PlanetPosition? CalculateBody(double jd, Planet planet)
    {
        var settings = Settings;
        int sweBody = SweBody(planet, settings.Node, settings.Lilith);
        var xx = new double[6];
        int flags = SwissEphemeris.SEFLG_SWIEPH | SwissEphemeris.SEFLG_SPEED;

        lock (SweLock)
        {
            if (SwissEphemeris.CalcUt(jd, sweBody, flags, xx, nint.Zero) < 0)
                return null;
        }

        return new PlanetPosition
        {
            Planet = planet,
            Longitude = xx[0],
            Latitude = xx[1],
            SpeedLongitude = xx[3],
            TruePoint = IsTruePoint(planet, settings.Node, settings.Lilith),
        };
    }

    private static bool IsTruePoint(Planet planet, NodeType node, LilithType lilith) =>
        (planet == Planet.NorthNode && node == NodeType.True) || (planet == Planet.Lilith && lilith == LilithType.True);

    // The next eclipse after an instant, of the Sun (seen from anywhere on Earth) or of
    // the Moon: the moment it is greatest, and its kind in a word. Null if the
    // ephemeris cannot say.
    public (double Jd, string Kind)? NextEclipse(double afterJd, bool lunar)
    {
        var tret = new double[10];
        int type;
        lock (SweLock)
        {
            type = lunar
                ? SwissEphemeris.LunEclipseWhen(afterJd, SwissEphemeris.SEFLG_SWIEPH, 0, tret, 0, nint.Zero)
                : SwissEphemeris.SolEclipseWhenGlob(afterJd, SwissEphemeris.SEFLG_SWIEPH, 0, tret, 0, nint.Zero);
        }
        if (type < 0) return null;

        string kind =
            (type & SwissEphemeris.SE_ECL_ANNULAR_TOTAL) != 0 ? "hybrid"
            : (type & SwissEphemeris.SE_ECL_TOTAL) != 0 ? "total"
            : (type & SwissEphemeris.SE_ECL_ANNULAR) != 0 ? "annular"
            : (type & SwissEphemeris.SE_ECL_PENUMBRAL) != 0 ? "penumbral"
            : "partial";
        return (tret[0], kind);
    }

    // The next sunrise (or sunset) after an instant at a place: when the Sun's upper edge
    // touches a sea-level horizon, with standard refraction, as almanacs give it. Null
    // if the Sun does not rise (or set) there within the next days, as inside the polar
    // circles in midsummer and midwinter.
    public double? NextSunriseOrSet(double afterJd, double latitude, double longitude, bool sunset)
    {
        var when = new double[1];
        int result;
        lock (SweLock)
        {
            result = SwissEphemeris.RiseTrans(afterJd, SwissEphemeris.SE_SUN, nint.Zero, SwissEphemeris.SEFLG_SWIEPH,
                sunset ? SwissEphemeris.SE_CALC_SET : SwissEphemeris.SE_CALC_RISE,
                [longitude, latitude, 0], 1013.25, 15, when, nint.Zero);
        }
        return result < 0 ? null : when[0];
    }

    // Set when a calculation could not use the Swiss Ephemeris data files and fell back
    // on the library's built-in (less precise) model, or could not supply a body at all.
    // Empty when all is well. Read by NatalChart.EphemerisNote.
    [ThreadStatic] private static string? _lastProblem;

    // The Ascendant that belongs with a given Midheaven at a given latitude — the same
    // geometry a chart is cast with, entered from the Midheaven instead of from a time.
    // `jd` only fixes the tilt of the Earth's axis, which changes by a hair in a lifetime.
    // Null if the ephemeris cannot supply that.
    public double? AscendantFor(double midheaven, double jd, double latitude)
    {
        var xx = new double[6];
        var cusps = new double[13];
        var ascmc = new double[10];
        lock (SweLock)
        {
            if (SwissEphemeris.CalcUt(jd, SwissEphemeris.SE_ECL_NUT, 0, xx, nint.Zero) < 0) return null;
            double eps = xx[0] * Math.PI / 180;   // true obliquity of the ecliptic
            double mc = midheaven * Math.PI / 180;
            // The Midheaven is the ecliptic degree on the meridian; its right ascension
            // is the sidereal time.
            double armc = Math.Atan2(Math.Sin(mc) * Math.Cos(eps), Math.Cos(mc)) * 180 / Math.PI;
            SwissEphemeris.HousesArmc(((armc % 360) + 360) % 360, latitude, xx[0], 'P', cusps, ascmc);
        }
        return ascmc[SwissEphemeris.SE_ASC];
    }

    private static List<PlanetPosition> CalculatePlanets(double jd, NodeType node, LilithType lilith)
    {
        _lastProblem = null;
        bool fallback = false;
        var missing = new List<string>();
        var result = new List<PlanetPosition>(PlanetMap.Length);

        foreach (var (planet, _) in PlanetMap)
        {
            if (CalculateOne(jd, planet, node, lilith, out bool fromFiles) is not { } position)
            {
                missing.Add(planet.Name()); // e.g. Chiron when its data file is absent
                continue;
            }
            // (The mean node and mean Lilith are computed without any file, so they
            // don't count.)
            if (!fromFiles && planet is not (Planet.NorthNode or Planet.Lilith))
                fallback = true;
            result.Add(position);
        }

        if (fallback || missing.Count > 0)
            _lastProblem =
                (fallback ? "The Swiss Ephemeris data files could not be read, so a less precise built-in model was used. " : "") +
                (missing.Count > 0 ? $"Not available: {string.Join(", ", missing)}." : "");
        return result;
    }

    private static PlanetPosition? CalculateOne(double jd, Planet planet, NodeType node, LilithType lilith) =>
        CalculateOne(jd, planet, node, lilith, out _);

    // One body in full. Null if the ephemeris cannot supply it; `fromFiles` says whether
    // the Swiss Ephemeris data files were really used (the flags that come back tell).
    // Call inside SweLock.
    private static PlanetPosition? CalculateOne(double jd, Planet planet, NodeType node, LilithType lilith, out bool fromFiles)
    {
        var xx = new double[6];
        var eq = new double[6];
        int flags = SwissEphemeris.SEFLG_SWIEPH | SwissEphemeris.SEFLG_SPEED;
        int body = SweBody(planet, node, lilith);

        int ret = SwissEphemeris.CalcUt(jd, body, flags, xx, nint.Zero);
        fromFiles = ret >= 0 && (ret & SwissEphemeris.SEFLG_SWIEPH) != 0;
        if (ret < 0) return null;

        // A second pass in equatorial coordinates, for the right ascension and
        // declination. If it fails they are marked unknown rather than left at zero.
        bool equatorial = SwissEphemeris.CalcUt(jd, body, flags | SwissEphemeris.SEFLG_EQUATORIAL, eq, nint.Zero) >= 0;

        return new PlanetPosition
        {
            Planet = planet,
            Longitude = xx[0],
            Latitude = xx[1],
            Distance = xx[2],
            SpeedLongitude = xx[3],
            SpeedLatitude = xx[4],
            TruePoint = IsTruePoint(planet, node, lilith),
            HasEquatorial = equatorial,
            RightAscension = equatorial ? eq[0] : 0,
            Declination = equatorial ? eq[1] : 0,
            SpeedDeclination = equatorial ? eq[4] : 0,
        };
    }

    // `substituted` is set when the Swiss Ephemeris could not calculate the requested
    // system at this latitude and returned Porphyry cusps in its place.
    private static (List<HouseCusp> houses, double asc, double mc, double vertex, double armc, bool substituted) CalculateHouses(
        double jd, double lat, double lon, HouseSystem system)
    {
        var cusps = new double[13];
        var ascmc = new double[10];

        bool substituted = SwissEphemeris.Houses(jd, lat, lon, system.SweCode(), cusps, ascmc) < 0;

        var houses = Enumerable.Range(1, 12)
            .Select(i => new HouseCusp { House = i, Longitude = cusps[i] })
            .ToList();

        return (houses, ascmc[SwissEphemeris.SE_ASC], ascmc[SwissEphemeris.SE_MC],
            ascmc[SwissEphemeris.SE_VERTEX], ascmc[SwissEphemeris.SE_ARMC], substituted);
    }

    private static List<Aspect> CalculateAspects(List<PlanetPosition> planets, OrbSettings orbs)
    {
        var aspects = new List<Aspect>();
        var types = orbs.Types().ToList();

        for (int i = 0; i < planets.Count; i++)
        for (int j = i + 1; j < planets.Count; j++)
        {
            double angle = AngleBetween(planets[i].Longitude, planets[j].Longitude);

            // Nearest exact angle first, so that where a wide major orb and a minor
            // aspect both cover the separation, the closer one is the one recorded.
            foreach (var type in types.OrderBy(t => Math.Abs(angle - t.Angle())))
            {
                double exactAngle = type.Angle();
                double orb = Math.Abs(angle - exactAngle);
                double allowed = orbs.For(type, planets[i].Planet, planets[j].Planet);
                if (orb <= allowed)
                {
                    // applying = faster planet is moving toward the exact angle
                    bool applying = IsApplying(planets[i], planets[j], type);
                    aspects.Add(new Aspect
                    {
                        PlanetA = planets[i].Planet,
                        PlanetB = planets[j].Planet,
                        Type = type,
                        Orb = orb,
                        Allowed = allowed,
                        IsApplying = applying,
                        OutOfSign = type.IsOutOfSign(planets[i].Longitude, planets[j].Longitude),
                    });
                    break; // one aspect per pair
                }
            }
        }

        return aspects;
    }

    // Each body against the Ascendant and the Midheaven, on the same orbs. (The two
    // angles are not aspected to each other.)
    private static List<AngleAspect> CalculateAngleAspects(
        List<PlanetPosition> planets, double asc, double mc, OrbSettings orbs) =>
        AspectsToPoints(planets, [(NatalPoint.Ascendant, asc), (NatalPoint.Midheaven, mc)], orbs);

    // Each body against each of some fixed points of the chart, on the chart's orbs.
    public static List<AngleAspect> AspectsToPoints(
        IReadOnlyList<PlanetPosition> planets, IReadOnlyList<(NatalPoint Point, double Longitude)> points, OrbSettings orbs)
    {
        var aspects = new List<AngleAspect>();
        foreach (var p in planets)
            foreach (var (angle, lon) in points)
                foreach (var type in orbs.Types().OrderBy(t => Math.Abs(AngleBetween(p.Longitude, lon) - t.Angle())))
                {
                    double orb = Math.Abs(AngleBetween(p.Longitude, lon) - type.Angle());
                    double allowed = orbs.For(type, p.Planet);
                    if (orb > allowed) continue;

                    // The angle is held still; the planet's own motion decides whether
                    // the aspect is closing or opening.
                    double next = Math.Abs(AngleBetween(p.Longitude + p.SpeedLongitude * StepDays, lon) - type.Angle());
                    aspects.Add(new AngleAspect
                    {
                        Planet = p.Planet, Angle = angle, Type = type,
                        Orb = orb, Allowed = allowed, IsApplying = next < orb,
                        OutOfSign = type.IsOutOfSign(p.Longitude, lon),
                    });
                    break; // one aspect per pair
                }
        return aspects;
    }

    private static double AngleBetween(double lonA, double lonB)
    {
        double diff = Math.Abs(lonA - lonB) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    private const double StepDays = 1.0 / 1440; // one minute

    private static bool IsApplying(PlanetPosition a, PlanetPosition b, AspectType type)
    {
        // Applying = the separation to the exact angle is shrinking. Advance both planets
        // by their own motion over one minute (so relative speed is captured) and compare.
        // A step as long as an hour would carry a fast Moon through an aspect that is a
        // few minutes from exact and out the other side, and call it separating.
        double currentAngle = AngleBetween(a.Longitude, b.Longitude);
        double nextA = a.Longitude + a.SpeedLongitude * StepDays;
        double nextB = b.Longitude + b.SpeedLongitude * StepDays;
        double nextAngle = AngleBetween(nextA, nextB);
        double exact = type.Angle();
        return Math.Abs(nextAngle - exact) < Math.Abs(currentAngle - exact);
    }
}
