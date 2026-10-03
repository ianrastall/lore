using Lore.Models;

namespace Lore.Tests;

// Planet and angle positions, checked against the positions Astro-Databank publishes
// for the same people (to within two arc-minutes, its display precision).
public class ChartServiceTests
{
    [Theory]
    //          id                          Sun                Moon               Ascendant
    [InlineData("leonardo-da-vinci",        "Taurus 3°38'",    "Pisces 3°02'",    "Sagittarius 6°39'")]
    [InlineData("immanuel-kant",            "Taurus 2°08'",    "Aries 14°29'",    "Taurus 9°29'")]
    [InlineData("johann-wolfgang-von-goethe", "Virgo 5°11'",   "Pisces 12°11'",   "Scorpio 23°02'")]
    [InlineData("jules-verne",              "Aquarius 18°46'", "Scorpio 14°35'",  "Gemini 15°40'")]
    [InlineData("james-joyce",              "Aquarius 13°22'", "Leo 2°40'",       "Capricorn 6°31'")]
    [InlineData("werner-heisenberg",        "Sagittarius 12°50'", "Libra 13°45'", "Gemini 21°07'")]
    [InlineData("jorge-luis-borges",        "Virgo 0°54'",     "Aries 14°44'",    "Cancer 12°38'")]
    [InlineData("roger-federer",            "Leo 15°38'",      "Scorpio 20°45'",  "Virgo 11°01'")]
    [InlineData("karl-marx",                "Taurus 13°56'",   "Taurus 11°16'",   "Aquarius 23°03'")]
    [InlineData("sigmund-freud",            "Taurus 16°20'",   "Gemini 14°40'",   "Scorpio 10°11'")]
    public void Positions_match_Astro_Databank(string id, string sun, string moon, string asc)
    {
        var chart = Repo.Charts.Calculate(Repo.Figure(id));
        AssertNear(sun, chart.GetPlanet(Planet.Sun)!.Longitude);
        AssertNear(moon, chart.GetPlanet(Planet.Moon)!.Longitude);
        AssertNear(asc, chart.Ascendant);
    }

    [Fact]
    public void All_thirteen_bodies_and_twelve_houses_are_calculated()
    {
        var chart = Repo.Charts.Calculate(Repo.Figure("elvis-presley"));
        Assert.Equal(13, chart.Planets.Count);
        Assert.Equal(12, chart.Houses.Count);
    }

    [Fact]
    public void Every_bundled_figure_calculates()
    {
        Assert.All(Repo.Figures, f => Assert.Equal(13, Repo.Charts.Calculate(f).Planets.Count));
    }

    private static void AssertNear(string expected, double longitude)
    {
        // "Taurus 3°38'" -> ecliptic longitude
        var parts = expected.Split(' ');
        var sign = Enum.Parse<ZodiacSign>(parts[0]);
        var dm = parts[1].TrimEnd('\'').Split('°');
        double target = (int)sign * 30 + int.Parse(dm[0]) + int.Parse(dm[1]) / 60.0;
        Assert.True(Repo.ArcMinutesBetween(target, longitude) <= 2.0,
            $"expected {expected}, got {Repo.Position(longitude)}");
    }
}
