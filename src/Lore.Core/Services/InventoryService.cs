using Lore.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lore.Services;

// The personality inventory: reads the instrument (Data\inventory.json) and scores a set
// of answers into a profile of the five traits and their 30 facets. Plain arithmetic on
// the published scoring keys and reference figures; nothing here knows about charts.
public sealed class InventoryService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public InventoryInstrument Instrument { get; }

    public int Count => Instrument.Items.Count;

    // True when the instrument was read and is whole (see Fault). Without that nothing
    // is offered, and Instrument is empty.
    public bool IsAvailable { get; }

    // The questionnaire as published: 120 statements, four to each of 30 facets, six
    // facets to each of five traits, answered on five choices. The reference figures
    // and the reading made from the answers both rest on exactly this shape.
    public const int Statements = 120, Facets = 30, Traits = 5, PerFacet = 4, PerTrait = 6, ChoiceCount = 5;

    public InventoryService(string inventoryJsonPath)
    {
        InventoryInstrument? read = null;
        try
        {
            if (File.Exists(inventoryJsonPath))
            {
                using var stream = File.OpenRead(inventoryJsonPath);
                read = JsonSerializer.Deserialize<InventoryInstrument>(stream, JsonOpts);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The personality inventory could not be read from {inventoryJsonPath}: {ex.Message}");
        }

        // Checked whole before any of it is used. A file that is good JSON and not a
        // good questionnaire is as unusable as one that is neither, and is treated the
        // same: nothing is offered, and nothing else in Lore is affected.
        string? fault = read is null ? "there is nothing in it" : Fault(read);
        if (read is not null && fault is not null)
            Diagnostics.Log($"The personality inventory in {inventoryJsonPath} is not usable: {fault}.");
        IsAvailable = fault is null;
        Instrument = IsAvailable ? read! : new InventoryInstrument();
    }

    // What is wrong with an instrument as read, or null if it is whole. Nothing in a
    // file is taken on trust: any part may be missing, null, repeated or out of range.
    internal static string? Fault(InventoryInstrument q)
    {
        try
        {
            if (q.Instrument is null || q.Choices is null || q.Domains is null || q.Facets is null || q.Items is null)
                return "a section is missing";
            if (q.Choices.Count != ChoiceCount || q.Choices.Any(string.IsNullOrWhiteSpace))
                return $"there must be {ChoiceCount} choices";

            if (q.Domains.Count != Traits || q.Domains.Any(d => d is null || string.IsNullOrWhiteSpace(d.Key) || string.IsNullOrWhiteSpace(d.Name)))
                return $"there must be {Traits} named traits";
            if (q.Domains.Select(d => d.Key).Distinct().Count() != Traits) return "a trait is given twice";
            if (q.Domains.Any(d => !Usable(d.Mean, d.Sd))) return "a trait's reference figures are not usable numbers";

            if (q.Facets.Count != Facets || q.Facets.Any(f => f is null || string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Name)))
                return $"there must be {Facets} named facets";
            if (q.Facets.Select(f => f.Key).Distinct().Count() != Facets) return "a facet is given twice";
            if (q.Facets.Any(f => !Usable(f.Mean, f.Sd))) return "a facet's reference figures are not usable numbers";
            var traits = q.Domains.Select(d => d.Key).ToHashSet();
            if (q.Facets.Any(f => f.Domain is null || !traits.Contains(f.Domain))) return "a facet belongs to no known trait";
            if (q.Domains.Any(d => q.Facets.Count(f => f.Domain == d.Key) != PerTrait))
                return $"each trait must have {PerTrait} facets";

            if (q.Items.Count != Statements || q.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Text)))
                return $"there must be {Statements} statements";
            if (!q.Items.Select(i => i.N).SequenceEqual(Enumerable.Range(1, Statements)))
                return $"the statements must be numbered 1 to {Statements} in order";
            var facets = q.Facets.Select(f => f.Key).ToHashSet();
            if (q.Items.Any(i => i.Facet is null || !facets.Contains(i.Facet))) return "a statement belongs to no known facet";
            if (q.Facets.Any(f => q.Items.Count(i => i.Facet == f.Key) != PerFacet))
                return $"each facet must have {PerFacet} statements";
            return null;
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentNullException or InvalidOperationException)
        {
            return $"it could not be checked ({ex.Message})";
        }
    }

    // A reference mean and spread a score can be set against: real numbers, the mean
    // on the 4-to-20 scale and the spread more than nothing.
    private static bool Usable(double mean, double sd) =>
        double.IsFinite(mean) && double.IsFinite(sd) && mean is >= 4 and <= 20 && sd > 0;

    // How many of a set of answers have been given.
    public int Answered(IReadOnlyList<int> answers) =>
        Enumerable.Range(0, Count).Count(i => i < answers.Count && answers[i] is >= 1 and <= 5);

    // The number of the first statement still unanswered, or null if none is.
    public int? FirstUnanswered(IReadOnlyList<int> answers)
    {
        for (int i = 0; i < Count; i++)
            if (i >= answers.Count || answers[i] is < 1 or > 5) return i + 1;
        return null;
    }

    // Scores a complete set of answers: `answers[n - 1]` is the answer to statement n, 1
    // (the first choice, "very inaccurate") to 5. Throws if any is missing.
    public InventoryProfile Score(IReadOnlyList<int> answers, string name, DateOnly takenOn)
    {
        if (FirstUnanswered(answers) is { } missing)
            throw new InvalidOperationException($"Statement {missing} has not been answered.");

        var facets = Instrument.Facets.Select(facet =>
        {
            int raw = Instrument.Items.Where(i => i.Facet == facet.Key)
                .Sum(i => i.Reversed ? 6 - answers[i.N - 1] : answers[i.N - 1]);
            return new FacetScore(facet, raw, (raw - facet.Mean) / facet.Sd);
        }).ToList();

        return new InventoryProfile
        {
            Name = name,
            TakenOn = takenOn,
            Domains = Instrument.Domains.Select(domain =>
            {
                var own = facets.Where(f => f.Facet.Domain == domain.Key).ToList();
                double score = own.Count == 0 ? 0 : own.Average(f => (double)f.Raw);
                return new DomainScore(domain, score, (score - domain.Mean) / domain.Sd, own);
            }).ToList(),
        };
    }

    // The profile as plain text, for saving or pasting into notes.
    public byte[] ToText(InventoryProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{p.Name} — Personality profile");
        sb.AppendLine($"{Instrument.Instrument.Name}, answered on {p.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine(Caution);
        sb.AppendLine();
        sb.AppendLine(EstimateNote);
        foreach (var d in p.Domains)
        {
            sb.AppendLine();
            sb.AppendLine($"{d.Name.ToUpperInvariant()}: {d.BandLabel} ({d.ScoreText}; {d.PercentileText})");
            sb.AppendLine(d.Text);
            foreach (var f in d.Facets)
            {
                sb.AppendLine();
                sb.AppendLine($"  {f.Name}: {f.BandLabel} ({f.ScoreText})");
                sb.AppendLine($"  {f.Text}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Reference figures: " + Instrument.Instrument.Norms);
        sb.AppendLine("Questionnaire: " + Instrument.Instrument.Author + ". " + Instrument.Instrument.Licence);
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Generated by Lore on {DateTime.Now:yyyy-MM-dd}."));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // Said wherever a profile is shown.
    public const string Caution =
        "This is what the answers say, set beside the answers of a large group of other people. It describes habits of " +
        "feeling and behaving, as the person sees them; it is not a measure of ability, worth or wellbeing, and no part " +
        "of it comes from the birth chart.";

    // Said wherever a score is given as so many "in 100".
    public const string EstimateNote =
        "Each \"in 100\" figure is an estimate, not a count: it is worked out from the reference group's average and " +
        "spread, taking scores to be spread in the usual bell shape. The reference group is one set of people of both " +
        "sexes and all ages together (see the reference figures below); nobody is compared with people of their own age or sex.";
}
