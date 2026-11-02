using System.Globalization;
using System.Text;
using Lore.Models;

namespace Lore.Services;

// Writers discovers charts and casts; it uses the same readings as the other views.
// There is deliberately no writing-advice corpus, invented role or prompt layer.
public sealed class WriterInterpreter(ChartService charts, ChartInterpreter natal, SynastryInterpreter synastry)
{
    public WriterBrief Character(WriterCandidate candidate, ChartSettings settings, string name = "Untitled character")
    {
        var chart = charts.With(settings).CalculateAt(candidate.Birth.Person, candidate.Birth.Utc);
        var sections = new List<DailySection>
        {
            new() { Heading = "Birth anchor", Items = [new DailyItem { Title = candidate.Anchor, Text = candidate.Evidence }] },
        };
        sections.AddRange(natal.Interpret(chart).Select(s => new DailySection
        { Heading = s.Heading, Items = s.Paragraphs.Select(p => new DailyItem { Text = p }).ToArray() }));
        sections.Add(BirthDetails([candidate.Birth], settings, chart.EphemerisNote));
        return new WriterBrief { Title = string.IsNullOrWhiteSpace(name) ? "Untitled character" : name.Trim(),
            Anchor = candidate.Anchor, Settings = settings, CorpusVersion = ContentVersion,
            Births = [candidate.Birth], Sections = sections };
    }

    public WriterBrief Group(WriterGroup group, ChartSettings settings)
    {
        if (group.Members.Count is not (2 or 3) || group.Members.Select(p => p.Id).Distinct().Count() != group.Members.Count ||
            group.Members.Select(p => p.Category).Distinct().Count() != 1 ||
            group.Members.Any(p => !p.HasBirthCoordinates || string.IsNullOrWhiteSpace(p.Category) || !WriterEligibility.DiedBy(p, group.DeathCutoff)))
            throw new ArgumentException("A cast needs two or three distinct figures in the same field, all deceased for at least fifty years.");
        for (int a = 0; a < group.Members.Count; a++) for (int b = a + 1; b < group.Members.Count; b++)
            if (!WriterEligibility.WithinAge(BirthTimeResolver.GregorianDate(group.Members[a]), BirthTimeResolver.GregorianDate(group.Members[b])) ||
                WriterEligibility.Distance(group.Members[a], group.Members[b]) > group.DistanceKm + 1e-8)
                throw new ArgumentException("This cast no longer meets its birthplace or ten-year age constraints.");
        var pinned = charts.With(settings);
        var cast = group.Members.Select(pinned.Calculate).ToArray();
        var births = cast.Select(c => new WriterBirth(c.Celebrity, c.CalculatedForUtc)).ToArray();
        var sections = (cast.Length == 2
            ? synastry.Compose(SynastryService.Compare(cast[0], cast[1], pinned.Davison(cast[0], cast[1]))).Sections
            : synastry.ComposeTrio(TrioService.Compare(cast[0], cast[1], cast[2])).Sections).ToList();
        var eligibility = group.Members.Select(p => new DailyItem { Title = p.Name,
            Text = $"{p.Category} · born {BirthTimeResolver.GregorianDate(p):yyyy-MM-dd} (Gregorian) · died {p.DeathDate}." }).ToList();
        eligibility.Add(new DailyItem { Text = $"Every member is in the same library category. The oldest and youngest were born within ten calendar years; " +
            $"the greatest birthplace distance is {group.DistanceKm:0.###} km. Every member died on or before {group.DeathCutoff:yyyy-MM-dd}. " +
            "Birthplace proximity does not establish that they met." });
        sections.Add(new DailySection { Heading = "Why this group qualifies", Items = eligibility });
        sections.Add(BirthDetails(births, settings, string.Join(" ", cast.Select(c => c.EphemerisNote).Where(s => s.Length > 0).Distinct())));
        return new WriterBrief { Title = group.Title, Kind = cast.Length == 3 ? "Trio" : "Pair",
            Anchor = $"{group.Members[0].Category} · {group.DistanceKm:0.#} km maximum · ten-year age limit · deaths by {group.DeathCutoff:yyyy-MM-dd}",
            CorpusVersion = ContentVersion, Settings = settings, Births = births, Sections = sections };
    }

    public static string ToText(WriterBrief brief)
    {
        var text = new StringBuilder().AppendLine(brief.Title).AppendLine(brief.Kind).AppendLine(brief.Anchor)
            .AppendLine($"Corpus {brief.CorpusVersion}").AppendLine();
        foreach (var section in brief.Sections)
        {
            text.AppendLine(section.Heading);
            foreach (var item in section.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Title)) text.AppendLine(item.Title);
                if (!string.IsNullOrWhiteSpace(item.Meta)) text.AppendLine(item.Meta);
                text.AppendLine(item.Text).AppendLine();
            }
        }
        return text.ToString();
    }
    private static string ContentVersion => typeof(WriterInterpreter).Assembly.GetName().Version!.ToString(3);
    private static DailySection BirthDetails(IReadOnlyList<WriterBirth> births, ChartSettings settings, string warning) =>
        new() { Heading = "Birth details and calculation", Items = births.Select(b => new DailyItem
        {
            Title = b.Person.Name,
            Text = $"{b.Person.BirthDate} {b.Person.BirthTime} at {b.Person.BirthPlace}; " +
                $"latitude {b.Person.Latitude.ToString("0.######", CultureInfo.InvariantCulture)}, longitude {b.Person.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}. " +
                $"Exact instant {b.Utc.ToString("O", CultureInfo.InvariantCulture)}. {settings.Houses.Name()}; {settings.Node.Name()}; {settings.Lilith.Name()}. " + warning,
        }).ToArray() };
}
