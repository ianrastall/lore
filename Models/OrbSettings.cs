namespace Lore.Models;

// How far from exact an aspect inside one chart may be and still count: an orb, in
// degrees, for each of the five aspects, plus an extra allowance when the Sun or Moon is
// one of the two bodies (the luminaries are traditionally given more room).
//
// These govern the birth chart only. Synastry and the daily horoscope keep their own,
// tighter orbs, because they compare a chart with something outside it.
public sealed record OrbSettings(
    double Conjunction = 8, double Sextile = 6, double Square = 8, double Trine = 8, double Opposition = 8,
    double LuminaryBonus = 0)
{
    public const double Max = 15;

    // Whether the birth chart also looks for the minor aspects (semi-sextile, semi-square,
    // sesquiquadrate, quincunx, quintile, biquintile), and the one orb they all share.
    // Off unless asked for.
    public bool MinorAspects { get; init; }
    public double Minor { get; init; } = 2;

    // The aspects in force: the five majors, and the minors if they are switched on.
    public IEnumerable<AspectType> Types() =>
        MinorAspects ? AspectTypeExtensions.Majors.Concat(AspectTypeExtensions.Minors) : AspectTypeExtensions.Majors;

    // What Lore has always used.
    public static readonly OrbSettings Lore = new();

    // Closer aspects only, with two degrees more for the Sun and Moon.
    public static readonly OrbSettings Tight = new(6, 4, 6, 6, 6, LuminaryBonus: 2);

    // The generous orbs many chart services draw with.
    public static readonly OrbSettings Wide = new(10, 6, 10, 10, 10);

    // The named sets, in the order the Settings menu lists them.
    public static readonly IReadOnlyList<(string Name, OrbSettings Orbs)> Presets =
    [
        ("Standard", Lore),
        ("Tight", Tight),
        ("Wide", Wide),
    ];

    // The orb allowed for one aspect between two particular bodies.
    // (The Sun and Moon allowance widens the major aspects only.)
    public double For(AspectType type, Planet a, Planet b) =>
        !type.IsMajor() ? Minor : Base(type) + (IsLuminary(a) || IsLuminary(b) ? LuminaryBonus : 0);

    // The orb allowed for an aspect from one body to an angle.
    public double For(AspectType type, Planet a) =>
        !type.IsMajor() ? Minor : Base(type) + (IsLuminary(a) ? LuminaryBonus : 0);

    public double Base(AspectType type) => type switch
    {
        AspectType.Conjunction => Conjunction,
        AspectType.Sextile     => Sextile,
        AspectType.Square      => Square,
        AspectType.Trine       => Trine,
        _                      => Opposition,
    };

    private static bool IsLuminary(Planet p) => p is Planet.Sun or Planet.Moon;

    // Values from a hand-edited or damaged settings file, pulled back into range.
    public OrbSettings Clamped() => new(
        Clamp(Conjunction), Clamp(Sextile), Clamp(Square), Clamp(Trine), Clamp(Opposition), Clamp(LuminaryBonus))
    {
        MinorAspects = MinorAspects,
        Minor = Clamp(Minor),
    };

    private static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, Max);

    // "Standard", "Tight", "Wide", or "Custom" when it matches none of them.
    // (Named by the major orbs alone; the minor aspects are a separate switch.)
    public string PresetName => Presets.FirstOrDefault(p => p.Orbs == MajorsOnly).Name ?? "Custom";

    private OrbSettings MajorsOnly => this with { MinorAspects = false, Minor = 2 };

    // "8° conjunction, 6° sextile, 8° square, 8° trine, 8° opposition; 2° more with the Sun or Moon"
    public string Describe()
    {
        string text = $"{Conjunction:0.#}° conjunction, {Sextile:0.#}° sextile, {Square:0.#}° square, " +
                      $"{Trine:0.#}° trine, {Opposition:0.#}° opposition";
        if (LuminaryBonus > 0) text += $"; {LuminaryBonus:0.#}° more with the Sun or Moon";
        text = $"{text} ({PresetName})";
        return MinorAspects ? $"{text}; minor aspects {Minor:0.#}°" : $"{text}; minor aspects off";
    }
}
