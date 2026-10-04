using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The comparison of two charts. Most tests use hand-built charts with round-number
// positions, so the expected aspects and orbs can be checked by eye.
public class SynastryServiceTests
{
    // A chart with the given bodies, Ascendant at `asc`, and twelve equal 30° houses
    // starting there (so the Midheaven here is simply the 10th cusp).
    private static NatalChart Chart(string name, bool timed, double asc, params (Planet planet, double lon)[] bodies) => new()
    {
        Celebrity = new Celebrity { Id = name, Name = name, BirthDate = "2000-01-01", BirthTimeKnown = timed },
        Planets = bodies.Select(b => new PlanetPosition { Planet = b.planet, Longitude = b.lon }).ToList(),
        Houses = Enumerable.Range(1, 12)
            .Select(h => new HouseCusp { House = h, Longitude = (asc + (h - 1) * 30) % 360 }).ToList(),
        Aspects = [],
        Ascendant = asc,
        Midheaven = (asc + 270) % 360,
    };

    private static SynastryAspect? Find(Synastry s, string first, string second) =>
        s.Aspects.SingleOrDefault(a => a.First.Name == first && a.Second.Name == second);

    [Fact]
    public void Finds_each_aspect_with_its_orb()
    {
        var a = Chart("A", true, 200, (Planet.Sun, 10), (Planet.Venus, 100));
        var b = Chart("B", true, 205, (Planet.Moon, 133), (Planet.Mars, 13), (Planet.Saturn, 282));
        var s = SynastryService.Compare(a, b);

        var sunMoon = Find(s, "Sun", "Moon")!;
        Assert.Equal(AspectType.Trine, sunMoon.Type);
        Assert.Equal(3, sunMoon.Orb, 6);

        Assert.Equal(AspectType.Conjunction, Find(s, "Sun", "Mars")!.Type);
        Assert.Equal(AspectType.Square, Find(s, "Venus", "Mars")!.Type);
        Assert.Equal(AspectType.Opposition, Find(s, "Venus", "Saturn")!.Type);
        Assert.Equal(8, Find(s, "Venus", "Saturn")!.Orb + Find(s, "Sun", "Mars")!.Orb + Find(s, "Venus", "Mars")!.Orb, 6);
    }

    [Fact]
    public void Measures_across_zero_Aries()
    {
        var a = Chart("A", true, 100, (Planet.Venus, 358));
        var b = Chart("B", true, 100, (Planet.Mars, 2));
        var aspect = Find(SynastryService.Compare(a, b), "Venus", "Mars")!;
        Assert.Equal(AspectType.Conjunction, aspect.Type);
        Assert.Equal(4, aspect.Orb, 6);
    }

    [Fact]
    public void Orbs_are_tighter_than_natal_and_tightest_for_a_sextile()
    {
        var a = Chart("A", true, 100, (Planet.Sun, 0));
        // 6° from a trine (in), 6.5° from a square (out), 4.5° from a sextile (out)
        var b = Chart("B", true, 100, (Planet.Jupiter, 126), (Planet.Saturn, 96.5), (Planet.Mars, 64.5));
        var s = SynastryService.Compare(a, b);

        Assert.NotNull(Find(s, "Sun", "Jupiter"));
        Assert.Null(Find(s, "Sun", "Saturn"));
        Assert.Null(Find(s, "Sun", "Mars"));
        Assert.All(s.Aspects, x => Assert.InRange(x.Orb, 0, SynastryService.Orb(x.Type)));
    }

    [Fact]
    public void Angles_take_part_and_the_list_runs_closest_first()
    {
        var a = Chart("A", true, 40, (Planet.Sun, 10));
        var b = Chart("B", true, 12, (Planet.Venus, 41));
        var s = SynastryService.Compare(a, b);

        Assert.Equal(AspectType.Conjunction, Find(s, "Sun", "Ascendant")!.Type);
        Assert.Equal(AspectType.Conjunction, Find(s, "Ascendant", "Venus")!.Type);
        Assert.Equal(s.Aspects.OrderBy(x => x.Orb).Select(x => x.Orb), s.Aspects.Select(x => x.Orb));
    }

    [Fact]
    public void Each_persons_planets_are_placed_in_the_others_houses()
    {
        var a = Chart("A", true, 350, (Planet.Sun, 5), (Planet.Moon, 345));
        var b = Chart("B", true, 0, (Planet.Mars, 95));
        var s = SynastryService.Compare(a, b);

        Assert.Equal([new HouseOverlay(Planet.Sun, 1), new HouseOverlay(Planet.Moon, 12)], s.FirstInSecondHouses);
        Assert.Equal([new HouseOverlay(Planet.Mars, 4)], s.SecondInFirstHouses); // 95° is 105° past A's Ascendant
    }

    [Fact]
    public void Without_a_birth_time_the_angles_Moon_and_houses_are_left_out()
    {
        var a = Chart("A", false, 40, (Planet.Sun, 10), (Planet.Moon, 70));
        var b = Chart("B", true, 12, (Planet.Venus, 70), (Planet.Mars, 10));
        var s = SynastryService.Compare(a, b);

        Assert.DoesNotContain(s.Aspects, x => x.First.IsAngle || x.First.Name == "Moon");
        Assert.Contains(s.Aspects, x => x.Second.IsAngle);          // B's angles still count
        Assert.Empty(s.SecondInFirstHouses);                        // A has no houses
        Assert.Equal([new HouseOverlay(Planet.Sun, 12)], s.FirstInSecondHouses); // and A's Moon is not placed
    }

    [Fact]
    public void Swapping_the_two_people_mirrors_the_result()
    {
        var a = Repo.Charts.Calculate(Demo.Person());
        var b = Repo.Charts.Calculate(Repo.Figures[0]);
        var ab = SynastryService.Compare(a, b);
        var ba = SynastryService.Compare(b, a);

        Assert.NotEmpty(ab.Aspects);
        Assert.Equal(
            ab.Aspects.Select(x => (x.First, x.Second, x.Type, Math.Round(x.Orb, 9))).OrderBy(x => x.ToString()),
            ba.Aspects.Select(x => (First: x.Second, Second: x.First, x.Type, Math.Round(x.Orb, 9))).OrderBy(x => x.ToString()));
        Assert.Equal(ab.FirstInSecondHouses, ba.SecondInFirstHouses);
    }

    [Fact]
    public void A_chart_compared_with_itself_has_every_point_conjunct_itself()
    {
        var chart = Repo.Charts.Calculate(Demo.Person());
        var s = SynastryService.Compare(chart, chart);

        foreach (var p in chart.Planets)
        {
            var self = Find(s, p.PlanetName, p.PlanetName)!;
            Assert.Equal(AspectType.Conjunction, self.Type);
            Assert.Equal(0, self.Orb, 9);
        }
        Assert.Equal(0, Find(s, "Ascendant", "Ascendant")!.Orb, 9);
    }
}
