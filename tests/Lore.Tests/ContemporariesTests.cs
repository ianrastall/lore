using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// Who could have known whom: the overlap of lifetimes, where it is said, and how the
// searches prefer it without disturbing the astrological order.
public class ContemporariesTests
{
    private static readonly SynastryInterpreter Interpreter = new(Repo.Data("synastry.json"));

    private static Celebrity Person(string born, string? died = null, string category = "Actor", bool julian = false) =>
        new() { Id = born, Name = born, Category = category, BirthDate = born, DeathDate = died, JulianCalendar = julian };

    private static NatalChart Chart(string id) => Repo.Charts.Calculate(Repo.Figure(id));

    private static string TextOf(IEnumerable<DailySection> sections) =>
        string.Join("\n", sections.SelectMany(s => s.Items).Select(i => $"{i.Title}|{i.Meta}|{i.Text}"));

    // ── The test ──────────────────────────────────────────────────────────────

    [Fact]
    public void Lifetimes_overlap_whichever_is_named_first_and_a_shared_day_counts()
    {
        var earlier = Person("1900-01-01", "1950-06-15");
        Assert.True(Lifetimes.Overlap(earlier, Person("1950-06-15", "2000-01-01")));
        Assert.True(Lifetimes.Overlap(Person("1950-06-15", "2000-01-01"), earlier));
        Assert.False(Lifetimes.Overlap(earlier, Person("1950-06-16", "2000-01-01")));
        Assert.False(Lifetimes.Overlap(Person("1950-06-16", "2000-01-01"), earlier));
    }

    [Fact]
    public void One_lifetime_inside_another_overlaps_and_a_brief_overlap_counts()
    {
        Assert.True(Lifetimes.Overlap(Person("1900-01-01", "1990-01-01"), Person("1940-01-01", "1941-01-01")));
        Assert.True(Lifetimes.Overlap(Person("1900-01-01", "1990-01-01"), Person("1989-12-31", "2060-01-01")));
    }

    [Fact]
    public void The_living_have_no_end_and_still_have_a_beginning()
    {
        Assert.True(Lifetimes.Overlap(Person("1950-01-01"), Person("2000-01-01")));
        Assert.True(Lifetimes.Overlap(Person("1950-01-01"), Person("1900-01-01", "1950-01-01")));
        Assert.False(Lifetimes.Overlap(Person("1950-01-02"), Person("1900-01-01", "1950-01-01")));
    }

    [Fact]
    public void My_Charts_people_count_as_living_whatever_a_file_says()
    {
        var mine = Person("1985-01-01", "1990-01-01", UserChartService.MyChartsCategory);
        Assert.True(Lifetimes.Overlap(mine, Person("2010-01-01")));
        Assert.True(Lifetimes.Overlap(mine, Person("1950-01-01", "1986-01-01")));

        // Counting as living does not move their birth.
        Assert.False(Lifetimes.Overlap(mine, Person("1935-01-08", "1977-08-16")));
    }

    [Fact]
    public void An_Old_Style_birth_is_converted_before_it_is_compared()
    {
        // 1 January 1700 O.S. is 11 January 1700 by the Gregorian calendar deaths are stored in.
        var oldStyle = Person("1700-01-01", "1760-01-01", julian: true);
        Assert.False(Lifetimes.Overlap(oldStyle, Person("1650-01-01", "1700-01-10")));
        Assert.True(Lifetimes.Overlap(oldStyle, Person("1650-01-01", "1700-01-11")));
    }

    [Fact]
    public void A_date_that_cannot_be_read_says_nothing_about_a_lifetime()
    {
        Assert.False(Lifetimes.Overlap(Person("sometime"), Person("1950-01-01")));
        Assert.True(Lifetimes.Overlap(Person("1950-01-01", "unknown"), Person("2000-01-01")));
    }

    [Fact]
    public void Everyone_in_the_trio_overlaps_or_the_trio_is_not_contemporaries()
    {
        var (einstein, marilyn, elvis) = (Chart("albert-einstein"), Chart("marilyn-monroe"), Chart("elvis-presley"));
        Assert.True(TrioService.Compare(einstein, marilyn, elvis).Contemporaries);

        // Einstein died before Saliba was born, and each overlaps Paul McCartney.
        var apart = TrioService.Compare(einstein, Chart("william-saliba"), Chart("paul-mccartney"));
        Assert.False(apart.Contemporaries);
        Assert.Equal([false, true, true], apart.Pairs.Select(p => p.Contemporaries));
    }

    // ── Where it is said ──────────────────────────────────────────────────────

