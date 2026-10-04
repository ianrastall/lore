namespace Lore.Models;

public sealed class NatalChart
{
    public required Celebrity Celebrity { get; init; }
    public required IReadOnlyList<PlanetPosition> Planets { get; init; }
    public required IReadOnlyList<HouseCusp> Houses { get; init; }    // 12 entries, index 0 = house 1
    public required IReadOnlyList<Aspect> Aspects { get; init; }

    // The bodies' aspects to the Ascendant and Midheaven; empty without a birth time.
    public IReadOnlyList<AngleAspect> AngleAspects { get; init; } = [];
    public double Ascendant { get; init; }   // ecliptic longitude
    public double Midheaven { get; init; }   // ecliptic longitude

    // The house system the cusps were actually calculated in, as shown to the user,
    // e.g. "Placidus houses" — or a note that another system had to stand in for it.
    public string HouseSystemLabel { get; init; } = "Placidus houses";

    // The settings this chart was calculated with.
    public ChartSettings Settings { get; init; } = ChartSettings.Default;

    // True when the Sun is above the horizon. The Sun sits on the ecliptic, so it is up
    // exactly when its longitude lies in the half running from the Descendant over the
    // Midheaven to the Ascendant — whatever the house system. Meaningless if not Timed.
    public bool IsDayChart =>
        GetPlanet(Planet.Sun) is { } sun && (((sun.Longitude - Ascendant) % 360) + 360) % 360 >= 180;

    // Without a birth time the planets are placed for noon and the Ascendant, Midheaven
    // and houses are unknown; nothing should be read from them.
    public bool Timed => Celebrity.BirthTimeKnown;

    public PlanetPosition? GetPlanet(Planet p) =>
        Planets.FirstOrDefault(x => x.Planet == p);

    // Ecliptic longitude of a body or angle; null for a body the chart does not have.
    public double? LongitudeOf(NatalPoint point) => point.Kind switch
    {
        NatalPointKind.Ascendant => Ascendant,
        NatalPointKind.Midheaven => Midheaven,
        _ => GetPlanet(point.Body)?.Longitude
    };

    public HouseCusp GetHouse(int house) =>
        Houses[house - 1];

    public int GetHouseForLongitude(double longitude)
    {
        double lon = ((longitude % 360) + 360) % 360;
        for (int i = 0; i < 12; i++)
        {
            double start = ((Houses[i].Longitude % 360) + 360) % 360;
            double end = ((Houses[(i + 1) % 12].Longitude % 360) + 360) % 360;
            if (end < start) end += 360;
            double test = lon < start ? lon + 360 : lon;
            if (test >= start && test < end)
                return i + 1;
        }
        return 1;
    }
}
