using Lore.Services;

namespace Lore.Tests;

public class HospitalServiceTests
{
    private static readonly Lazy<HospitalService> Service = new(() =>
    {
        var s = new HospitalService();
        s.LoadAsync(Repo.Data("hospitals.json")).GetAwaiter().GetResult();
        return s;
    });

    [Fact]
    public void One_letter_returns_nothing_and_two_return_results()
    {
        Assert.Empty(Service.Value.Search("s"));
        Assert.NotEmpty(Service.Value.Search("st"));
    }

    [Fact]
    public void Former_names_of_renamed_hospitals_are_searchable()
    {
        var hit = Service.Value.Search("Waterford Regional", max: 20);
        Assert.Contains(hit, h => h.Name == "Waterford Regional Hospital (now University Hospital Waterford)");
    }

    [Fact]
    public void Words_can_match_the_name_and_the_town_between_them()
    {
        var hits = Service.Value.Search("St Mary London", max: 20);
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Name.StartsWith("St Mary", StringComparison.OrdinalIgnoreCase) &&
                                   h.City.Contains("London", StringComparison.OrdinalIgnoreCase));
        Assert.All(hits, h => Assert.All(new[] { "St", "Mary", "London" }, word =>
            Assert.Contains(word, $"{h.Name} {h.City} {h.Country}", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Results_are_capped_and_prefix_matches_come_first()
    {
        var results = Service.Value.Search("Royal", max: 8);
        Assert.InRange(results.Count, 1, 8);
        Assert.StartsWith("Royal", results[0].Name, StringComparison.OrdinalIgnoreCase);
    }
}
