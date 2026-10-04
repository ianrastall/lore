namespace Lore.Models;

// The Rodden rating: the astrological community's shorthand for where a birth time
// came from, and so how far to trust it. It grades the source, not the precision — a
// birth certificate (AA) may still give a time rounded to the quarter hour.
public static class RoddenRating
{
    // In order from most to least reliable; the order the Add Chart dialog lists them in.
    public static readonly IReadOnlyList<(string Code, string Meaning)> All =
    [
        ("AA", "from a birth certificate or birth record"),
        ("A",  "from the person, their family or a close associate, by memory"),
        ("B",  "from a biography or autobiography"),
        ("C",  "caution — no source given, or a vague or rectified time"),
        ("DD", "conflicting or unverified — two or more sources disagree"),
        ("X",  "no time of birth is known"),
        ("XX", "the date itself is in doubt"),
    ];

    public static bool IsKnown(string? code) => All.Any(r => r.Code == code);

    // "AA — from a birth certificate or birth record"; the bare code if it is not one
    // of Rodden's, and empty if there is none.
    public static string Describe(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var match = All.FirstOrDefault(r => r.Code == code);
        return match.Code is null ? code : $"{match.Code} — {match.Meaning}";
    }
}
