namespace Lore.Models;

public enum AspectType
{
    Conjunction,  //   0° orb 8°
    Sextile,      //  60° orb 6°
    Square,       //  90° orb 8°
    Trine,        // 120° orb 8°
    Opposition    // 180° orb 8°
}

public static class AspectTypeExtensions
{
    public static double Angle(this AspectType a) => a switch
    {
        AspectType.Conjunction => 0,
        AspectType.Sextile     => 60,
        AspectType.Square      => 90,
        AspectType.Trine       => 120,
        AspectType.Opposition  => 180,
        _ => 0
    };

    public static double Orb(this AspectType a) => a switch
    {
        AspectType.Sextile => 6,
        _ => 8
    };

    public static string Symbol(this AspectType a) => a switch
    {
        AspectType.Conjunction => "☌",
        AspectType.Sextile     => "⚹",
        AspectType.Square      => "□",
        AspectType.Trine       => "△",
        AspectType.Opposition  => "☍",
        _ => "?"
    };
}

public sealed class Aspect
{
    public required Planet PlanetA { get; init; }
    public required Planet PlanetB { get; init; }
    public required AspectType Type { get; init; }
    public double Orb { get; init; }      // actual orb in degrees
    public bool IsApplying { get; init; } // applying (closing) vs separating

    public string Description =>
        $"{PlanetA.Symbol()} {Type.Symbol()} {PlanetB.Symbol()} ({Orb:F1}°{(IsApplying ? " app." : " sep.")})";
}
