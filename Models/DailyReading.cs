namespace Lore.Models;

// The overall weather of a day, from the balance of easy and hard transits chosen for
// the reading. Shown as a coloured pill, like the chart's dignity verdict.
public enum DayTone { Quiet, Flowing, Mixed, Demanding, Focused }

public static class DayToneExtensions
{
    public static string Label(this DayTone t) => t switch
    {
        DayTone.Flowing   => "Flowing",
        DayTone.Mixed     => "Mixed",
        DayTone.Demanding => "Demanding",
        DayTone.Focused   => "Focused",
        _                 => "Quiet",
    };

    // Hex both WinUI and QuestPDF understand.
    public static string ColorHex(this DayTone t) => t switch
    {
        DayTone.Flowing   => "#3FB84F", // green
        DayTone.Mixed     => "#E0A93B", // amber
        DayTone.Demanding => "#E5534B", // red
        DayTone.Focused   => "#6FA8DC", // blue
        _                 => "#9AA0A6", // neutral grey
    };
}

// One entry in a section of the daily reading: body text, optionally under a title
// line ("☽ Moon trine your natal ♀ Venus") and a smaller timing/house line.
public sealed class DailyItem
{
    public string? Title { get; init; }
    public string? Meta { get; init; }
    public required string Text { get; init; }

    // For x:Bind in the item template (bool converts to Visibility).
    public bool HasTitle => !string.IsNullOrEmpty(Title);
    public bool HasMeta => !string.IsNullOrEmpty(Meta);
}

public sealed class DailySection
{
    public required string Heading { get; init; }
    public required IReadOnlyList<DailyItem> Items { get; init; }
}

// The generated daily horoscope for one chart on one date.
public sealed class DailyReading
{
    public required string Name { get; init; }
    public required DateOnly Date { get; init; }
    public required string ZoneId { get; init; }
    public required DayTone Tone { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }

    // Every transit found for the day, best first — the "why this reading" trail. The
    // ones the reading actually used have Shown set.
    public required IReadOnlyList<TransitEvent> Events { get; init; }
    public required IReadOnlyList<string> Trace { get; init; }

    public string DateText => Date.ToString("dddd, d MMMM yyyy");
}
