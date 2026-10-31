using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The Trine view: three charts set together, the grand trines running through all three,
// and where the Golden step falls.
public class TrioServiceTests
{
    private static readonly SynastryInterpreter Interpreter = new(Repo.Data("synastry.json"));

    // An untimed chart holding just the given bodies, so the only contacts are the ones intended.
    private static NatalChart Chart(string name, params (Planet planet, double lon)[] bodies) => new()
    {
        Celebrity = new Celebrity { Id = name, Name = name, BirthDate = "2000-01-01", BirthTimeKnown = false },
        Planets = bodies.Select(x => new PlanetPosition { Planet = x.planet, Longitude = x.lon }).ToList(),
        Houses = Enumerable.Range(1, 12).Select(h => new HouseCusp { House = h, Longitude = (h - 1) * 30 }).ToList(),
        Aspects = [],
    };

    private static NatalChart Figure(string id) => Repo.Charts.Calculate(Repo.Figure(id));

    [Fact]
    public void A_grand_trine_takes_one_point_from_each_of_the_three()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)),      // Aries
            Chart("Ben", (Planet.Venus, 131)),    // Leo
            Chart("Cara", (Planet.Mars, 251)));   // Sagittarius

        var grand = Assert.Single(trio.GrandTrines);
        Assert.Equal([0, 1, 2], grand.Corners.Select(c => c.Person));
        Assert.Equal(Element.Fire, grand.Element);
        Assert.Equal(1, grand.WidestOrb, precision: 6);
    }

    [Fact]
    public void Two_points_in_one_chart_do_not_make_a_grand_trine_through_all_three()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Moon, 130)),
            Chart("Ben", (Planet.Venus, 250)),
            Chart("Cara", (Planet.Mars, 40)));   // nowhere near the triangle
        Assert.Empty(trio.GrandTrines);
    }

    [Fact]
    public void A_trine_beyond_the_orb_breaks_the_triangle()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)),
            Chart("Ben", (Planet.Venus, 130)),
            Chart("Cara", (Planet.Mars, 257)));  // 7° from a trine with Anna's Sun
        Assert.Empty(trio.GrandTrines);
    }

    [Fact]
    public void Slow_planets_cannot_make_up_a_grand_trine_on_their_own()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Neptune, 10)),
            Chart("Ben", (Planet.Pluto, 130)),
            Chart("Cara", (Planet.Sun, 250)));
        Assert.Empty(trio.GrandTrines);
    }

    [Fact]
    public void A_triangle_across_sign_boundaries_has_no_element_and_the_reading_says_so()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 359)),     // Pisces
            Chart("Ben", (Planet.Venus, 120)),    // Leo
            Chart("Cara", (Planet.Mars, 240)));   // Sagittarius

        Assert.Null(Assert.Single(trio.GrandTrines).Element);
        var item = Interpreter.ComposeTrio(trio).Sections[1].Items[0];
        Assert.Contains("Out of sign", item.Meta);
        Assert.Contains("runs out of sign", item.Text);
    }

    [Fact]
    public void A_strong_grand_trine_with_no_pair_in_difficulty_is_Golden()
    {
        // Three people joined by close trines of the most personal points, with nothing hard between them.
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Moon, 11), (Planet.Venus, 12)),
            Chart("Ben", (Planet.Sun, 130), (Planet.Moon, 131), (Planet.Venus, 132)),
            Chart("Cara", (Planet.Sun, 250), (Planet.Moon, 251), (Planet.Venus, 252)));

        Assert.Equal(TrioTone.Golden, trio.Tone);
    }

    [Fact]
    public void A_pair_in_difficulty_rules_out_Golden_however_strong_the_grand_trine()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Moon, 11), (Planet.Venus, 12)),
            // A trine to Anna and Cara, and a wall of squares to Cara's other points.
            Chart("Ben", (Planet.Sun, 130), (Planet.Moon, 131), (Planet.Mars, 340), (Planet.Saturn, 341), (Planet.Venus, 342)),
            Chart("Cara", (Planet.Sun, 250), (Planet.Moon, 251), (Planet.Mars, 70), (Planet.Saturn, 71), (Planet.Venus, 72)));

        Assert.Contains(trio.Pairs, p => p.Compatibility.Level < 0);
        Assert.NotEqual(TrioTone.Golden, trio.Tone);
    }

    [Fact]
    public void Golden_stays_rare_across_the_library()
    {
        var charts = Repo.Figures.Select(Repo.Charts.Calculate).ToList();
        var random = new Random(4101);
        var counts = Enum.GetValues<TrioTone>().ToDictionary(t => t, _ => 0);
        const int samples = 4000;
        for (int n = 0; n < samples;)
        {
            int a = random.Next(charts.Count), b = random.Next(charts.Count), c = random.Next(charts.Count);
            if (a == b || a == c || b == c) continue;
            counts[TrioService.Compare(charts[a], charts[b], charts[c]).Tone]++;
            n++;
        }

        double Share(TrioTone t) => (double)counts[t] / samples;
        Assert.InRange(Share(TrioTone.Golden), 0.015, 0.06);
        Assert.InRange(Share(TrioTone.Harmonious), 0.18, 0.33);
        Assert.InRange(Share(TrioTone.Mixed), 0.27, 0.41);
        Assert.InRange(Share(TrioTone.Strained), 0.30, 0.45);
    }

    [Fact]
    public void The_reading_names_all_three_and_leaves_no_placeholder_unfilled()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(
            Figure("albert-einstein"), Figure("marilyn-monroe"), Figure("elvis-presley")));

        Assert.Equal("Albert Einstein, Marilyn Monroe and Elvis Presley", reading.Title);
        Assert.Equal("At a glance", reading.Sections[0].Heading);
        Assert.Equal("How this is measured", reading.Sections[^1].Heading);
        var pairs = reading.Sections.Single(s => s.Heading == "The three pairs");
        Assert.Equal(3, pairs.Items.Count);
        Assert.Single(pairs.Items, i => i.Meta!.Contains("the weakest of the three"));

        string text = string.Join("\n", reading.Sections.SelectMany(s => s.Items.Select(i => $"{i.Title}|{i.Meta}|{i.Text}")));
        Assert.DoesNotContain("{", text);
        Assert.Contains("Albert", text);
        Assert.Contains("Marilyn", text);
        Assert.Contains("Elvis", text);
        Assert.All(reading.Sections.SelectMany(s => s.Items), i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
    }

    [Fact]
    public void The_same_three_charts_always_give_the_same_reading_in_any_order_of_grand_trines()
    {
        static string Text(TrioReading r) => string.Join("\n", r.Sections.SelectMany(s => s.Items.Select(i => $"{i.Title}|{i.Text}")));
        var (a, b, c) = (Figure("albert-einstein"), Figure("marilyn-monroe"), Figure("elvis-presley"));
        Assert.Equal(Text(Interpreter.ComposeTrio(TrioService.Compare(a, b, c))),
                     Text(Interpreter.ComposeTrio(TrioService.Compare(a, b, c))));
    }

    [Fact]
    public void Every_step_and_element_has_its_text()
    {
        foreach (var tone in Enum.GetValues<TrioTone>())
        {
            var trio = new Trio { Charts = [Chart("A"), Chart("B"), Chart("C")], Pairs = [], GrandTrines = [], Tone = tone };
            Assert.False(string.IsNullOrWhiteSpace(Glance(trio)));
        }
        foreach (double start in new[] { 10.0, 40.0, 70.0, 100.0 })   // Aries, Taurus, Gemini, Cancer
        {
            var trio = TrioService.Compare(
                Chart("Anna", (Planet.Sun, start)), Chart("Ben", (Planet.Venus, start + 120)), Chart("Cara", (Planet.Mars, start + 240)));
            Assert.StartsWith("A circuit of", Interpreter.ComposeTrio(trio).Sections[1].Items[0].Text);
        }
    }

    private static string Glance(Trio trio)
    {
        // Pairs are empty here, so only the tone's own line is under test.
        var withPair = new Trio
        {
            Charts = trio.Charts, GrandTrines = [], Tone = trio.Tone,
            Pairs = [new TrioPair(0, 1, SynastryService.Compare(trio.Charts[0], trio.Charts[1]), new Compatibility(SynastryTone.Light, 0, 0))],
        };
        return Interpreter.ComposeTrio(withPair).Sections[0].Items[0].Text;
    }

    [Fact]
    public void Grand_trines_in_one_element_are_described_once()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Mercury, 12)),
            Chart("Ben", (Planet.Venus, 130)),
            Chart("Cara", (Planet.Mars, 250))));

        var items = reading.Sections[1].Items;
        Assert.Equal(2, items.Count);
        Assert.StartsWith("A circuit of fire", items[0].Text);
        Assert.StartsWith("Another strand", items[1].Text);
    }

    [Fact]
    public void A_chart_with_no_birth_time_says_whose()
    {
        var timed = Figure("albert-einstein");
        var reading = Interpreter.ComposeTrio(TrioService.Compare(timed, Figure("marilyn-monroe"), Chart("Cara Smith", (Planet.Sun, 10))));
        Assert.Contains(reading.Sections[0].Items, i => i.Text.StartsWith("Cara has no recorded birth time"));
    }

    [Fact]
    public void The_search_for_a_third_leaves_out_the_two_and_puts_Golden_first()
    {
        var (a, b) = (Figure("albert-einstein"), Figure("marilyn-monroe"));
        var people = Repo.Figures.Take(200).Append(a.Celebrity).Append(b.Celebrity).ToList();

        var matches = TrioService.Rank(Repo.Charts, a, b, people);

        Assert.DoesNotContain(matches, m => m.Person.Id == a.Celebrity.Id || m.Person.Id == b.Celebrity.Id);
        Assert.Equal(matches.OrderBy(m => m.Tone).Select(m => m.Tone), matches.Select(m => m.Tone));
    }

    [Fact]
    public void The_finder_tries_every_pair_of_the_connections_once_and_puts_Golden_first()
    {
        var chart = Figure("albert-einstein");
        var connections = Repo.Figures.Where(f => f.Id != chart.Celebrity.Id).Take(30).ToList();

        var found = TrioService.FindTrios(Repo.Charts, chart, connections);

        Assert.Equal(30 * 29 / 2, found.Count);
        Assert.Equal(found.Count, found.Select(f => f.Name).Distinct().Count());
        Assert.DoesNotContain(found, f => f.Second.Id == f.Third.Id);
        Assert.Equal(found.OrderBy(f => f.Tone).Select(f => f.Tone), found.Select(f => f.Tone));

        // What a row says is what the reading of those three says.
        var row = found[0];
        var trio = TrioService.Compare(chart, Repo.Charts.Calculate(row.Second), Repo.Charts.Calculate(row.Third));
        Assert.Equal(trio.Tone, row.Tone);
        Assert.Equal(trio.GrandTrines.Count, row.GrandTrines);
    }

    [Fact]
    public async Task The_finder_opens_with_one_person_and_a_row_chooses_both_of_the_others()
    {
        var vm = new SynastryViewModel(Repo.Charts, Interpreter);
        async Task Settled()
        {
            await Task.Delay(50);
            for (int i = 0; vm.IsBusy || vm.IsSearching || vm.IsFindingThird; i++)
            {
                Assert.True(i < 3000, "The synastry view did not settle.");
                await Task.Delay(10);
            }
        }

        vm.SetPeople(Repo.Figures.Take(40).ToList());
        vm.Chart = Figure("albert-einstein");
        await Settled();
        Assert.True(vm.ShowMatches);

        vm.ShowTrine = true;
        await Settled();
        Assert.True(vm.FinderShown);
        Assert.False(vm.ShowMatches);
        Assert.NotEmpty(vm.BestTrios);
        Assert.StartsWith("Tried ", vm.FinderSummary);
        Assert.Equal("", vm.PromptText);

        var row = vm.BestTrios[0];
        vm.Choose(row);
        await Settled();
        Assert.False(vm.FinderShown);
        Assert.True(vm.TrioShown);
        Assert.Equal(row.Tone, vm.TrioReading!.Trio.Tone);
        Assert.Contains(row.Second.Name, vm.Title);
        Assert.Contains(row.Third.Name, vm.Title);

        // Clearing the second person goes back to the finder, with the third gone too.
        vm.ClearPartnerCommand.Execute(null);
        await Settled();
        Assert.Null(vm.Third);
        Assert.True(vm.FinderShown);
        Assert.NotEmpty(vm.BestTrios);
    }

    [Fact]
    public void The_trine_reading_saves_as_a_PDF_and_as_text()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(
            Figure("albert-einstein"), Figure("marilyn-monroe"), Figure("elvis-presley")));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(SynastryExportService.TrioToPdf(reading, []), 0, 4));
        string text = System.Text.Encoding.UTF8.GetString(SynastryExportService.TrioToText(reading));
        Assert.Contains("Albert Einstein, Marilyn Monroe and Elvis Presley — Trine", text);
        Assert.Contains("GRAND TRINES THROUGH ALL THREE CHARTS", text);
    }

    [Fact]
    public void Each_trine_export_kind_has_a_format()
    {
        Assert.Equal(".pdf", ExportRequest.Format("trinepdf").Extension);
        Assert.Equal(".txt", ExportRequest.Format("trinetxt").Extension);
        Assert.Equal(".png", ExportRequest.Format("trinepng").Extension);
    }

    [Fact]
    public async Task The_view_shows_the_search_then_the_three_and_keeps_one_mode_at_a_time()
    {
        var vm = new SynastryViewModel(Repo.Charts, Interpreter);
        async Task Settled()
        {
            await Task.Delay(50);
            for (int i = 0; vm.IsBusy || vm.IsFindingThird; i++)
            {
                Assert.True(i < 3000, "The synastry view did not settle.");
                await Task.Delay(10);
            }
        }

        vm.SetPeople(Repo.Figures.Take(60).ToList());
        vm.Chart = Figure("albert-einstein");
        vm.Partner = Repo.Figure("marilyn-monroe");
        await Settled();

        vm.ShowTrine = true;
        await Settled();
        Assert.True(vm.ThirdsShown);
        Assert.False(vm.ReadingShown);
        Assert.False(vm.ComparisonShown);
        Assert.NotEmpty(vm.BestThirds);
        Assert.Empty(vm.Sections);

        vm.Third = vm.BestThirds[0].Person;
        await Settled();
        Assert.True(vm.TrioShown);
        Assert.False(vm.ThirdsShown);
        Assert.Equal("At a glance", vm.Sections[0].Heading);
        Assert.StartsWith("The three read as", vm.ToneText);
        Assert.Contains(vm.Third!.Name, vm.Title);

        // Davison and Trine are never on together.
        vm.ShowDavison = true;
        Assert.False(vm.ShowTrine);
        vm.ShowTrine = true;
        Assert.False(vm.ShowDavison);

        // Someone else as the partner: nothing of the last three stays on screen.
        vm.Partner = Repo.Figure("elvis-presley");
        Assert.False(vm.TrioShown);
        await Settled();
        Assert.True(vm.TrioShown);
        Assert.Contains("Elvis Presley", vm.Title);

        vm.ClearThirdCommand.Execute(null);
        await Settled();
        Assert.True(vm.ThirdsShown);
    }

    // ── The other figures ─────────────────────────────────────────────────────

    [Fact]
    public void Three_points_together_make_a_meeting_point()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)), Chart("Ben", (Planet.Venus, 12)), Chart("Cara", (Planet.Mars, 14)));
        var figure = Assert.Single(trio.Figures);
        Assert.Equal(TrioFigureKind.Meeting, figure.Kind);
        Assert.Null(figure.Focus);
        Assert.Equal(4, figure.WidestOrb, precision: 6);
    }

    [Fact]
    public void Two_opposed_and_one_square_to_both_make_a_t_square_with_the_third_at_its_focus()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)), Chart("Ben", (Planet.Venus, 100)), Chart("Cara", (Planet.Mars, 190)));
        var figure = Assert.Single(trio.Figures);
        Assert.Equal(TrioFigureKind.TSquare, figure.Kind);
        Assert.Equal(1, figure.Focus);   // Ben, between the other two
    }

    [Fact]
    public void Two_in_sextile_and_one_quincunx_both_make_a_yod()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)), Chart("Ben", (Planet.Venus, 70)), Chart("Cara", (Planet.Mars, 220)));
        var figure = Assert.Single(trio.Figures);
        Assert.Equal(TrioFigureKind.Yod, figure.Kind);
        Assert.Equal(2, figure.Focus);
    }

    [Fact]
    public void Slow_planets_cannot_make_up_a_figure_on_their_own()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Pluto, 10)), Chart("Ben", (Planet.Neptune, 12)), Chart("Cara", (Planet.Mars, 14)));
        Assert.Empty(trio.Figures);
    }

    [Fact]
    public void The_figures_are_not_looked_for_in_a_search()
    {
        var trio = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)), Chart("Ben", (Planet.Venus, 12)), Chart("Cara", (Planet.Mars, 14)), figures: false);
        Assert.Empty(trio.Figures);
    }

    [Fact]
    public void A_reading_describes_its_figures_and_who_stands_where()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Moon, 100)),
            Chart("Ben", (Planet.Venus, 100), (Planet.Saturn, 190)),
            Chart("Cara", (Planet.Mars, 190))));
        var figures = reading.Sections.Single(s => s.Heading == "Other figures through all three charts");
        Assert.Contains(figures.Items, i => i.Title!.StartsWith("T-square") && i.Text.Contains("Ben"));
        string text = string.Join(" ", reading.Sections.SelectMany(s => s.Items).Select(i => $"{i.Title} {i.Meta} {i.Text}"));
        Assert.DoesNotContain("{", text);
    }

    [Fact]
    public void A_reading_says_what_none_of_the_three_has()
    {
        var reading = Interpreter.ComposeTrio(TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10), (Planet.Mercury, 15), (Planet.Venus, 20), (Planet.Mars, 25)),
            Chart("Ben", (Planet.Sun, 100)),
            Chart("Cara", (Planet.Mars, 190))));
        var brings = reading.Sections.Single(s => s.Heading == "What the three bring between them").Items;
        Assert.Contains(brings, i => i.Title == "Anna brings the Fire");
        Assert.Contains(brings, i => i.Title == "No Earth");
    }

    [Fact]
    public void The_person_the_others_both_get_on_with_is_named_and_a_close_trio_names_nobody()
    {
        var even = TrioService.Compare(
            Chart("Anna", (Planet.Sun, 10)), Chart("Ben", (Planet.Sun, 130)), Chart("Cara", (Planet.Sun, 250)));
        Assert.Null(even.Hub);
        Assert.Null(even.Outsider);
    }

    // ── As lived ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_answers_can_move_a_trio_and_its_charts_tone_stays()
    {
        var mirror = new MirrorInterpreter(Repo.Data("mirror.json"));
        var inventory = new InventoryService(Repo.Data("inventory.json"));
        NatalChart a = Figure("albert-einstein"), b = Figure("marilyn-monroe"), c = Figure("elvis-presley");
        var plain = TrioService.Compare(a, b, c);
        Assert.Null(plain.LivedTone);
        Assert.All(plain.Pairs, p => Assert.Null(p.AsLived));

        // Answering "very accurate" to everything marks most planets one way or the other.
        var profile = inventory.Score(Enumerable.Repeat(5, 120).ToArray(), "Someone", new DateOnly(2026, 10, 31));
        var answers = mirror.Compose(a, profile);
        var lived = TrioService.Compare(a, b, c, [answers, null, null]);
        Assert.Equal(plain.Tone, lived.Tone);
        Assert.NotNull(lived.LivedTone);
        Assert.NotNull(lived.Pairs[0].AsLived);
        Assert.Null(lived.Pairs[2].AsLived);                  // the pair it has no part in
        Assert.Equal(answers, lived.Answers[0]);

        // Answers given to the wrong person are left out.
        Assert.Null(TrioService.Compare(a, b, c, [null, answers, null]).LivedTone);
    }
}
