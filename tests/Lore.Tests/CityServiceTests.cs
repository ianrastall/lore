using Lore.Services;

namespace Lore.Tests;

public class CityServiceTests
{
    private static readonly Lazy<CityService> Service = new(() =>
    {
        var s = new CityService();
        s.LoadAsync(Repo.Data("cities.json")).GetAwaiter().GetResult();
        return s;
    });

    [Fact]
    public void An_empty_query_returns_nothing()
    {
        Assert.Empty(Service.Value.Search(""));
        Assert.Empty(Service.Value.Search("   "));
        Assert.Empty(Service.Value.Search(null));
    }

    [Fact]
    public void The_larger_city_of_the_same_name_comes_first()
    {
        var hits = Service.Value.Search("Paris");
        Assert.Equal("France", hits[0].Country);
        Assert.InRange(hits.Count, 1, 8);
    }

    [Fact]
    public void Words_can_match_the_name_and_the_country_between_them()
    {
        var hits = Service.Value.Search("Paris France");
        Assert.NotEmpty(hits);
        Assert.Equal("Paris", hits[0].Name);
        Assert.Equal("France", hits[0].Country);
    }

    [Fact]
    public void Words_can_match_the_name_and_the_region_between_them()
    {
        var hits = Service.Value.Search("Springfield Illinois");
        Assert.NotEmpty(hits);
        Assert.Equal("Springfield", hits[0].Name);
        Assert.Equal("Illinois", hits[0].Admin);
        Assert.All(hits, c => Assert.All(new[] { "Springfield", "Illinois" }, word =>
            Assert.Contains(word, $"{c.Name} {c.Admin} {c.Country}", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void A_name_of_several_words_still_ranks_its_own_city_first()
    {
        var hits = Service.Value.Search("New York");
        Assert.StartsWith("New York", hits[0].Name, StringComparison.OrdinalIgnoreCase);
    }
}
