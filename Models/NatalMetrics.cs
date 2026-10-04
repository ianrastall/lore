namespace Lore.Models;

// The measurements taken from a birth chart beyond its positions, cusps and aspects:
// further points, the Moon's phase, each body's relation to the Sun, contacts by
// declination, closeness to the angles, the balance of the chart, and its rulers.
// Everything here is a number or a name, unformatted; the Worksheet and the exports
// both read from this one result so that they cannot disagree.
public sealed class NatalMetrics
{
    // Points worked out from others rather than observed: the South Node always, and
    // with a birth time the Descendant, IC, Vertex and the Lots of Fortune and Spirit.
    public required IReadOnlyList<DerivedPoint> Points { get; init; }

    public LunarPhase? Moon { get; init; }                 // null if the chart lacks a Sun or Moon
    public required IReadOnlyList<SolarRelation> Solar { get; init; }

    // The tilt of the Earth's axis at the birth, which is the Sun's greatest possible
    // declination; null if the ephemeris could not supply it.
    public double? Obliquity { get; init; }
    public required IReadOnlyList<OutOfBounds> OutOfBounds { get; init; }
    public required IReadOnlyList<DeclinationContact> Parallels { get; init; }

    public required IReadOnlyList<Angularity> Angularity { get; init; }   // empty without a birth time
    public required Distribution Distribution { get; init; }
    public required AspectSummary Aspects { get; init; }
    public required Rulership Rulers { get; init; }

    public double? LongitudeOf(string point) => Points.FirstOrDefault(p => p.Name == point)?.Longitude;
}

public sealed record DerivedPoint(string Name, string Symbol, double Longitude);

// The Moon's place in its cycle at birth. Elongation is how far the Moon is ahead of the
// Sun in longitude (0° new, 180° full); the phase name is the eighth of the cycle that
// angle falls in, each eighth centred on its exact phase (so "Full Moon" runs from
// 157°30' to 202°30'). Illumination is the lit fraction of the disc, 0 to 1.
public sealed record LunarPhase(double Elongation, MoonPhase Phase, bool Waxing, double Illumination);

public enum SolarCondition { None, Cazimi, Combust, UnderBeams }

// One body's distance from the Sun along the zodiac. A body ahead of the Sun (East) sets
// after it and is seen in the evening; one behind it (West) rises before it, in the morning.
// Condition is the traditional reading of a close approach, given for Moon to Saturn only.
public sealed record SolarRelation(Planet Planet, double Elongation, bool EastOfSun, SolarCondition Condition);

// A body whose declination is beyond the Sun's greatest: Excess is by how much, in degrees.
public sealed record OutOfBounds(Planet Planet, double Declination, double Excess);

// Two bodies at the same declination (a parallel, both on one side of the equator) or at
// equal declinations on opposite sides (a contra-parallel).
public sealed record DeclinationContact(Planet A, Planet B, bool Contra, double Orb, bool Applying);

// How close a body is to the nearest of the four angles, and how far into its house it is.
public sealed record Angularity(Planet Planet, string NearestAngle, double Distance, int House, double IntoHouse);

public sealed record Tally(string Name, int Count);

// How the chart's bodies are spread. Total is how many bodies were counted (all those in
// the chart: the ten planets, the North Node, Chiron and Lilith). The house-based
// tallies are empty without a birth time.
public sealed class Distribution
{
    public required int Total { get; init; }
    public required IReadOnlyList<Tally> Elements { get; init; }
    public required IReadOnlyList<Tally> Modalities { get; init; }
    public required IReadOnlyList<Tally> Polarities { get; init; }
    public IReadOnlyList<Tally> HouseTypes { get; init; } = [];     // angular / succedent / cadent
    public IReadOnlyList<Tally> Hemispheres { get; init; } = [];    // upper / lower, eastern / western, by house
}

public sealed class AspectSummary
{
    public required IReadOnlyList<Tally> ByType { get; init; }       // body-to-body aspects only
    public required int Applying { get; init; }
    public required int Separating { get; init; }
    public Aspect? Closest { get; init; }
    public required IReadOnlyList<Tally> PerBody { get; init; }      // most aspected first
    // Planets (Sun to Pluto) making no major aspect to any other body, on the orbs in force.
    public required IReadOnlyList<Planet> Unaspected { get; init; }
}

// The ruler of one house: the traditional ruler of the sign on its cusp, and where that
// planet is.
public sealed record HouseRuler(int House, ZodiacSign CuspSign, Planet Ruler, ZodiacSign? RulerSign, int? RulerHouse);

// Who rules the degree a body stands in: its sign (the dispositor), its Egyptian term
// (bound) and its Chaldean face (decan). Chain follows dispositor to dispositor until it
// reaches a planet in its own sign or comes back on itself.
public sealed record Disposition(Planet Planet, ZodiacSign Sign, Planet SignRuler, Planet TermRuler, Planet FaceRuler,
    IReadOnlyList<Planet> Chain);

public sealed class Rulership
{
    // The ruler of the rising sign; null without a birth time.
    public Planet? ChartRuler { get; init; }
    public Planet? ModernChartRuler { get; init; }     // Uranus, Neptune or Pluto, where the rising sign has one
    public IReadOnlyList<HouseRuler> Houses { get; init; } = [];
    public required IReadOnlyList<Disposition> Dispositions { get; init; }
    public required IReadOnlyList<Planet> InDomicile { get; init; }
    // The one planet every chain ends at, if there is one.
    public Planet? FinalDispositor { get; init; }
    // Planets that dispose of each other in a ring; a ring of two is a mutual reception.
    public required IReadOnlyList<IReadOnlyList<Planet>> Loops { get; init; }
}