    [Fact]
    public void The_list_rows_say_so()
    {
        var (einstein, elvis) = (Repo.Figure("albert-einstein"), Repo.Figure("elvis-presley"));
        var level = new Compatibility(SynastryTone.Mixed, 1, 1);

        Assert.EndsWith(" · contemporaries", new SynastryMatch(einstein, level, true).Detail);
        Assert.DoesNotContain("contemporaries", new SynastryMatch(einstein, level, false).Detail);
        Assert.EndsWith(" · contemporaries", new TrioMatch(einstein, TrioTone.Mixed, 0, 0.5, 0, true).Detail);
        Assert.DoesNotContain("contemporaries", new TrioMatch(einstein, TrioTone.Mixed, 0, 0.5, 0, false).Detail);
        Assert.EndsWith(" · contemporaries", new TrioFind(einstein, elvis, TrioTone.Mixed, 0, 0.5, 0, true).Detail);
        Assert.DoesNotContain("contemporaries", new TrioFind(einstein, elvis, TrioTone.Mixed, 0, 0.5, 0, false).Detail);
    }

    [Fact]
    public void The_reading_of_two_says_so_only_when_their_lifetimes_overlap()
    {
        string Glance(string first, string second) => TextOf(Interpreter.Compose(SynastryService.Compare(Chart(first), Chart(second))).Sections.Take(1));

        Assert.Contains("The lifetimes of Albert and Marilyn overlap", Glance("albert-einstein", "marilyn-monroe"));
        Assert.DoesNotContain("lifetimes", Glance("albert-einstein", "william-saliba"));
    }

    [Fact]
    public void The_reading_of_three_says_so_at_a_glance_or_pair_by_pair()
    {
        var all = Interpreter.ComposeTrio(TrioService.Compare(Chart("albert-einstein"), Chart("marilyn-monroe"), Chart("elvis-presley")));
        Assert.Contains("The lifetimes of Albert, Marilyn and Elvis all overlap", TextOf(all.Sections.Take(1)));
        Assert.DoesNotContain("contemporaries", TextOf(all.Sections.Where(s => s.Heading == "The three pairs")));

        var some = Interpreter.ComposeTrio(TrioService.Compare(Chart("albert-einstein"), Chart("william-saliba"), Chart("paul-mccartney")));
        Assert.DoesNotContain("lifetimes", TextOf(some.Sections.Take(1)));
        var pairs = some.Sections.Single(s => s.Heading == "The three pairs").Items;
        Assert.DoesNotContain("contemporaries", pairs.Single(p => p.Title == "Albert and William").Meta);
        Assert.Contains("contemporaries", pairs.Single(p => p.Title == "Albert and Paul").Meta);
        Assert.Contains("contemporaries", pairs.Single(p => p.Title == "William and Paul").Meta);
    }

