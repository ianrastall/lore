namespace Lore.Models;

public enum Planet
{
    Sun = 0, Moon, Mercury, Venus, Mars,
    Jupiter, Saturn, Uranus, Neptune, Pluto,
    NorthNode, Chiron
}

public static class PlanetExtensions
{
    private static readonly string[] Symbols =
        ["☉", "☽", "☿", "♀", "♂", "♃", "♄", "♅", "♆", "♇", "☊", "⚷"];

    private static readonly string[] Names =
        ["Sun", "Moon", "Mercury", "Venus", "Mars",
         "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto",
         "North Node", "Chiron"];

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
    public double SpeedLongitude { get; init; } // degrees/day; negative = retrograde
    public bool IsRetrograde => SpeedLongitude < 0;

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
