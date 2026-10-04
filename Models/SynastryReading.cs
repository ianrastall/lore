namespace Lore.Models;

// The overall balance of easy and hard contacts between two charts. Shown as a coloured
// pill, like the daily reading's day tone.
public enum SynastryTone { Light, Harmonious, Mixed, Challenging }

public static class SynastryToneExtensions
{
    public static string Label(this SynastryTone t) => t switch
    {
        SynastryTone.Harmonious  => "Harmonious",
        SynastryTone.Mixed       => "Mixed",
        SynastryTone.Challenging => "Challenging",
        _                        => "Light",
    };

    // Hex both WinUI and QuestPDF understand.
    public static string ColorHex(this SynastryTone t) => t switch
    {
        SynastryTone.Harmonious  => "#3FB84F", // green
        SynastryTone.Mixed       => "#E0A93B", // amber
        SynastryTone.Challenging => "#E5534B", // red
        _                        => "#9AA0A6", // neutral grey
    };
}

// The generated synastry reading for two charts. Its sections are laid out like the
// daily reading's (a title line, a smaller detail line, body text), so they share the
// same section and item types.
public sealed class SynastryReading
{
    public required string FirstName { get; init; }
    public required string SecondName { get; init; }
    public required SynastryTone Tone { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }

    // Every contact found between the two charts, best first — the "why this reading"
    // trail. The ones the reading actually used have Shown set.
    public required IReadOnlyList<SynastryAspect> Aspects { get; init; }
    public required IReadOnlyList<string> Trace { get; init; }

    public string Title => $"{FirstName} & {SecondName}";
}
