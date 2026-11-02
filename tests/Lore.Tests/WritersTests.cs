using System.Text.Json;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

public sealed class WritersTests
{
    private static Celebrity Person(string id, string date = "1890-01-01", double lat = 40, double lon = 0, string time = "12:00", string? death = null, string category = "Test") => new()
    {
        Id = id, Name = id, BirthDate = date, BirthTime = time, BirthTimeKnown = true,
        BirthPlace = "Test city", Latitude = lat, Longitude = lon, UtcOffsetFixed = true, Category = category,
        DeathDate = death ?? DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddYears(70).ToString("yyyy-MM-dd"),
    };
    private static City Chicago => new() { Name = "Chicago", Country = "USA", Latitude = 41.8781, Longitude = -87.6298, TimeZoneId = "America/Chicago" };
    private static WriterInterpreter Interpreter => new(Repo.Charts, new ChartInterpreter(Repo.Data("interpretations.json")), new SynastryInterpreter(Repo.Data("synastry.json")));

    [Fact]
    public void Ten_year_limit_uses_full_dates_and_a_defined_leap_day_anniversary()
    {
        Assert.True(WriterEligibility.WithinAge(new(1990, 6, 15), new(2000, 6, 15)));
        Assert.False(WriterEligibility.WithinAge(new(1990, 6, 15), new(2000, 6, 16)));
        Assert.True(WriterEligibility.WithinAge(new(1992, 2, 29), new(2002, 2, 28)));
        Assert.False(WriterEligibility.WithinAge(new(1992, 2, 29), new(2002, 3, 1)));
    }

    [Fact]
    public void A_trio_is_a_clique_not_a_chain_and_never_averages_the_age_gap()
    {
        var people = new[] { Person("a", "1880-01-01", 40, 0), Person("b", "1885-01-01", 40, 0.8), Person("c", "1890-01-02", 40, 1.6) };
        var graph = WriterEligibility.Build(people, new WriterGroupQuery(3, 100));
        Assert.Equal(2, graph.PairCount);
        var result = new WriterGroupService(Repo.Charts).Search(people, new WriterGroupQuery(3, 100));
        Assert.Equal(0, result.Trios); Assert.Empty(result.Results);
        // Even with the date fixed, the endpoints are too far apart.
        people[2] = Person("c", "1890-01-01", 40, 1.6);
        result = new WriterGroupService(Repo.Charts).Search(people, new WriterGroupQuery(3, 100));
        Assert.Equal(0, result.Trios);
        result = new WriterGroupService(Repo.Charts).Search(people, new WriterGroupQuery(3, 150));
        Assert.Equal(1, result.Trios); Assert.Single(result.Results);
    }

    [Fact]
    public void Distance_handles_date_line_and_real_zero_coordinates_but_missing_coordinates_are_excluded()
    {
        Assert.InRange(WriterEligibility.Distance(Person("a", lat: 0, lon: 179.9), Person("b", lat: 0, lon: -179.9)), 22, 23);
        var absent = new Celebrity { Id = "missing", Name = "missing", BirthDate = "1890-01-01", BirthPlace = "Unknown", Category = "Test", DeathDate = "1960-01-01" };
        var graph = WriterEligibility.Build([Person("a", lat: 0, lon: 0), Person("b", lat: 0, lon: 0), absent], new());
        Assert.Equal(1, graph.PairCount); Assert.Equal(1, graph.Skipped);
    }

    [Fact]
    public void Eligibility_normalizes_old_style_dates_before_checking_the_age_gap()
    {
        var julian = new Celebrity { Id = "julian", Name = "julian", BirthDate = "1700-02-29", JulianCalendar = true,
            BirthPlace = "Test", Latitude = 40, Longitude = 0, Category = "Test", DeathDate = "1770-01-01" };
        var atBoundary = Person("b", "1710-03-11");
        var graph = WriterEligibility.Build([julian, atBoundary], new());
        Assert.Equal(1, graph.PairCount);
        Assert.Equal(0, WriterEligibility.Build([julian, Person("late", "1710-03-12")], new()).PairCount);
    }

