using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// The life-areas check-in: twelve statements, one to a house, rated in a minute and
// answered again as things change. Where the two questionnaires speak of the person and
// so of the planets, this speaks of their circumstances and so of the houses. It is Lore's
// own and has nothing behind it but the plain meaning of the words.
public sealed class LifeAreaService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public const int Areas = 12, ChoiceCount = 5;

    public LifeAreaInstrument Instrument { get; }

    public bool IsAvailable { get; }

    public int Count => Instrument.Areas.Count;

    public LifeAreaService(string lifeAreasJsonPath)
    {
        LifeAreaInstrument? read = null;
        try
        {
            if (File.Exists(lifeAreasJsonPath))
            {
                using var stream = File.OpenRead(lifeAreasJsonPath);
                read = JsonSerializer.Deserialize<LifeAreaInstrument>(stream, JsonOpts);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The life areas could not be read from {lifeAreasJsonPath}: {ex.Message}");
        }

        string? fault = read is null ? "there is nothing in it" : Fault(read);
        if (read is not null && fault is not null)
            Diagnostics.Log($"The life areas in {lifeAreasJsonPath} are not usable: {fault}.");
        IsAvailable = fault is null;
        Instrument = IsAvailable ? read! : new LifeAreaInstrument();
    }

    // What is wrong with an instrument as read, or null if it is whole: every house once,
    // in order, and a line for every use of each.
    internal static string? Fault(LifeAreaInstrument q)
    {
        try
        {
            if (q.Areas is null || q.Choices is null) return "a section is missing";
            if (q.Choices.Count != ChoiceCount || q.Choices.Any(string.IsNullOrWhiteSpace))
                return $"there must be {ChoiceCount} choices";
            if (!q.Areas.Select(a => a?.House ?? 0).SequenceEqual(Enumerable.Range(1, Areas)))
                return $"there must be {Areas} areas, one to a house, in order";
            if (q.Areas.Any(a => string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Statement) || !Whole(a.Low) || !Whole(a.High)))
                return "an area is missing its wording";
            if (q.Forecast is null || new[] { q.Forecast.LowHard, q.Forecast.LowEasy, q.Forecast.HighHard, q.Forecast.HighEasy }.Any(string.IsNullOrWhiteSpace))
                return "the forecast's wording is missing";
            return null;
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentNullException or InvalidOperationException)
        {
            return $"it could not be checked ({ex.Message})";
        }
    }

    private static bool Whole(LifeLines? lines) =>
        lines is not null && !string.IsNullOrWhiteSpace(lines.Report) && !string.IsNullOrWhiteSpace(lines.Hard) &&
        !string.IsNullOrWhiteSpace(lines.Easy) && !string.IsNullOrWhiteSpace(lines.Year);

    // A complete set of answers, by house, 1 to 5. Throws if any is missing.
    public LifeAreasReport Score(IReadOnlyList<int> answers, DateOnly takenOn)
    {
        if (Answers.FirstUnanswered(answers, Count) is { } missing)
            throw new InvalidOperationException($"Statement {missing} has not been answered.");
        return new LifeAreasReport
        {
            TakenOn = takenOn,
            Forecast = Instrument.Forecast,
            Ratings = Instrument.Areas.Select(a => new LifeAreaRating(a, answers[a.House - 1], Instrument.Choices[answers[a.House - 1] - 1])).ToList(),
        };
    }
}
