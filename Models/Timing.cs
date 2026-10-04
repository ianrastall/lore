namespace Lore.Models;

// The chart for the moment, in one particular year, when the Sun comes back to exactly
// where it stood at birth — cast for the birthplace.
public sealed class SolarReturn
{
    public required int Year { get; init; }
    public required DateTime Utc { get; init; }
    public required NatalChart Chart { get; init; }
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

// The Timing view's content for one chart on one date.
public sealed class TimingReading
{
    public required string Name { get; init; }
    public required DateOnly AsOf { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }
    public SolarReturn? Return { get; init; }       // null without a birth time
    public required Progression Progression { get; init; }
}
