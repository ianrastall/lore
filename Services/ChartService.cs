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
        (Planet.NorthNode, SwissEphemeris.SE_MEAN_NODE),
        (Planet.Chiron,    SwissEphemeris.SE_CHIRON),
    ];

    private const int HouseSystem = 'P'; // Placidus

    public ChartService(string ephemerisPath)
    {
        SwissEphemeris.SetEphePath(ephemerisPath);
    }

    public NatalChart Calculate(Celebrity celebrity)
    {
        var utc = celebrity.GetUtcBirthDateTime();
        double jd = SwissEphemeris.DateTimeToJulianDay(utc);

        var planets = CalculatePlanets(jd);
        var (houses, asc, mc) = CalculateHouses(jd, celebrity.Latitude, celebrity.Longitude);
        var aspects = CalculateAspects(planets);

        return new NatalChart
        {
            Celebrity = celebrity,
            Planets = planets,
            Houses = houses,
            Aspects = aspects,
            Ascendant = asc,
            Midheaven = mc,
        };
    }

    private static List<PlanetPosition> CalculatePlanets(double jd)
    {
        var result = new List<PlanetPosition>(PlanetMap.Length);
        var xx = new double[6];
        int flags = SwissEphemeris.SEFLG_SWIEPH | SwissEphemeris.SEFLG_SPEED;

        foreach (var (planet, sweBody) in PlanetMap)
        {
            int ret = SwissEphemeris.CalcUt(jd, sweBody, flags, xx, nint.Zero);
            if (ret < 0)
                continue; // skip bodies that fail (e.g., Chiron outside data range)

            result.Add(new PlanetPosition
            {
                Planet = planet,
                Longitude = xx[0],
                Latitude = xx[1],
                SpeedLongitude = xx[3],
            });
        }

        return result;
    }

    private static (List<HouseCusp> houses, double asc, double mc) CalculateHouses(
        double jd, double lat, double lon)
    {
        var cusps = new double[13];
        var ascmc = new double[10];

        SwissEphemeris.Houses(jd, lat, lon, HouseSystem, cusps, ascmc);

        var houses = Enumerable.Range(1, 12)
            .Select(i => new HouseCusp { House = i, Longitude = cusps[i] })
            .ToList();

        return (houses, ascmc[SwissEphemeris.SE_ASC], ascmc[SwissEphemeris.SE_MC]);
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
        // The faster-moving planet applies to the aspect if the angle is decreasing
        var faster = Math.Abs(a.SpeedLongitude) >= Math.Abs(b.SpeedLongitude) ? a : b;
        var slower = faster == a ? b : a;
        double currentAngle = AngleBetween(a.Longitude, b.Longitude);
        double nextA = a.Longitude + a.SpeedLongitude / 24.0; // advance 1 hour
        double nextB = b.Longitude + b.SpeedLongitude / 24.0;
        double nextAngle = AngleBetween(nextA, nextB);
        double exact = type.Angle();
        return Math.Abs(nextAngle - exact) < Math.Abs(currentAngle - exact);
    }
}
