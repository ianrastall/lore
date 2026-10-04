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
        (Planet.Lilith,    SwissEphemeris.SE_MEAN_APOG),
    ];

    // The house system and node type every calculation uses. Replaced as a whole when
    // the user changes a setting; each calculation reads it once.
    public ChartSettings Settings { get; set; } = ChartSettings.Default;

    private static int SweBody(Planet planet, NodeType node) =>
        planet == Planet.NorthNode && node == NodeType.True
            ? SwissEphemeris.SE_TRUE_NODE
            : PlanetMap[(int)planet].sweBody;

    // Swiss Ephemeris (sweph.dll) keeps global internal state and is NOT thread-safe:
    // swe_calc_ut / swe_houses share buffers and the ephemeris-file cache. This app hits
    // the native layer from more than one thread — chart selection calculates on a
    // thread-pool thread (Task.Run), while startup/add/delete re-scoring runs its own
    // pass — so two calculations can otherwise overlap and corrupt each other's results
    // or crash. Every native call is funnelled through this single lock; the calls are
    // sub-millisecond, so serialising them costs nothing perceptible.
    private static readonly object SweLock = new();

    public ChartService(string ephemerisPath)
    {
        SwissEphemeris.SetEphePath(ephemerisPath);
    }

    public NatalChart Calculate(Celebrity celebrity) =>
        CalculateAt(celebrity, BirthTimeResolver.ToUtc(celebrity));

    // The chart for this person's birthplace at a given instant rather than their
    // recorded birth time — what "if they were born ten minutes later" is tested with.
    public NatalChart CalculateAt(Celebrity celebrity, DateTime utc)
    {
        var settings = Settings;
        double jd = SwissEphemeris.DateTimeToJulianDay(utc);

        List<PlanetPosition> planets;
        List<HouseCusp> houses;
        double asc, mc;
        bool substituted;
        string problem;
        lock (SweLock)
        {
            planets = CalculatePlanets(jd, settings.Node);
            problem = _lastProblem ?? "";
            (houses, asc, mc, substituted) = CalculateHouses(jd, celebrity.Latitude, celebrity.Longitude, settings.Houses);
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
            Ascendant = asc,
            Midheaven = mc,
            Settings = settings,
            CalculatedForUtc = utc,
            EphemerisNote = problem.Trim(),
            HouseSystemLabel = substituted
                ? $"Porphyry houses ({settings.Houses.Name()} cannot be calculated at this latitude)"
                : $"{settings.Houses.Name()} houses",
        };
    }

    // Every body's position at an arbitrary instant (the "transit sky"), with the same
    // bodies, flags and lock as a natal calculation.
    public IReadOnlyList<PlanetPosition> CalculateSky(double jd)
    {
        var node = Settings.Node;
        lock (SweLock)
        {
            return CalculatePlanets(jd, node);
        }
    }

    // One body at one instant — what the transit solver refines exact times with.
    // Null if the ephemeris cannot supply it (same condition under which a natal
    // calculation skips the body).
    public PlanetPosition? CalculateBody(double jd, Planet planet)
    {
        int sweBody = SweBody(planet, Settings.Node);
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
        };
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

    private static List<PlanetPosition> CalculatePlanets(double jd, NodeType node)
    {
        _lastProblem = null;
        bool fallback = false;
        var missing = new List<string>();
        var result = new List<PlanetPosition>(PlanetMap.Length);
        var xx = new double[6];
        var eq = new double[6];
        int flags = SwissEphemeris.SEFLG_SWIEPH | SwissEphemeris.SEFLG_SPEED;

        foreach (var (planet, _) in PlanetMap)
        {
            int ret = SwissEphemeris.CalcUt(jd, SweBody(planet, node), flags, xx, nint.Zero);
            if (ret < 0)
            {
                missing.Add(planet.Name()); // e.g. Chiron when its data file is absent
                continue;
            }
            // The flags that come back say which ephemeris was really used. (The mean
            // node and mean Lilith are computed without any file, so they don't count.)
            if ((ret & SwissEphemeris.SEFLG_SWIEPH) == 0 && planet is not (Planet.NorthNode or Planet.Lilith))
                fallback = true;

            // A second pass in equatorial coordinates, for the declination only.
            double declination = SwissEphemeris.CalcUt(jd, SweBody(planet, node),
                flags | SwissEphemeris.SEFLG_EQUATORIAL, eq, nint.Zero) < 0 ? 0 : eq[1];

            result.Add(new PlanetPosition
            {
                Planet = planet,
                Longitude = xx[0],
                Latitude = xx[1],
                Declination = declination,
                SpeedLongitude = xx[3],
            });
        }

        if (fallback || missing.Count > 0)
            _lastProblem =
                (fallback ? "The Swiss Ephemeris data files could not be read, so a less precise built-in model was used. " : "") +
                (missing.Count > 0 ? $"Not available: {string.Join(", ", missing)}." : "");
        return result;
    }

    // `substituted` is set when the Swiss Ephemeris could not calculate the requested
    // system at this latitude and returned Porphyry cusps in its place.
    private static (List<HouseCusp> houses, double asc, double mc, bool substituted) CalculateHouses(
        double jd, double lat, double lon, HouseSystem system)
    {
        var cusps = new double[13];
        var ascmc = new double[10];

        bool substituted = SwissEphemeris.Houses(jd, lat, lon, system.SweCode(), cusps, ascmc) < 0;

        var houses = Enumerable.Range(1, 12)
            .Select(i => new HouseCusp { House = i, Longitude = cusps[i] })
            .ToList();

        return (houses, ascmc[SwissEphemeris.SE_ASC], ascmc[SwissEphemeris.SE_MC], substituted);
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
        List<PlanetPosition> planets, double asc, double mc, OrbSettings orbs)
    {
        var aspects = new List<AngleAspect>();
        foreach (var p in planets)
            foreach (var (angle, lon) in new[] { (NatalPoint.Ascendant, asc), (NatalPoint.Midheaven, mc) })
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
