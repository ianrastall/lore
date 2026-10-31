namespace Lore.Models;

// What would and would not change if the birth time were off by a stated margin:
// the chart recalculated at every minute of the window, and compared.
public sealed class TimeSensitivity
{
    public required int Minutes { get; init; }

    // "11:15 to 11:45, clock time at the birthplace"
    public required string Window { get; init; }

    // One line that says how much depends on the exact minute.
    public required string Summary { get; init; }

    // What is the same at every minute of the window.
    public required IReadOnlyList<string> Holds { get; init; }

    // What differs, with the clock time at which each thing changes.
    public required IReadOnlyList<string> Changes { get; init; }

    public bool HasChanges => Changes.Count > 0;

    // True for a chart with no birth time, where the window is the whole day.
    public bool WholeDay { get; init; }

    // For a chart with no birth time: every sign the Moon passes through that day
    // (one, or two if it changes sign). Empty for a timed chart.
    public IReadOnlyList<ZodiacSign> MoonSigns { get; init; } = [];

    // For a chart with no birth time: every body that changed sign during the day, with
    // the signs it was in, in order. A body not listed kept its sign all day. This is
    // the one account of which signs are not known, shared by every reading that speaks
    // of a body in its sign. Empty for a timed chart.
    public IReadOnlyDictionary<Planet, IReadOnlyList<ZodiacSign>> SignChanges { get; init; } =
        new Dictionary<Planet, IReadOnlyList<ZodiacSign>>();

    // The signs a body may be in when its sign cannot be given for want of a birth time:
    // those the day's scan found, or, where no scan is to hand (`day` null), none named
    // for the Moon, which is the body that changes sign every two or three days. Null
    // when the sign is known: the chart is timed, or the body kept its sign all day.
    public static IReadOnlyList<ZodiacSign>? SignsInDoubt(NatalChart chart, TimeSensitivity? day, Planet body)
    {
        if (chart.Timed) return null;
        if (day is { WholeDay: true }) return day.SignChanges.GetValueOrDefault(body);
        return body == Planet.Moon ? [] : null;
    }
}
