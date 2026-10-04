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

    [Fact]
    public void Positions_are_cut_to_the_minute_not_rounded_up()
    {
        // 29°59'40" of Taurus is still 29°59', never a 30th degree.
        Assert.Equal("29°59'", ZodiacSignExtensions.FormatDegreeInSign(59 + 59.0 / 60 + 40.0 / 3600));
        Assert.Equal("0°00'", ZodiacSignExtensions.FormatDegreeInSign(60));
        Assert.Equal("12°30'", ZodiacSignExtensions.FormatDegreeInSign(12.5));
    }

    [Fact]
    public void The_mean_node_is_never_marked_retrograde()
    {
        var chart = Repo.Charts.Calculate(Repo.Figure("elvis-presley"));
        var node = chart.GetPlanet(Planet.NorthNode)!;
        Assert.True(node.SpeedLongitude < 0);
        Assert.False(node.IsRetrograde);
    }

    [Fact]
    public void A_polar_birth_says_Placidus_was_replaced()
    {
        // Tromsø (69°39' N) is inside the Arctic Circle, where Placidus is undefined.
        var tromso = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTime = "12:00", BirthTimeKnown = true,
            Latitude = 69.65, Longitude = 18.96, TimeZoneId = "Europe/Oslo",
        };
        Assert.StartsWith("Porphyry houses", Repo.Charts.Calculate(tromso).HouseSystemLabel);
        Assert.Equal("Placidus houses", Repo.Charts.Calculate(Repo.Figure("elvis-presley")).HouseSystemLabel);
    }

    [Fact]
    public void A_chart_with_no_birth_time_gets_no_dignity_score()
    {
        var untimed = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTimeKnown = false,
            Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        };
        Assert.Null(Lore.Services.DignityService.ComputeIfTimed(Repo.Charts.Calculate(untimed)));
        Assert.NotNull(Lore.Services.DignityService.ComputeIfTimed(Repo.Charts.Calculate(Repo.Figure("elvis-presley"))));
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
