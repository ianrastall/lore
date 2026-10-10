using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The personality inventory: the IPIP-NEO-120 as bundled, its scoring, where the answers
// are kept, and the view that asks the questions and shows the profile.
public sealed class InventoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly InventoryService Inventory = new(Repo.Data("inventory.json"));

    private static int[] All(int answer) => Enumerable.Repeat(answer, 120).ToArray();

    private static readonly DateOnly Day = new(2026, 10, 10);

    // ── The instrument ────────────────────────────────────────────────────────

    [Fact]
    public void The_questionnaire_is_whole_120_statements_four_to_each_of_30_facets_six_facets_to_a_trait()
    {
        var q = Inventory.Instrument;
        Assert.True(Inventory.IsAvailable);
        Assert.Equal(120, q.Items.Count);
        Assert.Equal(Enumerable.Range(1, 120), q.Items.Select(i => i.N));
        Assert.Equal(new[] { "N", "E", "O", "A", "C" }, q.Domains.Select(d => d.Key));
        Assert.Equal(30, q.Facets.Count);
        Assert.Equal(5, q.Choices.Count);
        Assert.All(q.Domains, d => Assert.Equal(6, q.Facets.Count(f => f.Domain == d.Key)));
        Assert.All(q.Facets, f => Assert.Equal(4, q.Items.Count(i => i.Facet == f.Key)));

        // Johnson's order goes round the five traits: statement 1 is N1, 2 is E1 … 6 is N2.
        Assert.Equal(new[] { "N1", "E1", "O1", "A1", "C1", "N2" }, q.Items.Take(6).Select(i => i.Facet));
        Assert.Equal("Worry about things", q.Items[0].Text);
        Assert.Equal(55, q.Items.Count(i => i.Reversed)); // as in the published key

        // Every trait and facet has its reference figures and all its wording.
        Assert.All(q.Facets, f =>
        {
            Assert.InRange(f.Mean, 4, 20);
            Assert.InRange(f.Sd, 1, 6);
            Assert.All(new[] { f.Name, f.Measures, f.Low, f.Typical, f.High }, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
        Assert.All(q.Domains, d =>
        {
            Assert.InRange(d.Mean, 4, 20);
            Assert.All(new[] { d.Name, d.About, d.Low, d.Typical, d.High }, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        });
        Assert.All(q.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
        Assert.Equal(120, q.Items.Select(i => i.Text).Distinct().Count());
    }

    [Fact]
    public void A_missing_or_damaged_questionnaire_is_not_offered()
    {
        Assert.False(new InventoryService(Path.Combine(_dir, "nothing.json")).IsAvailable);
        Directory.CreateDirectory(_dir);
        string bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.False(new InventoryService(bad).IsAvailable);
        File.WriteAllText(bad, """{"choices":["a","b","c","d","e"],"domains":[],"facets":[],"items":[{"n":1,"text":"x","facet":"Z9"}]}""");
        Assert.False(new InventoryService(bad).IsAvailable);
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    [Fact]
    public void Neither_way_on_everything_scores_twelve_on_every_facet()
    {
        var profile = Inventory.Score(All(3), "Someone", Day);
        Assert.Equal(5, profile.Domains.Count);
        Assert.All(profile.Domains, d =>
        {
            Assert.Equal(12, d.Score, 9);
            Assert.Equal(6, d.Facets.Count);
            Assert.All(d.Facets, f => Assert.Equal(12, f.Raw));
        });
    }

    [Fact]
    public void A_reversed_statement_counts_the_other_way()
    {
        // "Very accurate" to everything: 5 for each statement keyed forwards, 1 for each reversed.
        var profile = Inventory.Score(All(5), "Someone", Day);
        foreach (var f in profile.Domains.SelectMany(d => d.Facets))
        {
            int reversed = Inventory.Instrument.Items.Count(i => i.Facet == f.Facet.Key && i.Reversed);
            Assert.Equal(5 * (4 - reversed) + reversed, f.Raw);
        }

        // Answering each statement so as to score highest on Anxiety and lowest on Anger.
        var answers = All(3);
        foreach (var item in Inventory.Instrument.Items)
        {
            if (item.Facet == "N1") answers[item.N - 1] = item.Reversed ? 1 : 5;
            if (item.Facet == "N2") answers[item.N - 1] = item.Reversed ? 5 : 1;
        }
        var n = Inventory.Score(answers, "Someone", Day).Domains.Single(d => d.Domain.Key == "N");
        Assert.Equal(20, n.Facets.Single(f => f.Facet.Key == "N1").Raw);
        Assert.Equal(4, n.Facets.Single(f => f.Facet.Key == "N2").Raw);
        Assert.Equal((20 + 4 + 12 * 4) / 6.0, n.Score, 9);
    }

    [Fact]
    public void A_score_is_placed_against_the_reference_figures()
    {
        var answers = All(3);
        foreach (var item in Inventory.Instrument.Items.Where(i => i.Facet == "N1"))
            answers[item.N - 1] = item.Reversed ? 1 : 5;
        var profile = Inventory.Score(answers, "Someone", Day);

        // Anxiety: 20 against an average of 12.07 and a spread of 3.77.
        var anxiety = profile.Domains[0].Facets[0];
        Assert.Equal((20 - 12.07) / 3.77, anxiety.Z, 9);
        Assert.Equal(ScoreBand.VeryHigh, anxiety.Band);
        Assert.Equal("Very high", anxiety.BandLabel);
        Assert.StartsWith("Worries a good deal", anxiety.Text);
        Assert.Equal("20 of 20", anxiety.ScoreText);

        // Self-efficacy: 12 against 16.32 and 2.40 is well below.
        var efficacy = profile.Domains.Single(d => d.Domain.Key == "C").Facets[0];
        Assert.Equal(ScoreBand.Low, efficacy.Band);
        Assert.StartsWith("Doubts own ability", efficacy.Text);

        // Activity level: 12 against 12.84 and 3.15 is ordinary.
        var activity = profile.Domains.Single(d => d.Domain.Key == "E").Facets[3];
        Assert.Equal(ScoreBand.Typical, activity.Band);
        Assert.Equal("Keeps a moderate pace.", activity.Text);
    }

    [Theory]
    [InlineData(-2.5, ScoreBand.VeryLow)]
    [InlineData(-2.0, ScoreBand.VeryLow)]
    [InlineData(-1.5, ScoreBand.Low)]
    [InlineData(-1.0, ScoreBand.Typical)]
    [InlineData(0.0, ScoreBand.Typical)]
    [InlineData(1.0, ScoreBand.Typical)]
    [InlineData(1.5, ScoreBand.High)]
    [InlineData(2.0, ScoreBand.VeryHigh)]
    public void The_bands_are_cut_at_one_and_two_standard_deviations(double z, ScoreBand band)
    {
        Assert.Equal(band, ScoreBandExtensions.Of(z));
    }

    [Fact]
    public void The_share_scoring_lower_follows_the_bell_curve_and_is_never_given_as_none_or_all()
    {
        Assert.Equal(50, InventoryProfile.PercentileOf(0), 4);
        Assert.Equal(84.134, InventoryProfile.PercentileOf(1), 2);
        Assert.Equal(15.866, InventoryProfile.PercentileOf(-1), 2);
        Assert.Equal(97.725, InventoryProfile.PercentileOf(2), 2);
        Assert.Equal("higher than about 85 in 100", InventoryProfile.PercentileText(84.1));
        Assert.Equal("higher than about 95 in 100", InventoryProfile.PercentileText(99.99));
        Assert.Equal("higher than about 5 in 100", InventoryProfile.PercentileText(0.01));
    }

    [Fact]
    public void An_unfinished_set_of_answers_is_not_scored()
    {
        var answers = All(4);
        answers[42] = 0;
        Assert.Equal(119, Inventory.Answered(answers));
        Assert.Equal(43, Inventory.FirstUnanswered(answers));
        Assert.Throws<InvalidOperationException>(() => Inventory.Score(answers, "Someone", Day));
        Assert.Null(Inventory.FirstUnanswered(All(4)));
        Assert.Equal(1, Inventory.FirstUnanswered([]));
    }

    [Fact]
    public void The_profile_as_text_names_every_trait_and_facet_and_says_what_it_is()
    {
        string text = System.Text.Encoding.UTF8.GetString(Inventory.ToText(Inventory.Score(All(3), "Ann Example", Day)));
        Assert.Contains("Ann Example — Personality profile", text);
        Assert.Contains("IPIP-NEO-120, answered on 10 October 2026", text);
        Assert.Contains("no part of it comes from the birth chart", text);
        Assert.All(Inventory.Instrument.Domains, d => Assert.Contains(d.Name.ToUpperInvariant() + ":", text));
        Assert.All(Inventory.Instrument.Facets, f => Assert.Contains($"  {f.Name}: ", text));
        Assert.Contains("320,128 people", text);
    }

    // ── Where the answers are kept ────────────────────────────────────────────

    [Fact]
    public void Answers_are_kept_by_chart_in_a_file_of_their_own_and_come_back()
    {
        var store = new InventoryStore(_dir);
        Assert.Empty(store.Get("user-1"));

        var sitting = new InventorySitting { Started = "2026-10-10", Answers = All(0) };
        sitting.Answers[0] = 5;
        store.Save("user-1", [sitting]);
        store.Save("user-2", [new InventorySitting { Started = "2026-10-09", Completed = "2026-10-09", Answers = All(2) }]);
        store.Flush();

        Assert.Equal("inventories.json", Path.GetFileName(store.FilePath));
        Assert.True(File.Exists(store.FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        var again = new InventoryStore(_dir);
        var back = Assert.Single(again.Get("user-1"));
        Assert.Equal(5, back.Answers[0]);
        Assert.False(back.IsComplete);
        Assert.True(Assert.Single(again.Get("user-2")).IsComplete);

        // What is handed out is a copy: changing it changes nothing until it is saved.
        back.Answers[0] = 1;
        Assert.Equal(5, again.Get("user-1")[0].Answers[0]);

        again.Remove("user-1");
        again.Flush();
        var last = new InventoryStore(_dir);
        Assert.Empty(last.Get("user-1"));
        Assert.Single(last.Get("user-2"));
    }

    [Fact]
    public void Two_windows_saving_for_different_people_keep_each_others_answers()
    {
        var first = new InventoryStore(_dir);
        var second = new InventoryStore(_dir);   // opened before the first has saved anything
        first.Save("user-1", [new InventorySitting { Started = "2026-10-10", Answers = All(1) }]);
        first.Flush();
        second.Save("user-2", [new InventorySitting { Started = "2026-10-10", Answers = All(2) }]);
        second.Flush();

        var both = new InventoryStore(_dir);
        Assert.Single(both.Get("user-1"));
        Assert.Single(both.Get("user-2"));
    }

    [Fact]
    public void A_file_that_cannot_be_understood_is_set_aside_not_written_over()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "inventories.json"), "precious but damaged");

        var store = new InventoryStore(_dir);
        Assert.Empty(store.Get("user-1"));
        var kept = Assert.Single(Directory.GetFiles(_dir, "inventories.unreadable-*.json"));
        Assert.Equal("precious but damaged", File.ReadAllText(kept));
    }

    // ── The view ──────────────────────────────────────────────────────────────

    private static NatalChart Own(string id = "user-1", string name = "Ann Example") => Repo.Charts.Calculate(new Celebrity
    {
        Id = id, Name = name, Category = UserChartService.MyChartsCategory,
        BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
        BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55, TimeZoneId = "Europe/London",
    });

    private InventoryViewModel View(out InventoryStore store)
    {
        store = new InventoryStore(_dir);
        return new InventoryViewModel(Inventory, store);
    }

    [Fact]
    public void It_is_offered_for_your_own_charts_and_not_for_the_figures_Lore_comes_with()
    {
        var vm = View(out _);
        Assert.Equal(InventoryStage.NoChart, vm.Stage);

        vm.Chart = Repo.Charts.Calculate(Repo.Figure("elvis-presley"));
        Assert.Equal(InventoryStage.NotOffered, vm.Stage);
        Assert.Contains("Elvis Presley is one of the figures Lore comes with", vm.NotOfferedText);

        vm.Chart = Own();
        Assert.Equal(InventoryStage.Introduction, vm.Stage);
        Assert.Equal("Ann Example — Personality inventory", vm.Title);
        Assert.Contains("120 short statements", vm.AboutText);

        vm.Chart = null;
        Assert.Equal(InventoryStage.NoChart, vm.Stage);

        var without = new InventoryViewModel(new InventoryService(Path.Combine(_dir, "nothing.json")), null) { Chart = Own() };
        Assert.Equal(InventoryStage.Unavailable, without.Stage);
    }

    [Fact]
    public void Statements_are_asked_ten_at_a_time_and_each_answer_is_saved_as_it_is_given()
    {
        var vm = View(out var store);
        vm.Chart = Own();
        vm.BeginCommand.Execute(null);

        Assert.Equal(InventoryStage.Asking, vm.Stage);
        Assert.Equal(12, vm.PageCount);
        Assert.Equal(10, vm.Questions.Count);
        Assert.Equal("Statements 1 to 10 of 120", vm.PageText);
        Assert.Equal("1.", vm.Questions[0].NumberText);
        Assert.Equal("Worry about things", vm.Questions[0].Text);
        Assert.Equal(-1, vm.Questions[0].AnswerIndex);
        Assert.False(vm.CanGoBack);

        vm.Questions[0].AnswerIndex = 4;   // very accurate
        vm.Questions[1].AnswerIndex = 0;   // very inaccurate
        vm.Questions[1].AnswerIndex = -1;  // the list of choices clearing itself is not an answer
        Assert.Equal(0, vm.Questions[1].AnswerIndex);
        Assert.Equal("2 of 120 answered", vm.ProgressText);

        vm.NextCommand.Execute(null);
        Assert.Equal("Statements 11 to 20 of 120", vm.PageText);
        Assert.Equal(11, vm.Questions[0].Number);
        vm.PreviousCommand.Execute(null);
        Assert.Equal(4, vm.Questions[0].AnswerIndex);   // shown again as it was answered

        store.Flush();
        var saved = Assert.Single(new InventoryStore(_dir).Get("user-1"));
        Assert.Equal(new[] { 5, 1, 0 }, saved.Answers.Take(3));
        Assert.False(saved.IsComplete);
    }

    [Fact]
    public void A_sitting_left_half_way_is_taken_up_again_at_the_first_statement_not_answered()
    {
        var vm = View(out var store);
        vm.Chart = Own();
        vm.BeginCommand.Execute(null);
        for (int page = 0; page < 3; page++)
        {
            foreach (var q in vm.Questions) q.AnswerIndex = 2;
            vm.NextCommand.Execute(null);
        }
        vm.Questions[0].AnswerIndex = 2;   // statement 31, and no further
        store.Flush();

        // Lore closed and opened again; and another person looked at in between.
        var later = new InventoryViewModel(Inventory, new InventoryStore(_dir));
        later.Chart = Own("user-2", "Ben Other");
        Assert.Equal(InventoryStage.Introduction, later.Stage);
        later.Chart = Own();
        Assert.Equal(InventoryStage.Asking, later.Stage);
        Assert.Equal("Statements 31 to 40 of 120", later.PageText);
        Assert.Equal("31 of 120 answered", later.ProgressText);
        Assert.Equal(2, later.Questions[0].AnswerIndex);
        Assert.Equal(-1, later.Questions[1].AnswerIndex);

        // The same person's chart calculated again (a setting changed) leaves the page alone.
        later.NextCommand.Execute(null);
        later.Chart = Own();
        Assert.Equal("Statements 41 to 50 of 120", later.PageText);
    }

    [Fact]
    public void Finishing_needs_every_statement_and_then_shows_the_profile()
    {
        var vm = View(out var store);
        vm.Chart = Own();
        vm.BeginCommand.Execute(null);
        for (int page = 0; page < vm.PageCount; page++)
        {
            foreach (var q in vm.Questions.Where(q => q.Number != 57)) q.AnswerIndex = 3;
            if (vm.CanGoOn) vm.NextCommand.Execute(null);
        }
        Assert.True(vm.IsLastPage);

        vm.FinishCommand.Execute(null);
        Assert.Equal(InventoryStage.Asking, vm.Stage);
        Assert.Equal("1 statement is still to be answered; the first is number 57.", vm.Notice);
        Assert.Equal("Statements 51 to 60 of 120", vm.PageText);

        vm.Questions.Single(q => q.Number == 57).AnswerIndex = 3;
        Assert.Equal("", vm.Notice);
        vm.FinishCommand.Execute(null);

        Assert.Equal(InventoryStage.Profile, vm.Stage);
        Assert.Equal(5, vm.Domains.Count);
        Assert.Equal("Ann Example", vm.Profile!.Name);
        Assert.StartsWith("Answered on ", vm.TakenText);
        Assert.Contains("Personality profile", System.Text.Encoding.UTF8.GetString(vm.ProfileText()));

        // It is there the next time, and a second sitting leaves the first in place.
        store.Flush();
        var later = new InventoryViewModel(Inventory, new InventoryStore(_dir)) { Chart = Own() };
        Assert.Equal(InventoryStage.Profile, later.Stage);
        later.TakeAgainCommand.Execute(null);
        Assert.Equal(InventoryStage.Asking, later.Stage);
        Assert.Equal(-1, later.Questions[0].AnswerIndex);
        later.AbandonCommand.Execute(null);
        Assert.Equal(InventoryStage.Profile, later.Stage);   // back to the finished one

        later.DeleteAnswers();
        Assert.Equal(InventoryStage.Introduction, later.Stage);
        Assert.Null(later.Profile);
    }
}
