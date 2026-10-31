using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Models;

namespace Lore.Services;

// "Lore.exe --daily-reading <name> <output file>": today's reading for a chart in My
// Charts, written as JSON with no window. This is the contract with Jupiter HUD: Lore owns
// the format ("lore.daily.v1"), and nothing outside Lore loads its assemblies or reads
// its data files. It uses the reader's calculation settings, as the Daily view does.
// Saved charts are read and never written.
public static class DailyReadingExport
{
    public const string Switch = "--daily-reading";
    public const string Kind = "lore.daily.v1";

    public sealed record Request(string Name, string OutputPath)
    {
        // The name and file after the switch, if the command line has it; null if not.
        public static Request? Parse(string[] args)
        {
            int at = Array.IndexOf(args, Switch);
            return at >= 0 && at + 2 < args.Length ? new Request(args[at + 1], args[at + 2]) : null;
        }
    }

    private sealed record Item(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("meta")] string Meta);

    private sealed record Document(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("dateText")] string DateText,
        [property: JsonPropertyName("tone")] string Tone,
        [property: JsonPropertyName("toneLabel")] string ToneLabel,
        [property: JsonPropertyName("toneColorHex")] string ToneColorHex,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("highlights")] IReadOnlyList<Item> Highlights,
        [property: JsonPropertyName("themes")] IReadOnlyList<Item> Themes,
        [property: JsonPropertyName("skyNotes")] IReadOnlyList<Item> SkyNotes);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // The chart called `name` in the saved charts at `chartsPath`, or null.
    public static Celebrity? FindChart(string chartsPath, string name)
    {
        if (!File.Exists(chartsPath)) return null;
        var charts = JsonSerializer.Deserialize<List<Celebrity>>(File.ReadAllText(chartsPath), ReadOptions);
        return charts?.FirstOrDefault(c => c.Name == name);
    }

    // The reading as the JSON the file holds.
    public static string ToJson(DailyReading reading)
    {
        static IReadOnlyList<Item> Items(DailySection? section) =>
            section?.Items.Select(i => new Item(i.Title ?? "", i.Text, i.Meta ?? "")).ToList() ?? [];

        DailySection? Section(string heading) =>
            reading.Sections.FirstOrDefault(s => s.Heading.Contains(heading, StringComparison.OrdinalIgnoreCase));

        var document = new Document(
            Kind, reading.Name,
            reading.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            reading.DateText, reading.Tone.ToString(), reading.Tone.ToString(), reading.Tone.ColorHex(),
            Section("glance")?.Items.FirstOrDefault()?.Text ?? "",
            Items(Section("highlight")), Items(Section("Longer-running")), Items(Section("sky")));
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    // Today's reading for `name`, from the copy of Lore in `baseDirectory` and the
    // reader's own folder. Written beside the file and swapped in, so whoever reads it
    // never meets half a reading.
    public static void Run(string baseDirectory, Request request, string userFolder, DateOnly date, NodaTime.DateTimeZone zone)
    {
        var person = FindChart(Path.Combine(userFolder, "mycharts.json"), request.Name)
            ?? throw new InvalidOperationException($"\"{request.Name}\" is not in My Charts.");

        var charts = new ChartService(Path.Combine(baseDirectory, "Assets", "Ephemeris"))
        {
            Settings = new SettingsService(userFolder).Load(),
        };
        var natal = charts.Calculate(person);
        var sky = new TransitService(charts).Scan(natal, date, zone);
        var reading = new DailyInterpreter(Path.Combine(baseDirectory, "Data", "daily.json")).Compose(natal, sky, zone);

        string output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = $"{output}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, ToJson(reading));
        File.Move(temporary, output, overwrite: true);
    }

    // The command itself. The exit code: 0 when the file was written. What went wrong
    // goes to the log, and to `<output file>.error` for the program that asked.
    public static int RunAndReport(string baseDirectory, Request request)
    {
        string errorPath = request.OutputPath + ".error";
        try
        {
            if (File.Exists(errorPath)) File.Delete(errorPath);
            Run(baseDirectory, request, AppFolder.Path, DateOnly.FromDateTime(DateTime.Now), TransitService.LocalZone());
            return 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Daily reading for another app failed: {ex}");
            try { File.WriteAllText(errorPath, ex.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return 1;
        }
    }
}
