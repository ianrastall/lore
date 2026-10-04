using System.Text.Json;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// Lore against swetest, the Swiss Ephemeris's own command-line program, for moments and
// places chosen to cover the awkward cases: both ephemeris files and the seam between
// them, both hemispheres, the equator, the date line, and both polar circles.
//
// The two share one engine, so this does not prove the astronomy (ChartServiceTests does
// that, against Astro-Databank). It proves Lore asks the engine the right question: the
// right instant, body numbers, flags, house codes, and coordinates the right way round.
// The reference file is written by scripts\build-swetest-reference.py.
public class SwetestReferenceTests
{
    // swetest prints seven decimal places; allow for its rounding and nothing more.
    private const double Tolerance = 1e-6; // degrees: under four thousandths of an arc-second

    private sealed record HouseSet(bool Substituted, double Ascendant, double Midheaven, double[] Cusps);

    private sealed record Case(
        string Name, string Why, string UtcDate, string UtcTime, double Latitude, double Longitude,
        Dictionary<string, double[]> Bodies, Dictionary<string, HouseSet> Houses)
    {
        public override string ToString() => Name;
    }

    private sealed record Reference(List<Case> Cases);

    private static readonly Lazy<List<Case>> All = new(() =>
        JsonSerializer.Deserialize<Reference>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", "swetest-reference.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Cases);

    public static IEnumerable<object[]> CaseNames() => All.Value.Select(c => new object[] { c.Name });

    private static Case Find(string name) => All.Value.Single(c => c.Name == name);

    // The moment is given in Universal Time with the offset fixed at zero, so no
    // time-zone lookup comes between the reference and the calculation.
    private static NatalChart Calculate(Case c, ChartSettings settings) =>
        new ChartService(Repo.Ephemeris) { Settings = settings }.Calculate(new Celebrity
        {
            Id = c.Name, Name = c.Name, BirthDate = c.UtcDate, BirthTime = c.UtcTime, BirthTimeKnown = true,
            Latitude = c.Latitude, Longitude = c.Longitude, UtcOffsetHours = 0, UtcOffsetFixed = true,
        });

    [Fact]
    public void The_reference_covers_every_case()
    {
        Assert.True(All.Value.Count >= 14);
        Assert.All(All.Value, c => Assert.Equal(14, c.Bodies.Count));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Bodies_match_swetest(string name)
    {
        var c = Find(name);
        var mean = Calculate(c, ChartSettings.Default);
        var real = Calculate(c, new ChartSettings(Node: NodeType.True));

        foreach (var planet in Enum.GetValues<Planet>())
        {
            string key = planet == Planet.NorthNode ? "MeanNode" : planet.ToString();
            AssertBody(c, key, mean.GetPlanet(planet)!);
        }
        AssertBody(c, "TrueNode", real.GetPlanet(Planet.NorthNode)!);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Houses_and_angles_match_swetest(string name)
    {
        var c = Find(name);
        foreach (var system in Enum.GetValues<HouseSystem>())
        {
            var expected = c.Houses[system.ToString()];
            var chart = Calculate(c, new ChartSettings(system));
            string where = $"{c.Name}, {system.Name()}";

            AssertAngle(expected.Ascendant, chart.Ascendant, $"{where}: Ascendant");
            AssertAngle(expected.Midheaven, chart.Midheaven, $"{where}: Midheaven");
            for (int house = 1; house <= 12; house++)
                AssertAngle(expected.Cusps[house - 1], chart.GetHouse(house).Longitude, $"{where}: cusp {house}");

            // Where swetest had to fall back to Porphyry, Lore must say so; elsewhere
            // the chart must carry the name of the system that was asked for.
            Assert.True(expected.Substituted == chart.HouseSystemLabel.StartsWith("Porphyry"),
                $"{where}: labelled \"{chart.HouseSystemLabel}\"");
            if (!expected.Substituted)
                Assert.Equal($"{system.Name()} houses", chart.HouseSystemLabel);
        }
    }

    [Fact]
    public void Placidus_and_Koch_are_replaced_inside_both_polar_circles_and_nowhere_else()
    {
        foreach (var c in All.Value)
        {
            bool polar = Math.Abs(c.Latitude) > 66.6;
            Assert.True(polar == c.Houses["Placidus"].Substituted, c.Name);
            Assert.True(polar == c.Houses["Koch"].Substituted, c.Name);
            Assert.False(c.Houses["WholeSign"].Substituted, c.Name);
            Assert.False(c.Houses["Equal"].Substituted, c.Name);
        }
    }

    private static void AssertBody(Case c, string key, PlanetPosition actual)
    {
        double[] expected = c.Bodies[key]; // longitude, latitude, speed, declination
        AssertAngle(expected[0], actual.Longitude, $"{c.Name}: {key} longitude");
        AssertClose(expected[1], actual.Latitude, $"{c.Name}: {key} latitude");
        AssertClose(expected[2], actual.SpeedLongitude, $"{c.Name}: {key} speed");
        AssertClose(expected[3], actual.Declination, $"{c.Name}: {key} declination");
    }

    private static void AssertClose(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what}: swetest {expected:F7}, Lore {actual:F7}");

    // As AssertClose, but 359.9999999° and 0° are neighbours.
    private static void AssertAngle(double expected, double actual, string what) =>
        Assert.True(Repo.ArcMinutesBetween(expected, actual) / 60 <= Tolerance,
            $"{what}: swetest {expected:F7}, Lore {actual:F7}");
}
