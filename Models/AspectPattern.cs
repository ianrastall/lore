namespace Lore.Models;

public enum PatternType
{
    Stellium,    // 3+ bodies in one sign
    GrandTrine,  // 3 bodies in mutual trine (a closed triangle)
    TSquare,     // an opposition with a third body square to both ends
    GrandCross   // two oppositions locked together by four squares
}

// A multi-body configuration detected from the chart's positions and its
// already-computed major aspects. Descriptor fields are populated only where
// they apply to the pattern (see each property).
public sealed class AspectPattern
{
    public required PatternType Type { get; init; }
    public required IReadOnlyList<Planet> Planets { get; init; }

    public ZodiacSign? Sign { get; init; }      // Stellium: the shared sign
    public Element? Element { get; init; }       // Grand Trine: the shared element
    public Modality? Modality { get; init; }     // T-Square / Grand Cross: the shared modality
    public Planet? Apex { get; init; }           // T-Square: the body squaring both ends of the opposition
}
