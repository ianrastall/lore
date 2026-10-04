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
}
