namespace Lore.Models;

public enum WriterAnchor { SkyEvents, Ascendant, Midheaven, Patterns, SkyAndAngle }

public sealed record WriterCharacterQuery(DateOnly Start, int Days, City Place,
    WriterAnchor Anchor = WriterAnchor.SkyEvents, Planet Planet = Planet.Venus);

public sealed record WriterGroupQuery(int Size = 2, double DistanceKm = 100,
    string Search = "", string Category = "", int FromYear = 1200, int ToYear = 2400,
    int Keep = 10_000, int MinimumSitelinks = 0, int? PairLevel = null, TrioTone? TrioTone = null,
    DateOnly? AsOf = null)
{
    public DateOnly DeathCutoff => (AsOf ?? DateOnly.FromDateTime(DateTime.Today)).AddYears(-50);
}

public sealed record WriterBirth(Celebrity Person, DateTime Utc);

public sealed record WriterCandidate(WriterBirth Birth, string Anchor, string Evidence, string Key = "", Planet? Body = null)
{
    public string Title => Anchor;
    public string Detail => $"{Birth.Person.BirthDate} {Birth.Person.BirthTime} · {Birth.Person.BirthPlace}";
}

public sealed record WriterGroup(IReadOnlyList<Celebrity> Members, double Share,
    double DistanceKm, double GrandTrineWeight, string Tone, DateOnly DeathCutoff)
{
    public string Title => string.Join(" · ", Members.Select(p => p.Name));
    public string Detail => $"{Members.Min(p => Services.BirthTimeResolver.GregorianDate(p)):yyyy}–" +
        $"{Members.Max(p => Services.BirthTimeResolver.GregorianDate(p)):yyyy} · " +
        $"{DistanceKm:0.#} km maximum birthplace distance · {Members[0].Category} · {Tone}";
}

public sealed record WriterGroupSearch(IReadOnlyList<WriterGroup> Results, int People,
    long Pairs, long Trios, long Matches, int Skipped)
{
    public string Summary => $"{People:N0} figures; {Pairs:N0} eligible pairs, {Trios:N0} eligible trios. " +
        $"Showing {Results.Count:N0} of {Matches:N0} results, using the Synastry / Trine order." +
        (Skipped > 0 ? $" {Skipped:N0} figures could not be used." : "");
}

// The complete reading and its inputs travel together, including sub-minute instants.
// A saved fictional character is never inserted in the public library or My Charts.
public sealed record WriterBrief
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "Untitled";
    public string Kind { get; init; } = "Character";
    public string Anchor { get; init; } = "";
    public string CorpusVersion { get; init; } = "";
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public ChartSettings Settings { get; init; } = ChartSettings.Default;
    public IReadOnlyList<WriterBirth> Births { get; init; } = [];
    public IReadOnlyList<DailySection> Sections { get; init; } = [];
    public string Detail => $"{Kind} · {CreatedUtc:yyyy-MM-dd} · {Anchor}";
}
