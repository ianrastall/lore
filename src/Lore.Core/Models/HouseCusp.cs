namespace Lore.Models;

public sealed class HouseCusp
{
    public required int House { get; init; } // 1–12
    public double Longitude { get; init; }   // ecliptic longitude of cusp, 0–360°

    public ZodiacSign Sign => ZodiacSignExtensions.FromLongitude(Longitude);
    public double DegreeInSign => ZodiacSignExtensions.DegreeInSign(Longitude);

    public string Label => House switch
    {
        1 => "ASC",
        4 => "IC",
        7 => "DSC",
        10 => "MC",
        _ => $"H{House}"
    };
}
