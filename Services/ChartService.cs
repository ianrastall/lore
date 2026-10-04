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

    public NatalChart Calculate(Celebrity celebrity)
    {
        var settings = Settings;
        var utc = BirthTimeResolver.ToUtc(celebrity);
        double jd = SwissEphemeris.DateTimeToJulianDay(utc);

        List<PlanetPosition> planets;
        List<HouseCusp> houses;
        double asc, mc;
        bool substituted;
        lock (SweLock)
        {
            planets = CalculatePlanets(jd, settings.Node);
            (houses, asc, mc, substituted) = CalculateHouses(jd, celebrity.Latitude, celebrity.Longitude, settings.Houses);
        }

        // Aspect detection is pure managed arithmetic on the results above — no native
        // state — so it stays outside the lock.
        var aspects = CalculateAspects(planets);

        return new NatalChart
        {
            Celebrity = celebrity,
            Planets = planets,
            Houses = houses,
            Aspects = aspects,
            Ascendant = asc,
            Midheaven = mc,
            Settings = settings,
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

    private static List<PlanetPosition> CalculatePlanets(double jd, NodeType node)
    {
        var result = new List<PlanetPosition>(PlanetMap.Length);
        var xx = new double[6];
        var eq = new double[6];
        int flags = SwissEphemeris.SEFLG_SWIEPH | SwissEphemeris.SEFLG_SPEED;

        foreach (var (planet, _) in PlanetMap)
        {
            int ret = SwissEphemeris.CalcUt(jd, SweBody(planet, node), flags, xx, nint.Zero);
            if (ret < 0)
                continue; // skip bodies that fail (e.g., Chiron outside data range)

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

    private static List<Aspect> CalculateAspects(List<PlanetPosition> planets)
    {
        var aspects = new List<Aspect>();
        var types = Enum.GetValues<AspectType>();

        for (int i = 0; i < planets.Count; i++)
        for (int j = i + 1; j < planets.Count; j++)
        {
            double angle = AngleBetween(planets[i].Longitude, planets[j].Longitude);

            foreach (var type in types)
            {
                double exactAngle = type.Angle();
                double orb = Math.Abs(angle - exactAngle);
                if (orb <= type.Orb())
                {
                    // applying = faster planet is moving toward the exact angle
                    bool applying = IsApplying(planets[i], planets[j], type);
                    aspects.Add(new Aspect
                    {
                        PlanetA = planets[i].Planet,
                        PlanetB = planets[j].Planet,
                        Type = type,
                        Orb = orb,
                        IsApplying = applying,
                    });
                    break; // one aspect per pair
                }
            }
        }

        return aspects;
    }

    private static double AngleBetween(double lonA, double lonB)
    {
        double diff = Math.Abs(lonA - lonB) % 360;
        return diff > 180 ? 360 - diff : diff;
    }

    private static bool IsApplying(PlanetPosition a, PlanetPosition b, AspectType type)
    {
        // Applying = the separation to the exact angle is shrinking. Advance both planets
        // by their own hourly motion (so relative speed is captured) and compare.
        double currentAngle = AngleBetween(a.Longitude, b.Longitude);
        double nextA = a.Longitude + a.SpeedLongitude / 24.0; // advance 1 hour
        double nextB = b.Longitude + b.SpeedLongitude / 24.0;
        double nextAngle = AngleBetween(nextA, nextB);
        double exact = type.Angle();
        return Math.Abs(nextAngle - exact) < Math.Abs(currentAngle - exact);
    }
}
