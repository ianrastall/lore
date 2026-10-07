namespace Lore.Models;

public enum Planet
{
    Sun = 0, Moon, Mercury, Venus, Mars,
    Jupiter, Saturn, Uranus, Neptune, Pluto,
    NorthNode, Chiron, Lilith
}

public static class PlanetExtensions
{
    private static readonly string[] Symbols =
        ["☉", "☽", "☿", "♀", "♂", "♃", "♄", "♅", "♆", "♇", "☊", "⚷", "⚸"];

    private static readonly string[] Names =
        ["Sun", "Moon", "Mercury", "Venus", "Mars",
         "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto",
         "North Node", "Chiron", "Lilith"];

    public static string Symbol(this Planet p) => Symbols[(int)p];
    public static string Name(this Planet p) => Names[(int)p];

    public static bool IsPersonal(this Planet p) =>
        p is Planet.Sun or Planet.Moon or Planet.Mercury or Planet.Venus or Planet.Mars;
}

public sealed class PlanetPosition
{
    public required Planet Planet { get; init; }
    public double Longitude { get; init; }    // ecliptic longitude, 0–360°
    public double Latitude { get; init; }     // ecliptic latitude
    public double Declination { get; init; }  // degrees north (+) or south (−) of the celestial equator
    public double SpeedLongitude { get; init; } // degrees/day; negative = retrograde

    // The rest of what the ephemeris returns. HasEquatorial is false when the equatorial
    // pass failed (or was never made): the declination is then unknown, not zero.
    public bool HasEquatorial { get; init; }
    public double RightAscension { get; init; }   // degrees, 0–360
    public double SpeedDeclination { get; init; } // degrees/day
    public double SpeedLatitude { get; init; }    // degrees/day
    public double Distance { get; init; }         // from the Earth, in astronomical units
    // The mean node always runs backwards and mean Lilith always forwards; neither has
    // retrograde periods, so neither is ever marked.
    public bool IsRetrograde => SpeedLongitude < 0 && Planet is not (Planet.NorthNode or Planet.Lilith);

    // Standing nearly still, as a planet does for some days either side of turning
    // retrograde or direct: moving at under roughly a tenth of its usual speed. The Sun
    // and Moon never do; the mean node and mean Lilith move at a steady rate and never
    // do either, but the true node and true Lilith stop and turn often.
    public bool IsStationary => Math.Abs(SpeedLongitude) < StationarySpeed[(int)Planet];

    // Degrees a day, by Planet order.
    private static readonly double[] StationarySpeed =
        [0, 0, 0.10, 0.06, 0.03, 0.012, 0.008, 0.004, 0.003, 0.003, 0.01, 0.005, 0.05];

    // "℞", "S" (stationary) or "S℞"; empty for ordinary direct motion.
    public string MotionMark => (IsStationary ? "S" : "") + (IsRetrograde ? "℞" : "");

    public ZodiacSign Sign => ZodiacSignExtensions.FromLongitude(Longitude);
    public double DegreeInSign => ZodiacSignExtensions.DegreeInSign(Longitude);

    // Convenience properties for x:Bind in DataTemplates (extension methods can't be called from x:Bind)
    public string PlanetSymbol => Planet.Symbol();
    public string PlanetName => Planet.Name();

    public string FormatPosition()
    {
        int deg = (int)DegreeInSign;
        int min = (int)((DegreeInSign - deg) * 60);
        return $"{deg:D2}°{min:D2}' {Sign.Symbol()} {Sign.Name()}{(IsRetrograde ? " ℞" : "")}";
    }
}