    [Fact]
    public void Global_search_is_independent_of_selection_complete_and_deterministic()
    {
        var people = Enumerable.Range(0, 8).Select(i => Person("person" + i, $"189{i}-06-15", time: $"{i + 5:00}:30")).ToArray();
        var service = new WriterGroupService(Repo.Charts);
        var query = new WriterGroupQuery(Size: 3, Keep: 200);
        var result = service.Search(people, query);
        Assert.Equal(28, result.Pairs); Assert.Equal(56, result.Trios); Assert.Equal(56, result.Matches);
        Assert.Equal(56, result.Results.Select(r => string.Join('|', r.Members.Select(m => m.Id).Order())).Distinct().Count());
        var reversed = service.Search(people.Reverse(), query);
        Assert.Equal(result.Results.Select(r => r.Title), reversed.Results.Select(r => r.Title));
        var best = service.Search(people, query with { Keep = 5 });
        Assert.Equal(56, best.Matches);
        Assert.Equal(result.Results.Take(5).Select(r => (r.Title, r.Share)), best.Results.Select(r => (r.Title, r.Share)));
    }

    [Fact]
    public void Pair_assessments_in_the_fast_global_pass_match_existing_synastry()
    {
        var charts = Repo.Sample.Take(18).Select(Repo.Charts.Calculate).ToArray();
        for (int a = 0; a < charts.Length; a++)
            for (int b = a + 1; b < charts.Length; b++)
            {
                var expected = SynastryScoring.Assess(SynastryService.Compare(charts[a], charts[b]).Aspects);
                var actual = WriterGroupService.AssessPair(charts[a], charts[b]);
                Assert.Equal(expected.Flow, actual.Flow, 10); Assert.Equal(expected.Tension, actual.Tension, 10);
                Assert.Equal(expected.Tone, actual.Tone);
            }
    }

    [Fact]
    public void Strong_trine_search_matches_the_existing_three_person_geometry()
    {
        var people = Enumerable.Range(0, 12).Select(i => Person("person" + i,
            new DateOnly(1888, 1, 1).AddDays(i * 137).ToString("yyyy-MM-dd"), time: $"{i + 3:00}:10")).ToArray();
        var charts = people.ToDictionary(p => p.Id, Repo.Charts.Calculate);
        var expected = new Dictionary<string, double>();
        for (int a = 0; a < people.Length; a++) for (int b = a + 1; b < people.Length; b++) for (int c = b + 1; c < people.Length; c++)
        {
            var trio = TrioService.Compare(charts[people[a].Id], charts[people[b].Id], charts[people[c].Id]);
            if (trio.Tone == TrioTone.Golden)
                expected[string.Join('|', new[] { people[a].Id, people[b].Id, people[c].Id }.Order())] = trio.GoldenWeight;
        }
        var actual = new WriterGroupService(Repo.Charts).Search(people, new WriterGroupQuery(3, TrioTone: TrioTone.Golden));
        Assert.NotEmpty(expected); Assert.Equal(expected.Count, actual.Matches);
        foreach (var group in actual.Results)
            Assert.Equal(expected[string.Join('|', group.Members.Select(m => m.Id).Order())], group.GrandTrineWeight, 10);
    }