    [Fact]
    public void A_reading_goes_out_in_the_exports()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(Chart("albert-einstein"), Chart("marilyn-monroe"), Chart("elvis-presley")));
        string text = System.Text.Encoding.UTF8.GetString(SynastryExportService.TrioToText(reading));
        Assert.Contains("all overlap", text);
    }

    // ── The preference ────────────────────────────────────────────────────────

    // Contemporaries ahead within each step, and the other order of each kind kept.
    private static void AssertAheadWithin<T, TStep>(IEnumerable<T> ranked, Func<T, TStep> step, Func<T, bool> contemporaries)
    {
        foreach (var within in ranked.GroupBy(step))
        {
            var flags = within.Select(contemporaries).ToList();
            Assert.Equal(flags.OrderByDescending(x => x), flags);
        }
    }

    [Fact]
    public void The_search_of_everyone_keeps_the_levels_and_puts_contemporaries_ahead_within_each()
    {
        var chart = Chart("albert-einstein");
        var ranked = SynastryScoring.Rank(Repo.Charts, chart, Repo.Sample);

        Assert.Contains(ranked, m => m.Contemporaries);
        Assert.Contains(ranked, m => !m.Contemporaries);
        Assert.Equal(ranked.OrderByDescending(m => m.Compatibility.Level).Select(m => m.Compatibility.Level),
                     ranked.Select(m => m.Compatibility.Level));
        AssertAheadWithin(ranked, m => m.Compatibility.Level, m => m.Contemporaries);
        Assert.All(ranked, m => Assert.Equal(Lifetimes.Overlap(chart.Celebrity, m.Person), m.Contemporaries));

        // Inside one kind of person in one level, the easier share still comes first.
        foreach (var group in ranked.GroupBy(m => (m.Compatibility.Level, m.Contemporaries)))
        {
            var shares = group.Select(m => m.Compatibility.Share).ToList();
            Assert.Equal(shares.OrderByDescending(x => x), shares);
        }
    }

    [Fact]
    public void The_search_for_a_third_keeps_the_tones_and_puts_contemporaries_ahead_within_each()
    {
        var (a, b) = (Chart("albert-einstein"), Chart("marilyn-monroe"));
        var matches = TrioService.Rank(Repo.Charts, a, b, Repo.Figures.Take(300));

        Assert.Contains(matches, m => m.Contemporaries);
        Assert.Contains(matches, m => !m.Contemporaries);
        Assert.Equal(matches.OrderBy(m => m.Tone).Select(m => m.Tone), matches.Select(m => m.Tone));
        AssertAheadWithin(matches, m => m.Tone, m => m.Contemporaries);
        Assert.All(matches, m => Assert.Equal(Lifetimes.Overlap(a.Celebrity, m.Person) && Lifetimes.Overlap(b.Celebrity, m.Person), m.Contemporaries));
    }

    [Fact]
    public void Two_who_never_overlapped_can_have_no_contemporary_third()
    {
        var (a, b) = (Chart("albert-einstein"), Chart("william-saliba"));
        Assert.DoesNotContain(TrioService.Rank(Repo.Charts, a, b, Repo.Figures.Take(100)), m => m.Contemporaries);
    }

    [Fact]
    public void The_finder_keeps_the_tones_and_puts_contemporaries_ahead_within_each()
    {
        var chart = Chart("albert-einstein");
        var found = TrioService.FindTrios(Repo.Charts, chart, Repo.Figures.Where(f => f.Id != chart.Celebrity.Id).Take(40).ToList());

        Assert.Contains(found, f => f.Contemporaries);
        Assert.Contains(found, f => !f.Contemporaries);
        Assert.Equal(found.OrderBy(f => f.Tone).Select(f => f.Tone), found.Select(f => f.Tone));
        AssertAheadWithin(found, f => f.Tone, f => f.Contemporaries);
        Assert.All(found, f => Assert.Equal(
            Lifetimes.Overlap(chart.Celebrity, f.Second) && Lifetimes.Overlap(chart.Celebrity, f.Third) && Lifetimes.Overlap(f.Second, f.Third),
            f.Contemporaries));
    }

    private static async Task Settled(SynastryViewModel vm)
    {
        await Task.Delay(50);
        for (int i = 0; vm.IsBusy || vm.IsSearching || vm.IsFindingThird; i++)
        {
            Assert.True(i < 6000, "The synastry view did not settle.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task The_preference_reaches_the_cast_the_finder_takes_before_it_pairs_anyone()
    {
        const int size = SynastryViewModel.ConnectionsTried;
        var vm = new SynastryViewModel(Repo.Charts, Interpreter);
        vm.SetPeople(Repo.Figures.Take(size + 60).ToList());   // the cut falls inside the list
        vm.Chart = Chart("albert-einstein");
        await Settled(vm);
        vm.ShowTrine = true;
        await Settled(vm);

        // The finder pairs those at the head of the search, and only those.
        var cast = vm.Matches.Take(size).ToList();
        var castIds = cast.Select(m => m.Person.Id).ToHashSet();
        Assert.Equal(size * (size - 1) / 2, vm.Trios.Count);
        Assert.All(vm.Trios, t => Assert.True(castIds.Contains(t.Second.Id) && castIds.Contains(t.Third.Id), t.Name));

        // Where the cut falls inside a level, the contemporaries of that level are the ones kept.
        int cutLevel = cast[^1].Compatibility.Level;
        var leftOut = vm.Matches.Skip(size).Where(m => m.Compatibility.Level == cutLevel).ToList();
        Assert.NotEmpty(leftOut);
        Assert.True(!cast.Any(m => m.Compatibility.Level == cutLevel && !m.Contemporaries) || !leftOut.Any(m => m.Contemporaries));

        // That is more of them than the charts' order alone would have kept.
        int byChartsAlone = vm.Matches.OrderByDescending(m => m.Compatibility.Level)
            .ThenByDescending(m => m.Compatibility.Share).Take(size).Count(m => m.Contemporaries);
        Assert.True(cast.Count(m => m.Contemporaries) > byChartsAlone);
    }

    [Fact]
    public async Task The_worst_matches_put_contemporaries_ahead_within_a_level_too()
    {
        var vm = new SynastryViewModel(Repo.Charts, Interpreter);
        vm.SetPeople(Repo.Figures.Take(300).ToList());
        vm.Chart = Chart("albert-einstein");
        await Settled(vm);

        var worst = vm.WorstMatches;
        Assert.Equal(12, worst.Count);
        Assert.Empty(worst.Select(m => m.Person.Id).Intersect(vm.BestMatches.Select(m => m.Person.Id)));
        Assert.Equal(worst.OrderBy(m => m.Compatibility.Level).Select(m => m.Compatibility.Level), worst.Select(m => m.Compatibility.Level));
        AssertAheadWithin(worst, m => m.Compatibility.Level, m => m.Contemporaries);

        // The bottom level's contemporaries are not pushed off the list by the people the
        // ranking puts after them.
        int bottom = worst[0].Compatibility.Level;
        int there = vm.Matches.Count(m => m.Compatibility.Level == bottom && m.Contemporaries);
        Assert.Equal(Math.Min(there, 12), worst.Count(m => m.Compatibility.Level == bottom && m.Contemporaries));
        Assert.Contains("contemporaries", vm.MatchesSummary);
    }
}
