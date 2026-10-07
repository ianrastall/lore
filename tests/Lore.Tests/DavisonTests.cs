using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The Davison chart: one real chart for two people, cast for the midpoint of their
// births in time and in place.
public class DavisonTests
{
    private static readonly SynastryInterpreter Interpreter = new(Repo.Data("synastry.json"));

    private static NatalChart Chart(string id) => Repo.Charts.Calculate(Repo.Figure(id));

    private static NatalChart Born(string date, string time, double latitude, double longitude, bool timed = true) =>
        Repo.Charts.Calculate(new Celebrity
        {
            Id = $"{date}{longitude}", Name = $"Born {date}", BirthDate = date, BirthTime = time, BirthTimeKnown = timed,
            BirthPlace = "Somewhere", Latitude = latitude, Longitude = longitude, UtcOffsetHours = 0, UtcOffsetFixed = true,
        });

    [Fact]
    public void It_is_cast_for_the_moment_and_the_place_halfway_between()
    {
        var a = Born("1990-01-01", "06:00", 40, 10);
        var b = Born("1994-01-01", "18:00", 60, 50);
        var d = Repo.Charts.Davison(a, b);

        Assert.Equal(new DateTime(1992, 1, 2, 0, 0, 0), d.CalculatedForUtc); // 1,461½ days apart, so 730¾ on
        Assert.Equal(50, d.Celebrity.Latitude, 9);
        Assert.Equal(30, d.Celebrity.Longitude, 9);
        Assert.True(d.Timed);

        // It is an ordinary chart: the one anybody born then and there would have.
        var born = Born("1992-01-02", "00:00", 50, 30);
        Assert.Equal(born.Ascendant, d.Ascendant, 9);
        Assert.Equal(born.Planets.Select(p => p.Longitude), d.Planets.Select(p => p.Longitude));

        // And it is the same whichever of the two is named first.
        var other = Repo.Charts.Davison(b, a);
        Assert.Equal(d.CalculatedForUtc, other.CalculatedForUtc);
        Assert.Equal(d.Ascendant, other.Ascendant, 9);
    }

    [Theory]
    [InlineData(170, -170, 180)]   // either side of the date line: midway is the date line, not Greenwich
    [InlineData(-170, 170, 180)]
    [InlineData(-120, -60, -90)]
    [InlineData(-10, 30, 10)]
    public void The_longitude_is_midway_round_the_shorter_side(double first, double second, double expected)
    {
        var d = Repo.Charts.Davison(Born("1990-01-01", "06:00", 0, first), Born("1990-01-01", "06:00", 0, second));
        double off = Math.Abs(((d.Celebrity.Longitude - expected) % 360 + 540) % 360 - 180);
        Assert.True(off < 1e-9, $"{d.Celebrity.Longitude}");
        Assert.InRange(d.Celebrity.Longitude, -180, 180);
    }

    [Fact]
    public void A_person_with_themselves_gives_their_own_chart()
    {
        var einstein = Chart("albert-einstein");
        var d = Repo.Charts.Davison(einstein, einstein);
        Assert.Equal(einstein.CalculatedForUtc, d.CalculatedForUtc);
        Assert.Equal(einstein.Ascendant, d.Ascendant, 9);
        Assert.Equal(einstein.Midheaven, d.Midheaven, 9);
    }

    [Fact]
    public void The_reading_states_the_chart_when_it_is_given_one()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        Assert.DoesNotContain(Interpreter.Compose(SynastryService.Compare(a, b)).Sections, s => s.Heading == "The Davison chart");

        var davison = Repo.Charts.Davison(a, b);
        var section = Interpreter.Compose(SynastryService.Compare(a, b, davison)).Sections[^1];
        Assert.Equal("The Davison chart", section.Heading);
        Assert.Contains(" UT  ·  ", section.Items[0].Meta);
        Assert.Contains("halfway between the two births", section.Items[0].Text);

        // Both angles, then all thirteen bodies, each with its house.
        var lines = section.Items[1].Text.Split('\n');
        Assert.Equal(2 + davison.Planets.Count, lines.Length);
        Assert.StartsWith("Ascendant ", lines[0]);
        Assert.All(lines.Skip(2), l => Assert.EndsWith(" house", l));
        Assert.Equal(davison.HouseSystemLabel, section.Items[1].Meta);
    }

    [Fact]
    public void With_a_birth_time_missing_the_angles_houses_and_Moon_are_left_out()
    {
        var a = Chart("albert-einstein");
        var b = Born("1935-01-08", "12:00", 34.26, -88.7, timed: false);
        var davison = Repo.Charts.Davison(a, b);
        Assert.False(davison.Timed);

        var items = Interpreter.Compose(SynastryService.Compare(a, b, davison)).Sections[^1].Items;
        Assert.Contains("a birth time is missing", items[1].Meta);
        var lines = items[1].Text.Split('\n');
        Assert.Equal(davison.Planets.Count - 1, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("Moon") || l.Contains("Ascendant") || l.Contains(" house"));
    }
}
