namespace Lore.Models;

// How three people's charts sit together, from Lore's own measure (see TrioService). A
// description of the symbolism, not a forecast for the three people. Golden is a trio
// with no pair in difficulty, more ease than not, and a strong grand trine running
// through all three charts.
public enum TrioTone { Golden, Harmonious, Mixed, Strained }

public static class TrioToneExtensions
{
    public static string Label(this TrioTone t) => t switch
    {
        TrioTone.Golden     => "Golden",
        TrioTone.Harmonious => "Harmonious",
        TrioTone.Strained   => "Strained",
        _                   => "Mixed",
    };

    // Hex both WinUI and QuestPDF understand.
    public static string ColorHex(this TrioTone t) => t switch
    {
        TrioTone.Golden     => "#E6B422", // gold
        TrioTone.Harmonious => "#3FB84F", // green
        TrioTone.Strained   => "#E5534B", // red
        _                   => "#E0A93B", // amber
    };
}

// One corner of a grand trine: a point in one person's chart. Person is 0, 1 or 2, the
// order the charts were given in.
public sealed record TrioPoint(int Person, NatalPoint Point, double Longitude)
{
    public ZodiacSign Sign => ZodiacSignExtensions.FromLongitude(Longitude);
}

// Three points in mutual trine, one from each person's chart. Weight says how much it
// counts: more for closer trines and more personal points.
public sealed record TrioGrandTrine(IReadOnlyList<TrioPoint> Corners, double WidestOrb, double Weight)
{
    // The element all three corners are in; null if a trine runs out of sign.
    public Element? Element
    {
        get
        {
            var elements = Corners.Select(c => c.Sign.GetElement()).Distinct().ToList();
            return elements.Count == 1 ? elements[0] : null;
        }
    }
}

// The figures other than a grand trine that a trio can make, each with one point from
// each person: all three at one place; two opposed with the third square to both; two in
// sextile with the third quincunx both.
public enum TrioFigureKind { Meeting, TSquare, Yod }

public static class TrioFigureKindExtensions
{
    public static string Label(this TrioFigureKind k) => k switch
    {
        TrioFigureKind.Meeting => "Meeting point",
        TrioFigureKind.TSquare => "T-square",
        _ => "Yod",
    };
}

// One such figure. Focus is the person at its apex, the one the other two press on; null
// for a meeting point, which has none.
public sealed record TrioFigure(TrioFigureKind Kind, IReadOnlyList<TrioPoint> Corners, int? Focus, double WidestOrb, double Weight);

// Two of the three people, compared as any two charts are. AsLived is that same balance
// with their answers taken into account; null if neither of the two has any.
public sealed record TrioPair(int First, int Second, Synastry Contacts, Compatibility Compatibility)
{
    public Compatibility? AsLived { get; init; }
}

// Three charts set together: each pair, and the grand trines running through all three.
public sealed class Trio
{
    public required IReadOnlyList<NatalChart> Charts { get; init; }
    public required IReadOnlyList<TrioPair> Pairs { get; init; }
    public required IReadOnlyList<TrioGrandTrine> GrandTrines { get; init; }  // heaviest first
    public required TrioTone Tone { get; init; }

    // The other figures through all three; empty if they were not looked for.
    public IReadOnlyList<TrioFigure> Figures { get; init; } = [];

    // Each person's answers, in order, if they have any; and the trio's tone with those
    // taken into account (null if nobody has). Tone stays the charts' own.
    public IReadOnlyList<MirrorReading?> Answers { get; init; } = [];
    public TrioTone? LivedTone { get; init; }
    public TrioTone RankedTone => LivedTone ?? Tone;

    private double ShareOf(int person) => Pairs.Where(p => p.First == person || p.Second == person)
        .Average(p => (p.AsLived ?? p.Compatibility).Share);

    // Who the other two both get on with best, and least; null when there is little in it.
    private const double MarkedSpread = 0.05;
    private bool Marked => Charts.Count == 3 && Pairs.Count == 3 &&
        Enumerable.Range(0, 3).Max(ShareOf) - Enumerable.Range(0, 3).Min(ShareOf) >= MarkedSpread;
    public int? Hub => Marked ? Enumerable.Range(0, 3).MaxBy(ShareOf) : null;
    public int? Outsider => Marked ? Enumerable.Range(0, 3).MinBy(ShareOf) : null;

    public double GoldenWeight => GrandTrines.Count > 0 ? GrandTrines[0].Weight : 0;   // the strongest grand trine
    public double Share => Pairs.Average(p => p.Compatibility.Share);          // the easy share, 0 to 1
    public TrioPair Weakest => Pairs.MinBy(p => p.Compatibility.Share)!;
}

// One person from the search for a third: a trio made with the two already chosen.
public sealed record TrioMatch(Celebrity Person, TrioTone Tone, double GoldenWeight, double Share, int GrandTrines)
{
    // For x:Bind in the list of matches.
    public string Name => Person.Name;
    public string Detail => $"{Person.Category} · {Person.BirthDate}";
    public string ToneLabel => Tone.Label();
    public string ColorHex => Tone.ColorHex();
    public string ShareText => $"{Math.Round(Share * 100):0}% easy";
    public string GrandTrineText => GrandTrines == 1 ? "1 grand trine" : $"{GrandTrines} grand trines";
}

// Two of a person's connections, tried together as a trio with them: one row of the
// finder for tri-synastries.
public sealed record TrioFind(Celebrity Second, Celebrity Third, TrioTone Tone, double GoldenWeight, double Share, int GrandTrines)
{
    // For x:Bind in the list.
    public string Name => $"{Second.Name} and {Third.Name}";
    public string Detail => $"{Second.Category} · {Third.Category}";
    public string ToneLabel => Tone.Label();
    public string ColorHex => Tone.ColorHex();
    public string ShareText => $"{Math.Round(Share * 100):0}% easy";
    public string GrandTrineText => GrandTrines == 1 ? "1 grand trine" : $"{GrandTrines} grand trines";
}

// The generated reading for three charts, laid out like the other readings.
public sealed class TrioReading
{
    public required IReadOnlyList<string> Names { get; init; }
    public required Trio Trio { get; init; }
    public required IReadOnlyList<DailySection> Sections { get; init; }

    public string Title => $"{Names[0]}, {Names[1]} and {Names[2]}";
}
