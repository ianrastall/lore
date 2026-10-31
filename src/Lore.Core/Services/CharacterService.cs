using Lore.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lore.Services;

// The character scales: reads the instrument (Data\character.json) and scores a set of
// answers into the three dimensions and their ten scales. Plain arithmetic on the
// published keys; nothing here knows about charts.
public sealed class CharacterService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CharacterInstrument Instrument { get; }

    public int Count => Instrument.Items.Count;

    // True when the instrument was read and is whole (see Fault).
    public bool IsAvailable { get; }

    public const int Statements = 96, Scales = 10, Dimensions = 3, ChoiceCount = 5;

    public CharacterService(string characterJsonPath)
    {
        CharacterInstrument? read = null;
        try
        {
            if (File.Exists(characterJsonPath))
            {
                using var stream = File.OpenRead(characterJsonPath);
                read = JsonSerializer.Deserialize<CharacterInstrument>(stream, JsonOpts);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The character scales could not be read from {characterJsonPath}: {ex.Message}");
        }

        string? fault = read is null ? "there is nothing in it" : Fault(read);
        if (read is not null && fault is not null)
            Diagnostics.Log($"The character scales in {characterJsonPath} are not usable: {fault}.");
        IsAvailable = fault is null;
        Instrument = IsAvailable ? read! : new CharacterInstrument();
    }

    // What is wrong with an instrument as read, or null if it is whole.
    internal static string? Fault(CharacterInstrument q)
    {
        try
        {
            if (q.Instrument is null || q.Choices is null || q.Dimensions is null || q.Scales is null || q.Items is null)
                return "a section is missing";
            if (q.Choices.Count != ChoiceCount || q.Choices.Any(string.IsNullOrWhiteSpace))
                return $"there must be {ChoiceCount} choices";

            if (q.Dimensions.Count != Dimensions || q.Dimensions.Any(d => d is null || string.IsNullOrWhiteSpace(d.Key) || string.IsNullOrWhiteSpace(d.Name)))
                return $"there must be {Dimensions} named dimensions";
            if (q.Dimensions.Select(d => d.Key).Distinct().Count() != Dimensions) return "a dimension is given twice";

            if (q.Scales.Count != Scales || q.Scales.Any(s => s is null || string.IsNullOrWhiteSpace(s.Key) || string.IsNullOrWhiteSpace(s.Name)))
                return $"there must be {Scales} named scales";
            if (q.Scales.Select(s => s.Key).Distinct().Count() != Scales) return "a scale is given twice";
            var dimensions = q.Dimensions.Select(d => d.Key).ToHashSet();
            if (q.Scales.Any(s => s.Dimension is null || !dimensions.Contains(s.Dimension))) return "a scale belongs to no known dimension";
            if (q.Dimensions.Any(d => q.Scales.All(s => s.Dimension != d.Key))) return "a dimension has no scales";

            if (q.Items.Count != Statements || q.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Text)))
                return $"there must be {Statements} statements";
            if (!q.Items.Select(i => i.N).SequenceEqual(Enumerable.Range(1, Statements)))
                return $"the statements must be numbered 1 to {Statements} in order";
            var scales = q.Scales.Select(s => s.Key).ToHashSet();
            if (q.Items.Any(i => i.Scale is null || !scales.Contains(i.Scale))) return "a statement belongs to no known scale";
            if (q.Scales.Any(s => q.Items.All(i => i.Scale != s.Key))) return "a scale has no statements";
            return null;
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentNullException or InvalidOperationException)
        {
            return $"it could not be checked ({ex.Message})";
        }
    }

    // Scores a complete set of answers: `answers[n - 1]` is the answer to statement n, 1
    // to 5. Throws if any is missing.
    public CharacterProfile Score(IReadOnlyList<int> answers, string name, DateOnly takenOn)
    {
        if (Answers.FirstUnanswered(answers, Count) is { } missing)
            throw new InvalidOperationException($"Statement {missing} has not been answered.");

        var scales = Instrument.Scales.Select(scale => new CharacterScore(scale,
            Instrument.Items.Where(i => i.Scale == scale.Key)
                .Average(i => (double)(i.Reversed ? 6 - answers[i.N - 1] : answers[i.N - 1])))).ToList();

        return new CharacterProfile
        {
            Name = name,
            TakenOn = takenOn,
            Dimensions = Instrument.Dimensions.Select(dimension =>
            {
                var own = scales.Where(s => s.Scale.Dimension == dimension.Key).ToList();
                return new CharacterDimensionScore(dimension, own.Average(s => s.Mean), own);
            }).ToList(),
        };
    }

    // The profile as plain text, for saving or pasting into notes.
    public byte[] ToText(CharacterProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{p.Name} — Character");
        sb.AppendLine($"{Instrument.Instrument.Name}, answered on {p.TakenOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine(Caution);
        foreach (var d in p.Dimensions)
        {
            sb.AppendLine();
            sb.AppendLine($"{d.Name.ToUpperInvariant()}: {d.BandLabel} ({d.ScoreText})");
            sb.AppendLine(d.Text);
            foreach (var s in d.Scales)
            {
                sb.AppendLine();
                sb.AppendLine($"  {s.Name}: {s.BandLabel} ({s.ScoreText})");
                sb.AppendLine($"  {s.Text}");
            }
        }
        sb.AppendLine();
        sb.AppendLine(Instrument.Instrument.Norms);
        sb.AppendLine("Questionnaire: " + Instrument.Instrument.Author + ". " + Instrument.Instrument.Licence);
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Generated by Lore on {DateTime.Now:yyyy-MM-dd}."));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // Said wherever a character profile is shown.
    public const string Caution =
        "This is how the person describes themselves today. It is not a measure of worth or of health, and it is not a " +
        "diagnosis. Most people answer these statements in the direction they would like to be true, and the scales are " +
        "marked preliminary by the people who made them. No part of it comes from the birth chart.";
}

// The two things every questionnaire needs to know about a set of answers.
public static class Answers
{
    public static int Given(IReadOnlyList<int> answers, int count) =>
        Enumerable.Range(0, count).Count(i => i < answers.Count && answers[i] is >= 1 and <= 5);

    // The number of the first statement still unanswered, or null if none is.
    public static int? FirstUnanswered(IReadOnlyList<int> answers, int count)
    {
        for (int i = 0; i < count; i++)
            if (i >= answers.Count || answers[i] is < 1 or > 5) return i + 1;
        return null;
    }
}
