using Lore.Models;

namespace Lore.Services;

// "Lore.exe --self-check [report file]": a copy of Lore checks itself and exits, with
// no window. It is how the release script tries the finished, packaged app, not just
// its source: every installed file is there, every corpus loads and is whole, the
// Swiss Ephemeris library answers, and a chart from the bundled library is calculated
// and read from the bundled data.
//
// It reads only what Lore is installed with. The reader's own folder (saved charts,
// settings, inventory answers) is never opened, so it is safe to run on any PC.
public static class SelfCheck
{
    public const string Switch = "--self-check";

    // The path after the switch, if the command line has the switch; null if not.
    // (An empty string: the switch with no report file.)
    public static string? ReportPath(string[] args)
    {
        int at = Array.IndexOf(args, Switch);
        if (at < 0) return null;
        return at + 1 < args.Length ? args[at + 1] : "";
    }

    // Everything wrong with the copy of Lore in `baseDirectory`; empty when it is sound.
    public static IReadOnlyList<string> Run(string baseDirectory, bool published)
    {
        var problems = new List<string>();
        string Data(string file) => Path.Combine(baseDirectory, "Data", file);

        void Try(string what, Action check)
        {
            try { check(); }
            catch (Exception ex) { problems.Add($"{what}: {ex.GetType().Name}: {ex.Message}"); }
        }
        void Need(bool ok, string problem) { if (!ok) problems.Add(problem); }

        foreach (string missing in InstalledFiles.Missing(baseDirectory, published))
            problems.Add($"missing or empty: {missing}");

        // The corpora: each loads, and those that can say so are whole.
        ChartInterpreter? report = null;
        Try("interpretations.json", () =>
        {
            report = new ChartInterpreter(Data("interpretations.json"));
            Need(report.IsAvailable, "interpretations.json: the Report's text is missing or empty");
        });
        DailyInterpreter? daily = null;
        Try("daily.json", () => daily = new DailyInterpreter(Data("daily.json")));
        Try("synastry.json", () => _ = new SynastryInterpreter(Data("synastry.json")));
        Try("davison.json", () => _ = new DavisonInterpreter(Data("davison.json")));
        Try("inventory.json", () => Need(new InventoryService(Data("inventory.json")).IsAvailable,
            "inventory.json: the questionnaire is missing or not whole"));
        Try("mirror.json", () => Need(new MirrorInterpreter(Data("mirror.json"), report).IsAvailable,
            "mirror.json: the chart-and-answers text is missing or empty"));
        Try("legend.json", () => Need(new LegendService(Data("legend.json")).IsAvailable,
            "legend.json: the Legend is missing or not whole"));

        // The library and the places.
        IReadOnlyList<Celebrity> figures = [];
        Try("celebrities.json", () =>
        {
            figures = new CelebrityService().LoadAsync(Data("celebrities.json")).GetAwaiter().GetResult();
            Need(figures.Count > 0, "celebrities.json: the figure library is empty");
            Need(figures.All(UserChartService.IsUsable), "celebrities.json: a figure has birth data that cannot be calculated");
        });
        Try("cities.json", () =>
        {
            var cities = new CityService();
            cities.LoadAsync(Data("cities.json")).GetAwaiter().GetResult();
            Need(cities.Search("London").Count > 0, "cities.json: London is not in the city list");
        });
        Try("hospitals.json", () =>
        {
            var hospitals = new HospitalService();
            hospitals.LoadAsync(Data("hospitals.json")).GetAwaiter().GetResult();
            Need(hospitals.Search("hospital").Count > 0, "hospitals.json: the hospital list is empty");
        });

        // The ephemeris: a chart calculated from the bundled files (not the rougher
        // built-in fallback), and read.
        Try("Swiss Ephemeris", () =>
        {
            var charts = new ChartService(Path.Combine(baseDirectory, "Assets", "Ephemeris"));
            // One figure from each of the two ephemeris ranges, where the library has them.
            var people = new[] { figures.FirstOrDefault(f => string.CompareOrdinal(f.BirthDate, "1800") < 0),
                                 figures.FirstOrDefault(f => string.CompareOrdinal(f.BirthDate, "1800") >= 0) }
                .OfType<Celebrity>().ToList();
            Need(people.Count > 0, "no figure to calculate a chart for");
            foreach (var person in people)
            {
                var chart = charts.Calculate(person);
                Need(chart.Planets.Count >= 10, $"the chart for {person.Name} has only {chart.Planets.Count} bodies");
                Need(chart.EphemerisNote.Length == 0, $"the chart for {person.Name}: {chart.EphemerisNote}");
                if (report is { IsAvailable: true })
                    Need(report.Interpret(chart).Count >= 5, $"the Report for {person.Name} is short of sections");
                if (daily is not null)
                {
                    var zone = NodaTime.DateTimeZone.Utc;
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var reading = daily.Compose(chart, new TransitService(charts).Scan(chart, today, zone), zone);
                    Need(reading.Sections.Count > 0, $"the daily reading for {person.Name} is empty");
                }
            }
        });

        return problems;
    }

    // Runs the check and writes its findings (one to a line, or "OK") to `reportPath`
    // when one is given. The exit code: 0 when sound, 1 when not.
    public static int RunAndReport(string baseDirectory, string reportPath)
    {
        IReadOnlyList<string> problems;
        try
        {
            // Off the calling thread: the app calls this from its window thread, where
            // waiting on the loaders' tasks would wait for ever for that same thread.
            bool published = File.Exists(Path.Combine(baseDirectory, "coreclr.dll"));
            problems = Task.Run(() => Run(baseDirectory, published)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            problems = [$"the self-check itself failed: {ex}"];
        }
        if (reportPath.Length > 0)
        {
            try { File.WriteAllLines(reportPath, problems.Count == 0 ? ["OK"] : problems); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (string problem in problems) Diagnostics.Log($"Self-check: {problem}");
        return problems.Count == 0 ? 0 : 1;
    }
}
