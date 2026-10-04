namespace Lore.Models;

// The numbers behind a chart, laid out for checking: how the birth time became
// Universal Time, every position in full, the house cusps, and every aspect.
public sealed class Worksheet
{
    public required string Name { get; init; }
    public required IReadOnlyList<WorksheetFact> Facts { get; init; }
    public required IReadOnlyList<WorksheetRow> Positions { get; init; }
    public required IReadOnlyList<WorksheetCusp> Cusps { get; init; }   // empty without a birth time

    // The points of the aspect grid, in order, and the aspects between them.
    public required IReadOnlyList<NatalPoint> Points { get; init; }
    public required IReadOnlyList<WorksheetAspect> Aspects { get; init; }

    public bool HasCusps => Cusps.Count > 0;

    public WorksheetAspect? AspectBetween(NatalPoint a, NatalPoint b) =>
        Aspects.FirstOrDefault(x => (x.A == a && x.B == b) || (x.A == b && x.B == a));
}

// One line of the "how this was calculated" block.
public sealed record WorksheetFact(string Label, string Value);

// One body or point. Everything is already formatted; blank where it does not apply.
public sealed record WorksheetRow(
    string Symbol, string Name, string Position, string Latitude, string Declination,
    string Speed, string House, string Motion);

public sealed record WorksheetCusp(string House, string Position);

public sealed record WorksheetAspect(
    NatalPoint A, NatalPoint B, AspectType Type, double Orb, bool Applying, bool OutOfSign = false)
{
    // "2°14' a" — the orb, then a for applying or s for separating.
    public string OrbText
    {
        get
        {
            int deg = (int)Orb;
            int min = Math.Min(59, (int)((Orb - deg) * 60));
            return $"{deg}°{min:D2}' {(Applying ? "a" : "s")}";
        }
    }
}
