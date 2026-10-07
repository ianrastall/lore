namespace Lore.Models;

// Vertex and Fortune are aspected on the Worksheet only: nothing transits them, and the
// report, the patterns and the wheel's lines leave them alone.
public enum NatalPointKind { Body, Ascendant, Midheaven, Vertex, Fortune }

// A fixed point in the birth chart that a moving (transiting) body can aspect: one of
// the chart's thirteen bodies, or an angle. Angles are not Planet values, so they get
// their own kind rather than being squeezed into that enum.
public readonly record struct NatalPoint(NatalPointKind Kind, Planet Body = Planet.Sun)
{
    public static NatalPoint Of(Planet body) => new(NatalPointKind.Body, body);
    public static readonly NatalPoint Ascendant = new(NatalPointKind.Ascendant);
    public static readonly NatalPoint Midheaven = new(NatalPointKind.Midheaven);
    public static readonly NatalPoint Vertex = new(NatalPointKind.Vertex);
    public static readonly NatalPoint Fortune = new(NatalPointKind.Fortune);

    public bool IsAngle => Kind != NatalPointKind.Body;

    // Display name, and the name used in daily.json keys ("Mars|Tension|Midheaven").
    public string Name => Kind switch
    {
        NatalPointKind.Ascendant => "Ascendant",
        NatalPointKind.Midheaven => "Midheaven",
        NatalPointKind.Vertex => "Vertex",
        NatalPointKind.Fortune => "Part of Fortune",
        _ => Body.Name()
    };

    public string Symbol => Kind switch
    {
        NatalPointKind.Ascendant => "↑",
        NatalPointKind.Midheaven => "MC",
        NatalPointKind.Vertex => "Vx",
        NatalPointKind.Fortune => "⊗",
        _ => Body.Symbol()
    };
}

// How a transit feels, which is what the daily corpus is written against: the five
// aspects collapse to three tones (a trine and a sextile read alike; so do a square
// and an opposition), keeping the bank of bespoke lines to a size that can be reviewed.
public enum TransitTone { Conjunction, Flow, Tension }

// Where a transit stands relative to the day being read.
public enum TransitPhase { Building, Exact, Easing }

public static class TransitExtensions
{
    public static TransitTone Tone(this AspectType a) => a switch
    {
        AspectType.Conjunction => TransitTone.Conjunction,
        AspectType.Sextile or AspectType.Trine => TransitTone.Flow,
        _ => TransitTone.Tension
    };

    // "Moon trine your natal Venus": the aspect as a verb-ish word between two names.
    public static string Verb(this AspectType a) => a switch
    {
        AspectType.Conjunction => "conjunct",
        AspectType.Sextile     => "sextile",
        AspectType.Square      => "square",
        AspectType.Trine       => "trine",
        AspectType.Opposition  => "opposite",
        _ => "aspecting"
    };

    // The Sun to Mars change from day to day and make the day's foreground; Jupiter
    // outward (and the slow points) hold an aspect for weeks and form its background.
    public static bool IsSlowMover(this Planet p) => !p.IsPersonal();
}

// One moving body in aspect to one fixed natal point during the day being read.
public sealed class TransitEvent
{
    public required Planet Mover { get; init; }
    public required NatalPoint Target { get; init; }
    public required AspectType Aspect { get; init; }

    public double MinOrb { get; init; }          // tightest orb reached within the day, degrees
    public DateTime PeakUtc { get; init; }       // when that happens
    public TransitPhase Phase { get; init; }
    public DateTime? ExactUtc { get; init; }     // nearest exact contact (inside or outside the day), if found
    public bool MoverRetrograde { get; init; }

    // Both houses are the birth chart's: the one the mover is passing through, and the
    // one holding the natal point. Null when the birth time (hence the houses) is unknown.
    public int? MoverHouse { get; init; }
    public int? TargetHouse { get; init; }

    public bool IsBackground => Mover.IsSlowMover();
    public TransitTone Tone => Aspect.Tone();

    // Editorial priority assigned by the interpreter — how much the reading should care,
    // not a probability of anything.
    public double Score { get; set; }
    public bool Shown { get; set; }
}

public enum MoonPhase
{
    NewMoon, WaxingCrescent, FirstQuarter, WaxingGibbous,
    FullMoon, WaningGibbous, LastQuarter, WaningCrescent
}

public static class MoonPhaseExtensions
{
    public static string Name(this MoonPhase p) => p switch
    {
        MoonPhase.NewMoon        => "New Moon",
        MoonPhase.WaxingCrescent => "Waxing Crescent",
        MoonPhase.FirstQuarter   => "First Quarter",
        MoonPhase.WaxingGibbous  => "Waxing Gibbous",
        MoonPhase.FullMoon       => "Full Moon",
        MoonPhase.WaningGibbous  => "Waning Gibbous",
        MoonPhase.LastQuarter    => "Last Quarter",
        _                        => "Waning Crescent"
    };
}

// A planet turning retrograde or direct during the day.
public sealed record Station(Planet Planet, bool TurnsRetrograde);

// Everything the transit scan found for one chart on one local calendar day: the facts
// the daily reading is then written from.
public sealed class DaySky
{
    public required DateOnly Date { get; init; }
    public required string ZoneId { get; init; }
    public required DateTime StartUtc { get; init; }   // local midnight …
    public required DateTime EndUtc { get; init; }     // … to the next local midnight

    public required IReadOnlyList<PlanetPosition> Midday { get; init; } // the sky halfway through the day
    public required IReadOnlyList<TransitEvent> Events { get; init; }

    public MoonPhase Phase { get; init; }
    public DateTime? PhaseExactUtc { get; init; }      // set when a New/Quarter/Full Moon falls in the day
    public ZodiacSign MoonSign { get; init; }          // at the start of the day
    public ZodiacSign? MoonEnters { get; init; }       // sign the Moon moves into during the day, if any
    public DateTime? MoonIngressUtc { get; init; }
    public required IReadOnlyList<Station> Stations { get; init; }

    public PlanetPosition? Body(Planet p) => Midday.FirstOrDefault(x => x.Planet == p);
}
