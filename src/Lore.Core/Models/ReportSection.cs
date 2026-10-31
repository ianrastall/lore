namespace Lore.Models;

// One heading + its paragraphs in the generated natal report.
public sealed class ReportSection
{
    public required string Heading { get; init; }
    public required IReadOnlyList<string> Paragraphs { get; init; }
}
