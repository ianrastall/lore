using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The whole of each search can be read and queried, not a short list from the top of it.
public class SynastryResultsTests
{
    private const int People = 400;

    private static async Task Settled(SynastryViewModel vm)
    {
        await Task.Delay(50);
        for (int i = 0; vm.IsBusy || vm.IsSearching || vm.IsFindingThird; i++)
        {
            Assert.True(i < 6000, "The synastry view did not settle.");
            await Task.Delay(10);
        }
    }

    private static async Task<SynastryViewModel> Searched()
    {
        var vm = new SynastryViewModel(Repo.Charts, new SynastryInterpreter(Repo.Data("synastry.json")));
        vm.SetPeople(Repo.Figures.Take(People).ToList());
        vm.Chart = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        await Settled(vm);
        return vm;
    }

    [Fact]
    public async Task Everyone_compared_can_be_seen_in_the_order_of_the_search()
    {
        var vm = await Searched();
        Assert.Equal(vm.Matches.Count, vm.ShownMatches.Count);
        Assert.True(vm.ShownMatches.Count >= People - 1);
        Assert.Equal(vm.Matches.Select(m => m.Person.Id), vm.ShownMatches.Select(m => m.Person.Id));
        Assert.Equal($"Showing {vm.Matches.Count:n0} of {vm.Matches.Count:n0}", vm.ResultsCount);
        Assert.False(vm.FiltersSet);
    }

