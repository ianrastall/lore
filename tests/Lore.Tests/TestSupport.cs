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
    // The repository root: the nearest folder above the test binaries holding Lore.slnx.
    public static string Root { get; } = FindRoot();

    public static string Data(string file) => Path.Combine(Root, "content", file);

    public static string Ephemeris =>
        Path.Combine(Root, "third_party", "swisseph", "ephe");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lore.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root (Lore.slnx) above " + AppContext.BaseDirectory);
    }

    private static readonly Lazy<ChartService> LazyCharts = new(() => new ChartService(Ephemeris));
    public static ChartService Charts => LazyCharts.Value;

    private static readonly Lazy<List<Celebrity>> LazyFigures = new(() =>
        JsonSerializer.Deserialize<List<Celebrity>>(File.ReadAllText(Data("celebrities.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
    public static IReadOnlyList<Celebrity> Figures => LazyFigures.Value;

    // The figures an expensive test works through: enough to hold every kind of chart,
    // without casting the whole library again for each test. The first ones in the file,
    // so they stay the same as the library grows.
    public static IReadOnlyList<Celebrity> Sample => LazySample.Value;
    private static readonly Lazy<List<Celebrity>> LazySample = new(() => Figures.Take(1400).ToList());

    private static readonly Lazy<Dictionary<string, Celebrity>> LazyById = new(() => Figures.ToDictionary(f => f.Id));
    public static Celebrity Figure(string id) => LazyById.Value[id];

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
