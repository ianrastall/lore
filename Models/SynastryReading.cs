namespace Lore.Models;

// The overall balance of easy and hard contacts between two charts, on a five-step scale
// from +2 to -2. Shown as a coloured pill, like the daily reading's day tone. Light is a
// pair with no personal contacts at all, which sits at 0 beside Mixed.
public enum SynastryTone { Light, Harmonious, Mixed, Challenging, Soulmates, Adversaries }

public static class SynastryToneExtensions
{
    public static string Label(this SynastryTone t) => t switch
    {
        SynastryTone.Soulmates   => "Soulmates",
        SynastryTone.Harmonious  => "Harmonious",
        SynastryTone.Mixed       => "Mixed",
        SynastryTone.Challenging => "Challenging",
        SynastryTone.Adversaries => "Adversaries",
        _                        => "Light",
    };

    // +2 Soulmates, +1 Harmonious, 0 Mixed (or Light), -1 Challenging, -2 Adversaries.
    public static int Level(this SynastryTone t) => t switch
    {
        SynastryTone.Soulmates   => 2,
        SynastryTone.Harmonious  => 1,
        SynastryTone.Challenging => -1,
        SynastryTone.Adversaries => -2,
        _                        => 0,
    };

    // "+2", "0", "−1": the level as it is written beside the label.
    public static string LevelText(this SynastryTone t) =>
        t.Level() switch { > 0 and var n => $"+{n}", < 0 and var n => $"−{-n}", _ => "0" };

    // Hex both WinUI and QuestPDF understand.
    public static string ColorHex(this SynastryTone t) => t switch
    {
        SynastryTone.Soulmates   => "#2FD4A7", // bright teal-green
        SynastryTone.Harmonious  => "#3FB84F", // green
        SynastryTone.Mixed       => "#E0A93B", // amber
        SynastryTone.Challenging => "#E5534B", // red
        SynastryTone.Adversaries => "#C2356B", // deep crimson
        _                        => "#9AA0A6", // neutral grey
    };
}

// How two charts weigh up: the weight of their easy contacts, the weight of their hard
// ones, and the tone that balance comes to. Lore's own measure (see SynastryScoring), and
// a description of the symbolism, not a forecast for the two people.
public sealed record Compatibility(SynastryTone Tone, double Flow, double Tension)
{
    public int Level => Tone.Level();
    public double Net => Flow - Tension;                                 // easy less hard
    public double Share => Flow + Tension <= 0 ? 0.5 : Flow / (Flow + Tension);   // the easy share, 0 to 1
}

// One person from a search of everyone against a chart.
public sealed record SynastryMatch(Celebrity Person, Compatibility Compatibility)
{
    // For x:Bind in the list of matches.
    public string Name => Person.Name;
    public string Detail => $"{Person.Category} · {Person.BirthDate}";
    public string LevelText => Compatibility.Tone.LevelText();
    public string ToneLabel => Compatibility.Tone.Label();
    public string ColorHex => Compatibility.Tone.ColorHex();
    public string ShareText => $"{Math.Round(Compatibility.Share * 100):0}% easy";
}

// The generated synastry reading for two charts. Its sections are laid out like the
// daily reading's (a title line, a smaller detail line, body text), so they share the
// same section and item types.
public sealed class SynastryReading
{
    public required string FirstName { get; init; }
    public required string SecondName { get; init; }
    public required SynastryTone Tone { get; init; }
    public required Compatibility Compatibility { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }

    // Every contact found between the two charts, best first — the "why this reading"
    // trail. The ones the reading actually used have Shown set.
    public required IReadOnlyList<SynastryAspect> Aspects { get; init; }
    public required IReadOnlyList<string> Trace { get; init; }

    public string Title => $"{FirstName} & {SecondName}";
}
