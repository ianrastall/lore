namespace Lore.Models;

// One stretch during which a moving body stays within orb of an aspect to a natal
// point: when it comes into orb, when it is exact (more than once if the body turns
// retrograde while still in orb; never, if it turns back just short), and when it leaves.
public sealed class TransitPass
{
    public required Planet Mover { get; init; }
    public required NatalPoint Target { get; init; }
    public required AspectType Aspect { get; init; }

    public DateTime? EnterUtc { get; init; }   // null: already within orb when the forecast begins
    public DateTime? LeaveUtc { get; init; }   // null: still within orb when the forecast ends
    public required IReadOnlyList<DateTime> ExactUtc { get; init; }

    public DateTime PeakUtc { get; init; }     // the first exact moment, or else the closest approach
    public double MinOrb { get; init; }        // 0 when exact; otherwise how close it comes, in degrees
    public bool MoverRetrograde { get; init; } // at the peak
    public int? TargetHouse { get; init; }     // the natal point's house; null without a birth time

    public bool IsExact => ExactUtc.Count > 0;
    public bool IsBackground => Mover.IsSlowMover();
    public TransitTone Tone => Aspect.Tone();
}

// The transits coming up for one chart over a stretch of days, written out month by month.
public sealed class ForecastReading
{
    public required string Name { get; init; }
    public required DateOnly Start { get; init; }
    public required int Days { get; init; }
    public required string ZoneId { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; } // one per month
    public required IReadOnlyList<TransitPass> Passes { get; init; }    // in order of their peaks

    public DateOnly End => Start.AddDays(Days - 1);
    public string RangeText => $"{Start:d MMMM yyyy} to {End:d MMMM yyyy}";
}