    [Fact]
    public void Searches_cancel_and_invalid_queries_are_rejected()
    {
        var cancelled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => WriterEligibility.Build(Repo.Figures, new(), cancelled));
        Assert.Throws<OperationCanceledException>(() => new WriterGroupService(Repo.Charts).Search(Repo.Figures, new(), cancelled));
        Assert.Throws<ArgumentException>(() => WriterEligibility.Build(Repo.Figures, new(DistanceKm: double.NaN)));
        Assert.Throws<ArgumentException>(() => WriterEligibility.Build(Repo.Figures, new(TrioTone: (TrioTone)99)));
    }

    [Fact]
    public void Public_group_filters_do_not_include_personal_or_fictional_charts()
    {
        var personal = new Celebrity { Id = "mine", Name = "mine", BirthDate = "1990-01-01", Category = UserChartService.MyChartsCategory,
            BirthPlace = "Test", Latitude = 40, Longitude = 0 };
        var graph = WriterEligibility.Build([Person("a"), Person("b"), personal], new(Search: "Test", Category: "Test"));
        Assert.Equal(2, graph.People.Count); Assert.Equal(1, graph.PairCount);
    }

    [Fact]
    public void Prominence_filters_people_before_pair_and_trio_enumeration()
    {
        Celebrity Known(string id, int editions) => new() { Id = id, Name = id, BirthDate = "1890-01-01", BirthPlace = "Test",
            Latitude = 0, Longitude = 0, Sitelinks = editions, Category = "Test", DeathDate = "1960-01-01" };
        var graph = WriterEligibility.Build([Known("a", 100), Known("b", 30), Known("c", 9)], new(MinimumSitelinks: 30));
        Assert.Equal(2, graph.People.Count); Assert.Equal(1, graph.PairCount);
    }

    [Fact]
    public void Global_pair_assessment_does_not_invent_the_moon_or_angles_for_an_untimed_chart()
    {
        var a = Repo.Charts.Calculate(Demo.Person(false));
        var b = Repo.Charts.Calculate(Person("b"));
        var expected = SynastryScoring.Assess(SynastryService.Compare(a, b).Aspects);
        var actual = WriterGroupService.AssessPair(a, b);
        Assert.Equal(expected.Flow, actual.Flow, 10); Assert.Equal(expected.Tension, actual.Tension, 10);
    }

    [Fact]
    public void Generated_historical_births_round_trip_through_the_places_own_mean_time()
    {
        var place = new City { Name = "Paris", Country = "France", Latitude = 48.8566, Longitude = 2.3522, TimeZoneId = "Europe/Paris" };
        var found = new WriterCharacterService(Repo.Charts).Find(new(new(1800, 3, 1), 1, place, WriterAnchor.Midheaven));
        Assert.NotEmpty(found);
        foreach (var candidate in found)
        {
            Assert.Equal("1800-03-01", candidate.Birth.Person.BirthDate);
            Assert.InRange((candidate.Birth.Utc - BirthTimeResolver.ToUtc(candidate.Birth.Person)).TotalSeconds, 0, 60);
        }
    }

    [Fact]
    public async Task Opening_a_saved_reading_cancels_a_pending_recomposition()
    {
        var people = new[] { Person("a"), Person("b") };
        var group = new WriterGroupService(Repo.Charts).Search(people, new()).Results[0];
        var saved = Interpreter.Group(group, ChartSettings.Default) with { Title = "Keep this snapshot" };
        var vm = new WritersViewModel(Repo.Charts, Interpreter);
        var pending = vm.SelectGroupAsync(group);
        vm.ShowBrief(saved);
        await pending;
        Assert.Same(saved, vm.Brief);
    }

    [Fact]
    public void Sky_anchors_have_exact_events_and_location_specific_readings()
    {
        var service = new WriterCharacterService(Repo.Charts);
        var query = new WriterCharacterQuery(new(1990, 6, 1), 30, Chicago);
        var candidates = service.Find(query);
        var full = candidates.Single(c => c.Key == "FullMoon");
        var chart = Repo.Charts.CalculateAt(full.Birth.Person, full.Birth.Utc);
        Assert.InRange(Math.Abs(Math.Abs(chart.GetPlanet(Planet.Moon)!.Longitude - chart.GetPlanet(Planet.Sun)!.Longitude) - 180), 0, 0.001);
        Assert.Equal(full.Birth.Utc, BirthTimeResolver.LocalTimeAt(full.Birth.Person, full.Birth.Utc).AddHours(5));
        var brief = Interpreter.Character(full, ChartSettings.Default);
        Assert.Equal(full.Birth.Utc, brief.Births[0].Utc);
        var otherPlace = new City { Name = "Sydney", Country = "Australia", Latitude = -33.8688, Longitude = 151.2093, TimeZoneId = "Australia/Sydney" };
        var other = service.Find(query with { Place = otherPlace }).Single(c => c.Key == "FullMoon");
        Assert.Equal(full.Birth.Utc, other.Birth.Utc);
        var otherBrief = Interpreter.Character(other, ChartSettings.Default);
        Assert.NotEqual(brief.Sections[1].Items.Select(i => i.Text), otherBrief.Sections[1].Items.Select(i => i.Text));
        var expected = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(chart);
        Assert.Equal(expected.SelectMany(s => s.Paragraphs), brief.Sections.Skip(1).Take(expected.Count).SelectMany(s => s.Items).Select(i => i.Text));
    }

    [Theory]
    [InlineData(WriterAnchor.Ascendant)] [InlineData(WriterAnchor.Midheaven)]
    public void Local_angle_anchors_refine_and_stay_inside_the_local_period(WriterAnchor anchor)
    {
        var found = new WriterCharacterService(Repo.Charts).Find(new(new(2026, 11, 1), 1, Chicago, anchor));
        Assert.NotEmpty(found);
        foreach (var candidate in found)
        {
            Assert.Equal("2026-11-01", candidate.Birth.Person.BirthDate);
            var chart = Repo.Charts.CalculateAt(candidate.Birth.Person, candidate.Birth.Utc);
            double angle = anchor == WriterAnchor.Ascendant ? chart.Ascendant : chart.Midheaven;
            Assert.InRange(Repo.ArcMinutesBetween(chart.GetPlanet(Planet.Venus)!.Longitude, angle), 0, 0.06);
        }
    }

    [Fact]
    public void Combined_anchors_keep_both_constraints_and_patterns_are_labelled_as_samples()
    {
        var service = new WriterCharacterService(Repo.Charts);
        var query = new WriterCharacterQuery(new(1990, 6, 1), 30, Chicago, WriterAnchor.SkyAndAngle);
        foreach (var found in service.Find(query))
        {
            var chart = Repo.Charts.CalculateAt(found.Birth.Person, found.Birth.Utc);
            Assert.True(Math.Min(Repo.ArcMinutesBetween(chart.GetPlanet(Planet.Venus)!.Longitude, chart.Ascendant),
                Repo.ArcMinutesBetween(chart.GetPlanet(Planet.Venus)!.Longitude, chart.Midheaven)) <= 180);
        }
        var patterns = service.Find(query with { Days = 1, Anchor = WriterAnchor.Patterns });
        Assert.NotEmpty(patterns); Assert.All(patterns, p => Assert.Contains("hourly sample", p.Evidence));
    }

    [Fact]
    public async Task Saved_briefs_merge_between_windows_keep_exact_times_and_do_not_overwrite_damage()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lore-writers-" + Guid.NewGuid().ToString("N"));
        try
        {
            var candidate = new WriterCharacterService(Repo.Charts).Find(new(new(1990, 6, 1), 30, Chicago))[0];
            var brief = Interpreter.Character(candidate, ChartSettings.Default);
            var one = new WriterStore(directory); var two = new WriterStore(directory);
            await one.SaveAsync(brief);
            await two.SaveAsync(brief with { Id = "second", Title = "Another" });
            Assert.Equal(2, (await one.LoadAsync()).Count);
            var roundTrip = (await two.LoadAsync()).Single(b => b.Id == brief.Id);
            Assert.Equal(brief.Births[0].Utc, roundTrip.Births[0].Utc);
            Assert.Equal(WriterInterpreter.ToText(brief), WriterInterpreter.ToText(roundTrip));
            Assert.True(File.Exists(Path.Combine(directory, "writer-briefs.json.bak")));
            await File.WriteAllTextAsync(Path.Combine(directory, "writer-briefs.json"), "damaged");
            await Assert.ThrowsAnyAsync<JsonException>(() => one.SaveAsync(brief));
            Assert.Equal("damaged", await File.ReadAllTextAsync(Path.Combine(directory, "writer-briefs.json")));
            Assert.False(File.Exists(Path.Combine(directory, "mycharts.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Writers_works_without_a_selected_chart_and_cancel_cannot_deliver_stale_results()
    {
        var vm = new WritersViewModel(Repo.Charts, Interpreter);
        vm.SetPeople([Person("a"), Person("b")]);
        await vm.FindGroupsAsync(new()); Assert.Single(vm.Groups);
        await vm.SelectGroupAsync(vm.Groups[0]); Assert.True(vm.HasBrief);
        var pending = vm.FindCharactersAsync(new(new(1990, 1, 1), 366, Chicago, WriterAnchor.Ascendant));
        vm.Cancel(); await pending;
        Assert.Empty(vm.Candidates); Assert.False(vm.IsBusy); Assert.False(vm.HasBrief);
    }

    [Fact]
    public void Group_readings_name_every_pair_and_keep_their_inputs_and_export()
    {
        var people = new[] { Person("a", "1890-01-01"), Person("b", "1891-01-01"), Person("c", "1892-01-01") };
        var group = new WriterGroupService(Repo.Charts).Search(people, new(3)).Results[0];
        var brief = Interpreter.Group(group, ChartSettings.Default);
        var expected = new SynastryInterpreter(Repo.Data("synastry.json")).ComposeTrio(
            TrioService.Compare(Repo.Charts.Calculate(people[0]), Repo.Charts.Calculate(people[1]), Repo.Charts.Calculate(people[2])));
        Assert.Equal(expected.Sections.SelectMany(s => s.Items).Select(i => i.Text),
            brief.Sections.Take(expected.Sections.Count).SelectMany(s => s.Items).Select(i => i.Text));
        Assert.Equal(3, brief.Births.Count); Assert.Contains("within ten calendar years", WriterInterpreter.ToText(brief));
        Assert.True(DailyExportService.WritersToPdf(brief).Length > 1000);
    }

    [Fact]
    public void Death_floor_is_fifty_full_calendar_years_and_unknown_or_partial_deaths_cannot_pass()
    {
        var people = new[] { Person("old"), Person("boundary", death: "1976-11-01"),
            Person("recent", death: "1976-11-02"), Person("partial", death: "1960"),
            new Celebrity { Id = "unknown", BirthDate = "1890-01-01", Name = "unknown", Category = "Test", BirthPlace = "Test", Latitude = 40, Longitude = 0 } };
        var query = new WriterGroupQuery(AsOf: new DateOnly(2026, 11, 1));
        var graph = WriterEligibility.Build(people, query);
        Assert.Equal(2, graph.People.Count); Assert.Equal(1, graph.PairCount);
        Assert.Equal(3, WriterEligibility.Build(people, query with { AsOf = new DateOnly(2026, 11, 2) }).PairCount);
    }

    [Fact]
    public void Every_member_must_be_in_the_same_field_even_when_searching_all_fields()
    {
        var people = new[] { Person("writer-a", category: "Writer"), Person("writer-b", category: "Writer"), Person("artist", category: "Artist") };
        var result = new WriterGroupService(Repo.Charts).Search(people, new());
        Assert.Single(result.Results); Assert.All(result.Results[0].Members, p => Assert.Equal("Writer", p.Category));
        Assert.Empty(new WriterGroupService(Repo.Charts).Search(people, new(3)).Results);
    }

    [Fact]
    public void Pair_reading_is_the_existing_synastry_reading_without_a_prompt_layer()
    {
        var people = new[] { Person("a"), Person("b", "1891-01-01") };
        var group = new WriterGroupService(Repo.Charts).Search(people, new()).Results[0];
        var a = Repo.Charts.Calculate(people[0]); var b = Repo.Charts.Calculate(people[1]);
        var expected = new SynastryInterpreter(Repo.Data("synastry.json")).Compose(SynastryService.Compare(a, b, Repo.Charts.Davison(a, b)));
        var brief = Interpreter.Group(group, ChartSettings.Default);
        Assert.Equal(expected.Sections.SelectMany(s => s.Items).Select(i => i.Text),
            brief.Sections.Take(expected.Sections.Count).SelectMany(s => s.Items).Select(i => i.Text));
        Assert.Equal(expected.Tone.Label(), group.Tone); Assert.Equal(expected.Compatibility.Share, group.Share, 10);
    }

    [Fact]
    public async Task Release_gate_rejects_private_writer_briefs_and_their_backups()
    {
        string stage = Path.Combine(Path.GetTempPath(), "lore-writer-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "Data"));
            File.Copy(Repo.Data("celebrities.json"), Path.Combine(stage, "Data", "celebrities.json"));
            async Task<int> Check()
            {
                var start = new System.Diagnostics.ProcessStartInfo("pwsh")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { "-NoProfile", "-File", Path.Combine(Repo.Root, "build", "assert-clean-release.ps1"), "-StageDir", stage }) start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(); await Task.WhenAll(output, error);
                return process.ExitCode;
            }
            Assert.Equal(0, await Check());
            await File.WriteAllTextAsync(Path.Combine(stage, "writer-briefs.json.bak"), "[]");
            Assert.NotEqual(0, await Check());
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