    [Fact]
    public async Task A_level_shows_all_of_that_level_and_nobody_else()
    {
        var vm = await Searched();
        var harmonious = vm.LevelChoices.Single(c => c.Text.StartsWith("Harmonious"));
        vm.SelectedLevel = harmonious;

        var expected = vm.Matches.Where(m => m.Compatibility.Level == harmonious.Level).Select(m => m.Person.Id);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, vm.ShownMatches.Select(m => m.Person.Id));
        Assert.True(vm.FiltersSet);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(vm.Matches.Count, vm.ShownMatches.Count);
        Assert.False(vm.FiltersSet);
    }

    [Theory]
    [InlineData("Marilyn")]     // a name
    [InlineData("Liverpool")]   // a birthplace
    [InlineData("guitarist")]   // the biography
    [InlineData("1926")]        // a birth year
    public async Task The_search_reaches_names_places_biographies_and_years(string text)
    {
        var vm = await Searched();
        vm.ResultSearch = text;
        Assert.NotEmpty(vm.ShownMatches);
        Assert.True(vm.ShownMatches.Count < vm.Matches.Count);
        Assert.All(vm.ShownMatches, m => Assert.True(
            m.Person.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || m.Person.BirthPlace.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            m.Person.Bio.Contains(text, StringComparison.OrdinalIgnoreCase) || m.Person.BirthDate.StartsWith(text), m.Name));
    }

    [Fact]
    public async Task Filters_narrow_together()
    {
        var vm = await Searched();
        vm.SelectedScope = "Actor";
        vm.ContemporariesOnly = true;
        Assert.NotEmpty(vm.ShownMatches);
        Assert.All(vm.ShownMatches, m => Assert.True(m.Person.Category == "Actor" && m.Contemporaries, m.Name));
        Assert.Equal(vm.Matches.Count(m => m.Person.Category == "Actor" && m.Contemporaries), vm.ShownMatches.Count);
    }

    [Fact]
    public async Task The_list_sorts_every_way_it_offers()
    {
        var vm = await Searched();
        vm.SelectedSort = SynastryViewModel.HardestFirst;
        var levels = vm.ShownMatches.Select(m => m.Compatibility.Level).ToList();
        Assert.Equal(levels.Order(), levels);
        Assert.Equal(vm.Matches.Count, levels.Count);

        vm.SelectedSort = SynastryViewModel.BestKnown;
        var fame = vm.ShownMatches.Select(m => m.Person.Sitelinks ?? int.MaxValue).ToList();
        Assert.Equal(fame.OrderDescending(), fame);

        vm.SelectedSort = SynastryViewModel.LeastKnown;
        fame = vm.ShownMatches.Select(m => m.Person.Sitelinks ?? int.MaxValue).ToList();
        Assert.Equal(fame.Order(), fame);

        vm.SelectedSort = SynastryViewModel.ByName;
        var names = vm.ShownMatches.Select(m => m.Name).ToList();
        Assert.Equal(names.Order(StringComparer.CurrentCultureIgnoreCase), names);

        vm.SelectedSort = SynastryViewModel.OldestFirst;
        var born = vm.ShownMatches.Select(m => m.Person.BirthDate).ToList();
        Assert.Equal(born.Order(StringComparer.Ordinal), born);

        vm.SelectedSort = SynastryViewModel.YoungestFirst;
        born = vm.ShownMatches.Select(m => m.Person.BirthDate).ToList();
        Assert.Equal(born.OrderDescending(StringComparer.Ordinal), born);

        vm.SelectedSort = null;   // a box emptied while its list is replaced is no choice
        Assert.Equal(SynastryViewModel.YoungestFirst, vm.ResultSort);
    }

    [Fact]
    public async Task Everyone_tried_as_the_third_can_be_seen_and_queried()
    {
        var vm = await Searched();
        vm.Partner = Repo.Figure("marilyn-monroe");
        await Settled(vm);
        vm.ShowTrine = true;
        await Settled(vm);

        Assert.True(vm.ThirdsShown && vm.ResultsShown);
        Assert.Equal(vm.ThirdMatches.Count, vm.ShownThirds.Count);
        Assert.True(vm.ShownThirds.Count >= People - 2);
        Assert.Equal(vm.ThirdsHeading, vm.ResultsHeading);

        var mixed = vm.LevelChoices.Single(c => c.Text == "Mixed");
        vm.SelectedLevel = mixed;
        Assert.NotEmpty(vm.ShownThirds);
        Assert.All(vm.ShownThirds, m => Assert.Equal(TrioTone.Mixed, m.Tone));
    }

    [Fact]
    public async Task The_scale_changes_with_the_view_and_a_level_does_not_carry_across()
    {
        var vm = await Searched();
        vm.SelectedLevel = vm.LevelChoices.Single(c => c.Text.StartsWith("Soulmates"));
        vm.ShowTrine = true;
        Assert.Null(vm.ResultLevel);
        Assert.Contains(vm.LevelChoices, c => c.Text == "Golden");
        Assert.Equal("Any level", ((LevelChoice)vm.SelectedLevel!).Text);
        vm.ShowTrine = false;
        await Settled(vm);
        Assert.Contains(vm.LevelChoices, c => c.Text.StartsWith("Soulmates"));
    }

    [Fact]
    public async Task The_finder_pairs_only_the_people_the_query_allows_and_shows_every_pair()
    {
        var vm = await Searched();
        vm.SelectedScope = "Actor";
        vm.ShowTrine = true;
        await Settled(vm);

        int cast = Math.Min(SynastryViewModel.ConnectionsTried, vm.Matches.Count(m => m.Person.Category == "Actor"));
        Assert.Equal(cast * (cast - 1) / 2, vm.ShownTrios.Count);
        Assert.All(vm.ShownTrios, t => Assert.True(t.Second.Category == "Actor" && t.Third.Category == "Actor", t.Name));

        vm.ResultSearch = "American";
        vm.ContemporariesOnly = true;
        await Settled(vm);
        Assert.NotEmpty(vm.ShownTrios);
        Assert.All(vm.ShownTrios, t => Assert.True(
            t.Second.Bio.Contains("American") || t.Second.Name.Contains("American") || t.Second.BirthPlace.Contains("American"), t.Name));
    }

    [Fact]
    public async Task Starting_a_trio_from_a_name_moves_on_to_looking_for_a_third()
    {
        var vm = await Searched();
        var person = vm.ShownMatches[3].Person;
        vm.AddAsThird(person);
        await Settled(vm);

        Assert.True(vm.ShowTrine);
        Assert.Equal(person.Id, vm.Partner?.Id);
        Assert.True(vm.ThirdsShown);
        Assert.NotEmpty(vm.ShownThirds);
    }

    [Fact]
    public async Task The_query_stays_when_someone_is_chosen_and_let_go()
    {
        var vm = await Searched();
        vm.ResultSearch = "Marilyn";
        int shown = vm.ShownMatches.Count;

        vm.Partner = vm.ShownMatches[0].Person;
        await Settled(vm);
        Assert.False(vm.ResultsShown);

        vm.ClearPartnerCommand.Execute(null);
        await Settled(vm);
        Assert.True(vm.ResultsShown);
        Assert.Equal("Marilyn", vm.ResultSearch);
        Assert.Equal(shown, vm.ShownMatches.Count);
    }
}
