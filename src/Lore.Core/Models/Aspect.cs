namespace Lore.Models;

public enum AspectType
{
    Conjunction,  //   0° orb 8°
    Sextile,      //  60° orb 6°
    Square,       //  90° orb 8°
    Trine,        // 120° orb 8°
    Opposition,   // 180° orb 8°

    // The minor aspects: found only in a birth chart, and only when switched on.
    SemiSextile,    //  30°
    SemiSquare,     //  45°
    Sesquiquadrate, // 135°
    Quincunx,       // 150°
    Quintile,       //  72°, a fifth of the circle
    Biquintile      // 144°, two fifths
}

public static class AspectTypeExtensions
{
    // The five major (Ptolemaic) aspects — the only ones transits and synastry look for.
    public static readonly IReadOnlyList<AspectType> Majors =
    [
        AspectType.Conjunction, AspectType.Sextile, AspectType.Square, AspectType.Trine, AspectType.Opposition,
    ];

    public static readonly IReadOnlyList<AspectType> Minors =
    [
        AspectType.SemiSextile, AspectType.SemiSquare, AspectType.Sesquiquadrate, AspectType.Quincunx,
        AspectType.Quintile, AspectType.Biquintile,
    ];

    public static bool IsMajor(this AspectType a) => a <= AspectType.Opposition;

    // How many signs apart two points "should" be for this aspect — a trine joins signs
    // four apart, of one element. Null for the semi-square, sesquiquadrate, quintile and
    // biquintile, whose angles fall across sign boundaries by nature.
    public static int? SignsApart(this AspectType a) => a switch
    {
        AspectType.Conjunction => 0,
        AspectType.SemiSextile => 1,
        AspectType.Sextile     => 2,
        AspectType.Square      => 3,
        AspectType.Trine       => 4,
        AspectType.Quincunx    => 5,
        AspectType.Opposition  => 6,
        _ => null
    };

    // An aspect is out of sign (dissociate) when it is within orb by degree but the two
    // signs are not in that relationship: a conjunction from 28° Aries to 2° Taurus.
    // Traditional practice reads such an aspect as weaker, or not at all.
    public static bool IsOutOfSign(this AspectType a, double longitudeA, double longitudeB)
    {
        if (a.SignsApart() is not { } expected) return false;
        int apart = Math.Abs((int)ZodiacSignExtensions.FromLongitude(longitudeA) -
                             (int)ZodiacSignExtensions.FromLongitude(longitudeB));
        if (apart > 6) apart = 12 - apart;
        return apart != expected;
    }

    // As written for a reader: "Semi-square", not the enum's "SemiSquare".
    public static string Name(this AspectType a) => a switch
    {
        AspectType.SemiSextile => "Semi-sextile",
        AspectType.SemiSquare  => "Semi-square",
        _ => a.ToString()
    };

    public static double Angle(this AspectType a) => a switch
    {
        AspectType.SemiSextile    => 30,
        AspectType.SemiSquare     => 45,
        AspectType.Sesquiquadrate => 135,
        AspectType.Quincunx       => 150,
        AspectType.Quintile       => 72,
        AspectType.Biquintile     => 144,
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
        _ when !a.IsMajor() => 2,
        _ => 8
    };

    public static string Symbol(this AspectType a) => a switch
    {
        AspectType.Conjunction => "☌",
        AspectType.Sextile     => "⚹",
        AspectType.Square      => "□",
        AspectType.Trine       => "△",
        AspectType.Opposition  => "☍",
        AspectType.SemiSextile    => "⚺",
        AspectType.SemiSquare     => "∠",
        AspectType.Sesquiquadrate => "⚼",
        AspectType.Quincunx       => "⚻",
        AspectType.Quintile       => "Q",
        AspectType.Biquintile     => "bQ",
        _ => "?"
    };
}

public sealed class Aspect
{
    public required Planet PlanetA { get; init; }
    public required Planet PlanetB { get; init; }
    public required AspectType Type { get; init; }
    public double Orb { get; init; }      // actual orb in degrees
    public double Allowed { get; init; }  // the widest orb the settings allowed for this pair
    public bool IsApplying { get; init; } // applying (closing) vs separating
    public bool OutOfSign { get; init; }  // within orb, but the signs are not in this aspect (see IsOutOfSign)

    public string Description =>
        $"{PlanetA.Symbol()} {Type.Symbol()} {PlanetB.Symbol()} ({Orb:F1}°{(IsApplying ? " app." : " sep.")})";
}

// An aspect from one of the chart's bodies to its Ascendant or Midheaven (or, on the
// Worksheet, to its Vertex or Part of Fortune). Kept apart from Aspect (which joins two
// bodies) because an angle is not a Planet; only a chart with a birth time has any.
public sealed class AngleAspect
{
    public required Planet Planet { get; init; }
    public required NatalPoint Angle { get; init; }
    public required AspectType Type { get; init; }
    public double Orb { get; init; }      // actual orb in degrees
    public double Allowed { get; init; }  // the widest orb the settings allowed for this pair
    public bool IsApplying { get; init; } // by the planet's own motion, the angle held still
    public bool OutOfSign { get; init; }  // within orb, but the signs are not in this aspect

    public string Description =>
        $"{Planet.Symbol()} {Type.Symbol()} {Angle.Symbol} ({Orb:F1}°{(IsApplying ? " app." : " sep.")})";
}
