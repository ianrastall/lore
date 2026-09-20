namespace Lore.Models;

public sealed class NatalChart
{
    public required Celebrity Celebrity { get; init; }
    public required IReadOnlyList<PlanetPosition> Planets { get; init; }
    public required IReadOnlyList<HouseCusp> Houses { get; init; }    // 12 entries, index 0 = house 1
    public required IReadOnlyList<Aspect> Aspects { get; init; }
    public double Ascendant { get; init; }   // ecliptic longitude
    public double Midheaven { get; init; }   // ecliptic longitude

    public PlanetPosition? GetPlanet(Planet p) =>
        Planets.FirstOrDefault(x => x.Planet == p);

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
