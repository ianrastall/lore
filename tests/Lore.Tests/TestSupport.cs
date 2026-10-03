using System.Text.Json;
using Lore.Models;
using Lore.Services;

// The Swiss Ephemeris keeps global state (ChartService serialises its own calls, but
// setting the ephemeris path does not), so run test classes one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lore.Tests;

// Locations and shared, expensive objects for the tests.
internal static class Repo
{
    // The repository root: the nearest folder above the test binaries holding Lore.csproj.
    public static string Root { get; } = FindRoot();

    public static string Data(string file) => Path.Combine(Root, "Data", file);

    public static string Ephemeris =>
        Path.Combine(Root, "Data", "swisseph-2.10.3bfinal", "swisseph-2.10.3bfinal", "ephe");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lore.csproj")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root (Lore.csproj) above " + AppContext.BaseDirectory);
    }

    private static readonly Lazy<ChartService> LazyCharts = new(() => new ChartService(Ephemeris));
    public static ChartService Charts => LazyCharts.Value;

    private static readonly Lazy<List<Celebrity>> LazyFigures = new(() =>
        JsonSerializer.Deserialize<List<Celebrity>>(File.ReadAllText(Data("celebrities.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
    public static IReadOnlyList<Celebrity> Figures => LazyFigures.Value;

    public static Celebrity Figure(string id) => Figures.Single(f => f.Id == id);

    // A chart's position as "Taurus 3°38'", the form Astro-Databank and the report use.
    public static string Position(double longitude) =>
        $"{ZodiacSignExtensions.FromLongitude(longitude).Name()} {ZodiacSignExtensions.FormatDegreeInSign(longitude)}";

    // Angular distance in arc-minutes, folded so 359° and 1° are 2° apart.
    public static double ArcMinutesBetween(double a, double b)
    {
        double d = Math.Abs(((a - b) % 360 + 540) % 360 - 180);
        return d * 60;
    }
}

// A made-up chart (Codex's worked example: 15 June 1990, 08:30, Chicago), used where a
// test needs a fixed person rather than one of the bundled figures.
internal static class Demo
{
    public static Celebrity Person(bool timed = true) => new()
    {
        Id = "demo", Name = "Demo Person", Category = "Test",
        BirthDate = "1990-06-15", BirthTime = "08:30", BirthTimeKnown = timed,
        BirthPlace = "Chicago", Latitude = 41.8781, Longitude = -87.6298,
        UtcOffsetHours = -5, TimeZoneId = "America/Chicago",
    };
}
