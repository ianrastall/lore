using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The traditional dignity score: its house table and where the verdict bands fall.
public class DignityServiceTests
{
    // Equal 30° houses from 0° Aries, with the Sun set down in a chosen house. It is
    // kept in a fire sign each time so that only the house part of its score changes.
    private static PlanetDignity SunIn(int house) => DignityService.Compute(new NatalChart
    {
        Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTime = "12:00", BirthTimeKnown = true },
        Planets = [new PlanetPosition { Planet = Planet.Sun, Longitude = 125 }],   // Leo
        Houses = Enumerable.Range(1, 12)
            .Select(i => new HouseCusp { House = i, Longitude = (125 - 15 - 30 * (house - 1) + 30 * (i - 1) + 720) % 360 }).ToList(),
        Aspects = [],
        Ascendant = (125 - 15 - 30 * (house - 1) + 720) % 360,
    }).Planets.Single();

    [Theory]
    [InlineData(1, 5)] [InlineData(2, 3)] [InlineData(3, 1)] [InlineData(4, 4)]
    [InlineData(5, 3)] [InlineData(6, -2)] [InlineData(7, 4)] [InlineData(8, -2)]
    [InlineData(9, 2)] [InlineData(10, 5)] [InlineData(11, 4)] [InlineData(12, -5)]
    public void Each_house_scores_as_in_Lillys_table(int house, int points)
    {
        // The house is the Sun's only accidental dignity: it has no motion or solar score.
        Assert.Equal(points, SunIn(house).Accidental);
    }

    [Fact]
    public void The_third_house_is_named_in_the_trail()
    {
        Assert.Contains("3rd house +1", SunIn(3).Notes);
    }

    [Fact]
    public void The_bands_keep_their_proportions_across_the_library()
    {
        var verdicts = Repo.Figures.Select(f => DignityService.Compute(Repo.Charts.Calculate(f)).Verdict).ToList();
        double Share(ChartVerdict v) => verdicts.Count(x => x == v) / (double)verdicts.Count;

        // About a quarter Extraordinary and a sixth Alarming. Adding figures will move
        // these a little; if one drifts out of range, redraw the bands in DignityService.
        Assert.InRange(Share(ChartVerdict.Extraordinary), 0.20, 0.30);
        Assert.InRange(Share(ChartVerdict.Alarming), 0.12, 0.22);
    }
}
