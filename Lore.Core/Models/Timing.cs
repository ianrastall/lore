namespace Lore.Models;

// Somewhere a solar return is cast for other than the birthplace: usually where the
// person was living that year.
public sealed record ReturnPlace(string Name, double Latitude, double Longitude);

// The chart for the moment, in one particular year, when the Sun comes back to exactly
// where it stood at birth — cast for the birthplace unless another place was chosen.
// The moment is the same wherever it is cast for; the Ascendant and houses are not.
public sealed class SolarReturn
{
    public required int Year { get; init; }
    public required DateTime Utc { get; init; }
    public required NatalChart Chart { get; init; }
    public ReturnPlace? Place { get; init; }        // null: the birthplace
}

// One progressed position beside the natal one it grew from.
public sealed record ProgressedPoint(NatalPoint Point, double Longitude, double NatalLongitude, bool Retrograde);

// A progressed point within orb of an aspect to a point in the birth chart.
public sealed record ProgressedAspect(NatalPoint Progressed, NatalPoint Natal, AspectType Type, double Orb);

// The birth chart moved on by the "day for a year" rule: the sky as it stood as many
// days after birth as the person is years old.
public sealed class Progression
{
    public required DateOnly AsOf { get; init; }
    public required double AgeYears { get; init; }
    public required DateTime ProgressedUtc { get; init; }    // the instant the sky is read for
    public required IReadOnlyList<ProgressedPoint> Points { get; init; }
    public required IReadOnlyList<ProgressedAspect> Aspects { get; init; }
    public required MoonPhase MoonPhase { get; init; }
    public required double Elongation { get; init; }          // progressed Moon ahead of progressed Sun, 0–360°

    public ProgressedPoint? Get(NatalPoint point) => Points.FirstOrDefault(p => p.Point == point);
}

// The annual profection: one house of the birth chart "switched on" for each year of
// life, counted in whole signs from the rising sign (the first house in the first year,
// the second in the next, and round again every twelve). The sign's traditional ruler is
// the Lord of the Year; LordSign and LordHouse say where it stands in the birth chart.
public sealed record Profection(
    int Age, int House, ZodiacSign Sign, Planet Lord, ZodiacSign? LordSign, int? LordHouse,
    DateOnly From, DateOnly Until);

// The birth chart directed by solar arc: every point moved on by the distance the
// progressed Sun has travelled since birth (about a degree a year).
public sealed class SolarArc
{
    public required double Arc { get; init; }
    public required IReadOnlyList<ProgressedPoint> Points { get; init; }     // never retrograde
    public required IReadOnlyList<ProgressedAspect> Aspects { get; init; }   // directed to natal, closest first
}

// The birth chart as it would stand had the person been born at the same instant
// somewhere else: the same planets, under that place's horizon and houses.
public sealed record Relocation(ReturnPlace Place, NatalChart Chart);

// The Timing view's content for one chart on one date.
public sealed class TimingReading
{
    public required string Name { get; init; }
    public required DateOnly AsOf { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }
    public SolarReturn? Return { get; init; }       // null without a birth time
    public required Progression Progression { get; init; }
    public Profection? Profection { get; init; }    // null without a birth time, or before the birth
    public SolarArc? SolarArc { get; init; }        // null if the Sun could not be progressed
    public Relocation? Relocation { get; init; }    // null unless another place was chosen
}
