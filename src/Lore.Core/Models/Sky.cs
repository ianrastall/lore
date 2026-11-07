namespace Lore.Models;

// One aspect between two moving planets during the day being read: the sky's own
// weather, the same for everyone. First is the faster planet in the conventional
// order (the Moon, then the Sun out to Pluto), so "Mars square Saturn", never the
// other way round.
public sealed class SkyAspect
{
    public required Planet First { get; init; }
    public required Planet Second { get; init; }
    public required AspectType Aspect { get; init; }

    public double MinOrb { get; init; }          // tightest orb reached within the day, degrees
    public DateTime PeakUtc { get; init; }       // when that happens
    public TransitPhase Phase { get; init; }
    public DateTime? ExactUtc { get; init; }     // nearest exact moment (inside or outside the day), if found
    public bool FirstRetrograde { get; init; }
    public bool SecondRetrograde { get; init; }

    // The Moon's contacts come and go within hours; aspects between Jupiter and the
    // planets beyond it hold for weeks and form the background.
    public bool IsLunar => First == Planet.Moon;
    public bool IsBackground => First.IsSlowMover();
    public TransitTone Tone => Aspect.Tone();

    // Between two of Uranus, Neptune and Pluto: in orb for years at a time, the backdrop
    // of an era rather than of a day, so it is read but does not set the day's tone.
    public bool IsGenerational => First is Planet.Uranus or Planet.Neptune or Planet.Pluto;

    // Editorial priority assigned by the interpreter, not a probability of anything.
    public double Score { get; set; }
    public bool Shown { get; set; }

    // The order planets are named in: the Moon first, then Sun to Pluto.
    public static int Rank(Planet p) => p == Planet.Moon ? -1 : (int)p;
}

// Everything the sky scan found for one local calendar day, with no birth chart: the
// facts the Sky view's reading is written from.
public sealed class SkyDay
{
    // The Moon, its phase, void-of-course stretches, stations and planetary hours, as
    // the Daily view has them (with no transits to a chart).
    public required DaySky Day { get; init; }
    public required IReadOnlyList<SkyAspect> Aspects { get; init; }

    // New and Full Moons, eclipses, stations and changes of sign during the day.
    public required IReadOnlyList<SkyEvent> Events { get; init; }

    public DateOnly Date => Day.Date;
}

// The written reading of one day's sky.
public sealed class SkyReading
{
    public required DateOnly Date { get; init; }
    public required string ZoneId { get; init; }
    public required DayTone Tone { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }

    // Every aspect in effect, best first; the ones the reading used have Shown set.
    public required IReadOnlyList<SkyAspect> Aspects { get; init; }
    public required IReadOnlyList<string> Trace { get; init; }

    // The sky halfway through the day, for the wheel.
    public IReadOnlyList<PlanetPosition> Midday { get; init; } = [];

    public string DateText => Date.ToString("dddd, d MMMM yyyy");
}

// One day of the week strip above the reading.
public sealed record SkyDayTone(DateOnly Date, DayTone Tone)
{
    public string Weekday => Date.ToString("ddd");
    public string DayText => Date.ToString("d MMM");
    public string ColorHex => Tone.ColorHex();
    public string Label => Tone.Label();
}
