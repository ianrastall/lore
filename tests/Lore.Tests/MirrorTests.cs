using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// The reading that sets a person's inventory profile beside their chart: which facets
// speak for which planet, how their pattern picks an expression, and how that is set
// against the planet's dignity in the chart.
public sealed class MirrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly InventoryService Inventory = new(Repo.Data("inventory.json"));
    private static readonly MirrorInterpreter Mirror =
        new(Repo.Data("mirror.json"), new ChartInterpreter(Repo.Data("interpretations.json")));
    private static readonly DateOnly Day = new(2026, 10, 10);

    private static NatalChart Chart(string id = "elvis-presley") => Repo.Charts.Calculate(Repo.Figure(id));

    // Answers that score each named facet as high (20) or low (4) and every other 12.
    private static InventoryProfile Profile(params (string Facet, bool High)[] set)
    {
        var answers = Enumerable.Repeat(3, 120).ToArray();
        foreach (var item in Inventory.Instrument.Items)
            foreach (var (facet, high) in set)
                if (item.Facet == facet) answers[item.N - 1] = high != item.Reversed ? 5 : 1;
        return Inventory.Score(answers, "Someone", Day);
    }

    private static MirrorPlanet Planet(MirrorReading r, Planet planet) => r.Planets.Single(p => p.Planet == planet);

    // ── The corpus ────────────────────────────────────────────────────────────

    [Fact]
    public void Every_planet_read_has_dials_of_real_facets_and_an_expression_for_every_pattern()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var facets = Inventory.Instrument.Facets.Select(f => f.Key).ToHashSet();
        string[] seven = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn"];
        var planets = doc.RootElement.GetProperty("planets");
        Assert.Equal(seven.Append("Neptune"), planets.EnumerateObject().Select(p => p.Name));

        var used = new List<string>();
        foreach (var planet in planets.EnumerateObject())
        {
            var dials = planet.Value.GetProperty("dials").EnumerateObject().ToList();
            Assert.NotEmpty(dials);
            foreach (var dial in dials)
                foreach (var key in dial.Value.EnumerateArray().Select(k => k.GetString()!.TrimStart('-')))
                {
                    Assert.Contains(key, facets);
                    used.Add(key);
                }

            var expressions = planet.Value.GetProperty("expressions").EnumerateArray().ToList();
            // The last expression takes whatever the others did not, so none is left unread.
            Assert.Empty(expressions[^1].GetProperty("when").EnumerateObject());
            Assert.All(expressions, e =>
            {
                Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("name").GetString()));
                Assert.True(e.GetProperty("text").GetString()!.Length > 80);
                Assert.Contains(e.GetProperty("lived").GetString(), new[] { "well", "strain", "plain" });
                Assert.All(e.GetProperty("when").EnumerateObject(), w =>
                {
                    Assert.Contains(w.Name, dials.Select(d => d.Name));
                    Assert.Contains(w.Value.GetString(), new[] { "low", "mid", "high" });
                });
            });
            Assert.Equal(expressions.Count, expressions.Select(e => e.GetProperty("name").GetString()).Distinct().Count());

            // The seven the chart scores have both paragraphs for where the two accounts differ.
            if (seven.Contains(planet.Name))
            {
                Assert.True(planet.Value.GetProperty("unlived").GetString()!.Length > 80, planet.Name);
                Assert.True(planet.Value.GetProperty("hardWon").GetString()!.Length > 80, planet.Name);
            }
        }
        // No facet speaks for two planets.
        Assert.Equal(used.Count, used.Distinct().Count());
        Assert.True(Mirror.IsAvailable);
    }

    // ── How a planet is being lived ───────────────────────────────────────────

    [Fact]
    public void The_pattern_of_a_planets_facets_picks_its_expression()
    {
        var chart = Chart();
        string Mars(params (string, bool)[] set) => Planet(Mirror.Compose(chart, Profile(set)), Models.Planet.Mars).Expression;

        Assert.Equal("measured", Mars());
        Assert.Equal("direct", Mars(("E3", true), ("E4", true), ("E5", true)));
        Assert.Equal("combative", Mars(("E3", true), ("E4", true), ("E5", true), ("N2", true)));
        Assert.Equal("held in", Mars(("E3", false), ("E4", false), ("E5", false), ("N2", true)));
        Assert.Equal("muted", Mars(("E3", false), ("E4", false), ("E5", false), ("N2", false)));
        Assert.Equal("short-fused", Mars(("N2", true)));

        // A facet marked with a minus counts the other way up: modesty lowers the Sun's confidence.
        var modest = Mirror.Compose(chart, Profile(("A5", true), ("C1", false), ("C4", false), ("E6", true)));
        Assert.Equal("unassuming", Planet(modest, Models.Planet.Sun).Expression);
        var proud = Mirror.Compose(chart, Profile(("A5", false), ("C1", true), ("C4", true), ("E6", true)));
        Assert.Equal("in full light", Planet(proud, Models.Planet.Sun).Expression);
    }

    [Fact]
    public void Each_planet_is_given_with_the_charts_account_and_then_the_answers()
    {
        var chart = Chart();
        var reading = Mirror.Compose(chart, Profile(("E3", false), ("E4", false), ("E5", false), ("N2", true)));

        Assert.Equal(8, reading.Planets.Count);
        var lived = reading.Sections.Single(s => s.Heading == "How each planet is being lived").Items;
        Assert.Equal(8, lived.Count);

        var mars = lived.Single(i => i.Title == "♂ Mars: held in");
        Assert.Equal("Assertiveness very low  ·  Activity Level very low  ·  Excitement-Seeking very low  ·  Anger very high", mars.Meta);
        Assert.StartsWith("The chart: Mars in Libra, 10th house, the planet of drive, anger and assertion. ", mars.Text);
        Assert.Contains("\nThe answers: The answers describe anger that is felt strongly and seldom acted on", mars.Text);
        // The Report's own words for Mars in Libra stand between the two.
        var report = new ChartInterpreter(Repo.Data("interpretations.json")).PlanetInSign(chart.GetPlanet(Models.Planet.Mars)!);
        Assert.Contains(report, mars.Text);

        // The reading says what it is and what it is not, before anything else.
        var opening = reading.Sections[0].Items.Select(i => i.Text).ToList();
        Assert.Contains(opening, t => t.Contains("The chart is not changed"));
        Assert.Contains(opening, t => t.Contains("No research connects a birth chart with measured personality"));
        Assert.DoesNotContain(reading.Sections.SelectMany(s => s.Items), i => i.Text.Contains('{'));
    }

    // ── Where the two accounts differ ─────────────────────────────────────────

    [Fact]
    public void An_easy_placement_lived_with_strain_is_unlived_and_a_hard_one_lived_well_is_hard_won()
    {
        // Elvis Presley: Saturn in Aquarius, its own sign, is well placed; his Sun, in the
        // second house and close to nothing that helps it, is not.
        var chart = Chart();
        var score = DignityService.Compute(chart);
        int saturn = score.Planets.Single(p => p.Planet == Models.Planet.Saturn).Total;
        Assert.True(saturn >= MirrorInterpreter.EasyAt);

        // Saturn easy in the chart; the answers describe no structure and much self-consciousness.
        var strained = Mirror.Compose(chart, Profile(("C5", false), ("C3", false), ("C2", false), ("C6", false), ("N4", true)));
        var s = Planet(strained, Models.Planet.Saturn);
        Assert.Equal("exposed", s.Expression);
        Assert.Equal(MirrorExpectation.Easy, s.Expectation);
        Assert.Equal(MirrorVerdict.Unlived, s.Verdict);
        var item = strained.Sections.Single(x => x.Heading == "Where the chart and the answers part company")
            .Items.Single(i => i.Title == "♄ Saturn: unlived");
        Assert.StartsWith($"In the chart it works easily (dignity +{saturn}: ", item.Meta);
        Assert.EndsWith("in the answers, exposed", item.Meta);
        Assert.StartsWith("The chart gives Saturn an easy place", item.Text);

        // The same Saturn, lived with discipline and ease: as the chart has it.
        var well = Mirror.Compose(chart, Profile(("C5", true), ("C3", true), ("C2", true), ("C6", true)));
        Assert.Equal(MirrorVerdict.AsWritten, Planet(well, Models.Planet.Saturn).Verdict);
        Assert.Contains(well.Sections.Single(x => x.Heading == "Where the chart and the answers part company").Items,
            i => i.Title == "As the chart has it" && i.Meta!.Contains("♄ Saturn"));

        // Every planet of every timed figure falls in exactly one of the five cases.
        foreach (var f in Repo.Figures.Where(f => f.BirthTimeKnown).Take(60))
        {
            var reading = Mirror.Compose(Repo.Charts.Calculate(f), Profile(("N2", true), ("E6", true), ("C5", false)));
            Assert.All(reading.Planets.Where(p => p.Planet != Models.Planet.Neptune), p =>
            {
                Assert.NotEqual(MirrorExpectation.Unknown, p.Expectation);
                Assert.Equal(p.Dignity >= MirrorInterpreter.EasyAt, p.Expectation == MirrorExpectation.Easy);
                Assert.Equal(p.Dignity <= MirrorInterpreter.StrainedAt, p.Expectation == MirrorExpectation.Strained);
            });
            Assert.Equal(4, reading.Sections.Count);
        }
    }

    [Fact]
    public void A_hard_placement_lived_well_is_hard_won()
    {
        // Find a figure whose Mars is badly placed, and answer as someone whose drive is direct.
        var direct = Profile(("E3", true), ("E4", true), ("E5", true));
        var chart = Repo.Figures.Where(f => f.BirthTimeKnown).Select(f => Repo.Charts.Calculate(f))
            .First(c => DignityService.Compute(c).Planets.Single(p => p.Planet == Models.Planet.Mars).Total <= MirrorInterpreter.StrainedAt);

        var reading = Mirror.Compose(chart, direct);
        Assert.Equal(MirrorVerdict.HardWon, Planet(reading, Models.Planet.Mars).Verdict);
        var item = reading.Sections.Single(x => x.Heading == "Where the chart and the answers part company")
            .Items.Single(i => i.Title == "♂ Mars: hard-won");
        Assert.StartsWith("In the chart it works with strain (dignity −", item.Meta);
        Assert.StartsWith("The chart gives Mars a difficult place", item.Text);
    }

    [Fact]
    public void Without_a_birth_time_only_the_first_part_is_given()
    {
        var reading = Mirror.Compose(Repo.Charts.Calculate(Demo.Person(timed: false)), Profile(("N2", true)));
        Assert.DoesNotContain(reading.Sections, s => s.Heading == "Where the chart and the answers part company");
        Assert.Contains(reading.Sections[0].Items, i => i.Text.StartsWith("This chart has no birth time"));
        Assert.All(reading.Planets, p => Assert.Equal(MirrorExpectation.Unknown, p.Expectation));
        Assert.All(reading.Sections.Single(s => s.Heading == "How each planet is being lived").Items,
            i => Assert.DoesNotContain(" house", i.Text.Split('\n')[0]));
    }

    [Fact]
    public void Different_answers_give_a_different_reading_of_the_same_chart()
    {
        var chart = Chart();
        var one = Mirror.Compose(chart, Profile(("N1", true), ("N3", true), ("N6", true), ("E3", true), ("E4", true)));
        var other = Mirror.Compose(chart, Profile(("N1", false), ("N3", false), ("N6", false), ("E3", false), ("E4", false)));
        var a = one.Planets.ToDictionary(p => p.Planet, p => p.Expression);
        var b = other.Planets.ToDictionary(p => p.Planet, p => p.Expression);
        Assert.NotEqual(a[Models.Planet.Moon], b[Models.Planet.Moon]);
        Assert.NotEqual(a[Models.Planet.Mars], b[Models.Planet.Mars]);
        Assert.Equal(a[Models.Planet.Venus], b[Models.Planet.Venus]); // nothing about Venus was answered differently

        string text = System.Text.Encoding.UTF8.GetString(MirrorInterpreter.ToText(one));
        Assert.Contains("Elvis Presley — The chart and the answers", text);
        Assert.Contains("HOW EACH PLANET IS BEING LIVED", text);
    }

    // ── Shaping the other readings ────────────────────────────────────────────

    private static readonly ChartInterpreter Report = new(Repo.Data("interpretations.json"));

    [Fact]
    public void Every_expression_but_the_middling_ones_has_a_line_for_the_Report_and_each_scored_planet_lines_for_the_day()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        foreach (var planet in doc.RootElement.GetProperty("planets").EnumerateObject())
        {
            var expressions = planet.Value.GetProperty("expressions").EnumerateArray().ToList();
            foreach (var e in expressions)
            {
                bool middling = !e.GetProperty("when").EnumerateObject().Any();
                bool has = e.TryGetProperty("report", out var line) && !string.IsNullOrWhiteSpace(line.GetString());
                // Where the answers are ordinary the Report is left exactly as it was.
                Assert.True(has != middling, $"{planet.Name}/{e.GetProperty("name").GetString()}");
                if (has) Assert.StartsWith("By {name}'s answers", line.GetString());
            }

            // A line for the day wherever an expression counts as lived well or with strain.
            var lived = expressions.Select(e => e.GetProperty("lived").GetString()!).Where(l => l != "plain").Distinct().ToList();
            if (lived.Count == 0)
            {
                Assert.False(planet.Value.TryGetProperty("daily", out _), planet.Name);
                continue;
            }
            var daily = planet.Value.GetProperty("daily");
            Assert.Equal(lived.Order(), daily.EnumerateObject().Select(d => d.Name).Order());
            Assert.All(daily.EnumerateObject(), d =>
                Assert.All(new[] { "hard", "easy" }, kind => Assert.StartsWith("By your answers", d.Value.GetProperty(kind).GetString())));
        }
    }

    [Fact]
    public void The_Report_gains_a_sentence_for_each_planet_the_answers_speak_of_and_says_so_at_the_end()
    {
        var chart = Chart();
        var plain = Report.Interpret(chart);
        var answers = Mirror.Compose(chart, Profile(("E3", false), ("E4", false), ("E5", false), ("N2", true)));
        var shaped = Report.Interpret(chart, null, answers);

        string Line(IReadOnlyList<ReportSection> report, string planet) =>
            report.Single(s => s.Heading == "The Planets").Paragraphs.Single(p => p.Contains($" {planet} in "));

        // Mars is "held in": its paragraph is the one the Report always had, and one sentence more.
        Assert.Equal(Line(plain, "Mars") + " By Elvis's answers this drive is felt more than it is used: the anger is there and the assertion is not.",
            Line(shaped, "Mars"));
        // Uranus is not read, and stays word for word.
        Assert.Equal(Line(plain, "Uranus"), Line(shaped, "Uranus"));
        // Nothing else in the Report moves.
        foreach (string heading in new[] { "Overview", "Major Aspects", "Chart Patterns" })
            Assert.Equal(plain.Single(s => s.Heading == heading).Paragraphs, shaped.Single(s => s.Heading == heading).Paragraphs);

        var closing = shaped[^1];
        Assert.Equal("In the Light of the Answers", closing.Heading);
        Assert.StartsWith("Elvis answered Lore's personality inventory on 10 October 2026.", closing.Paragraphs[0]);
        Assert.DoesNotContain(plain, s => s.Heading == "In the Light of the Answers");
        Assert.DoesNotContain(shaped.SelectMany(s => s.Paragraphs), p => p.Contains('{'));
    }

    [Fact]
    public void A_planet_whose_placement_and_answers_disagree_is_named_at_the_end_of_the_Report()
    {
        var chart = Chart();
        var answers = Mirror.Compose(chart, Profile(("C5", false), ("C3", false), ("C2", false), ("C6", false), ("N4", true)));
        var closing = Report.Interpret(chart, null, answers)[^1].Paragraphs;
        Assert.Contains(closing, p => p.StartsWith("♄ Saturn, unlived: The chart gives Saturn an easy place"));
    }

    [Fact]
    public void One_persons_answers_never_colour_another_persons_Report_or_day()
    {
        var elvis = Chart();
        var marilyn = Chart("marilyn-monroe");
        var answers = Mirror.Compose(elvis, Profile(("N1", true), ("N3", true), ("N6", true), ("N2", true)));

        Assert.Equal(Report.Interpret(marilyn).SelectMany(s => s.Paragraphs),
            Report.Interpret(marilyn, null, answers).SelectMany(s => s.Paragraphs));

        var daily = new DailyInterpreter(Repo.Data("daily.json"));
        var transits = new TransitService(Repo.Charts);
        var date = new DateOnly(2026, 10, 2);
        var zone = NodaTime.DateTimeZone.Utc;
        string Text(MirrorReading? a) => string.Join("|", daily.Compose(marilyn, transits.Scan(marilyn, date, zone), zone, a)
            .Sections.SelectMany(s => s.Items).Select(i => i.Text));
        Assert.Equal(Text(null), Text(answers));
    }

    [Fact]
    public void A_transit_to_a_planet_lived_with_strain_or_lived_well_gains_a_line_in_the_Daily_and_the_Forecast()
    {
        var chart = Chart();
        var daily = new DailyInterpreter(Repo.Data("daily.json"));
        var transits = new TransitService(Repo.Charts);
        var zone = NodaTime.DateTimeZone.Utc;
        var start = new DateOnly(2026, 10, 1);

        // Every one of the seven lived with strain or well, so that any transit to one of them shows it.
        var answers = Mirror.Compose(chart, Profile(
            ("C1", false), ("C4", false), ("A5", true), ("E6", false),          // Sun dimmed
            ("N1", true), ("N3", true), ("N6", true),                            // Moon uneasy
            ("O5", true),                                                        // Mercury inquiring
            ("E1", false), ("E2", false), ("A1", false), ("A4", false),          // Venus guarded
            ("E3", true), ("E4", true), ("E5", true), ("N2", true),              // Mars combative
            ("O4", true), ("A3", true), ("N5", true),                            // Jupiter lavish
            ("C5", true), ("C3", true), ("C2", true), ("C6", true)));            // Saturn structured
        Assert.All(answers.Planets.Where(p => p.Planet != Models.Planet.Neptune), p => Assert.NotEqual(MirrorLived.Plain, p.Lived));

        var passes = transits.Forecast(chart, start, 91, zone);
        var plain = daily.ComposeForecast(chart, passes, start, 91, zone);
        var shaped = daily.ComposeForecast(chart, passes, start, 91, zone, null, answers);
        var before = plain.Sections.SelectMany(s => s.Items).ToList();
        var after = shaped.Sections.SelectMany(s => s.Items).ToList();
        Assert.Equal(before.Select(i => i.Title), after.Select(i => i.Title));

        string[] read = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn"];
        int changed = 0;
        foreach (var (was, now) in before.Zip(after))
        {
            bool toRead = read.Any(planet => was.Title is { } t && t.EndsWith($"your natal {Symbol(planet)} {planet}"));
            if (toRead)
            {
                Assert.StartsWith(was.Text + " By your answers", now.Text);
                changed++;
            }
            else
                Assert.Equal(was.Text, now.Text);   // transits to anything else read as they did
        }
        Assert.True(changed > 5);

        // A square to Mars, lived combatively, takes the hard line; a trine the easy one.
        var mars = answers.Planets.Single(p => p.Planet == Models.Planet.Mars);
        Assert.StartsWith("By your answers your temper is quick", mars.DailyLine(TransitTone.Tension));
        Assert.StartsWith("By your answers your energy does not always", mars.DailyLine(TransitTone.Flow));
        Assert.Equal(mars.DailyHard, mars.DailyLine(TransitTone.Conjunction));   // under strain, a conjunction is hard
        var saturn = answers.Planets.Single(p => p.Planet == Models.Planet.Saturn);
        Assert.Equal(saturn.DailyEasy, saturn.DailyLine(TransitTone.Conjunction)); // lived well, it is easy

        // The same in a day's reading: nothing is added, removed or reordered, only lengthened.
        for (int day = 0; day < 20; day++)
        {
            var date = start.AddDays(day);
            var a = daily.Compose(chart, transits.Scan(chart, date, zone), zone).Sections.SelectMany(s => s.Items).ToList();
            var b = daily.Compose(chart, transits.Scan(chart, date, zone), zone, answers).Sections.SelectMany(s => s.Items).ToList();
            Assert.Equal(a.Select(i => i.Title), b.Select(i => i.Title));
            Assert.All(a.Zip(b), pair => Assert.True(pair.Second.Text == pair.First.Text || pair.Second.Text.StartsWith(pair.First.Text + " By your answers")));
        }
    }

    private static string Symbol(string planet) => Enum.GetValues<Planet>().Single(p => p.Name() == planet).Symbol();

    // ── In the view ───────────────────────────────────────────────────────────

    [Fact]
    public void The_view_makes_the_reading_with_the_profile_and_again_when_the_chart_is_recalculated()
    {
        static Celebrity Person() => new()
        {
            Id = "user-1", Name = "Ann Example", Category = UserChartService.MyChartsCategory,
            BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
            BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55, TimeZoneId = "Europe/London",
        };
        var store = new InventoryStore(_dir);
        store.Save("user-1", [new InventorySitting { Started = "2026-10-10", Completed = "2026-10-10", Answers = Enumerable.Repeat(4, 120).ToArray() }]);

        var vm = new InventoryViewModel(Inventory, store, Mirror) { Chart = Repo.Charts.Calculate(Person()) };
        Assert.Equal(InventoryStage.Profile, vm.Stage);
        Assert.True(vm.HasReading);
        Assert.True(vm.ProfileShown);
        Assert.False(vm.ReadingShown);

        vm.ShowReading = true;
        Assert.True(vm.ReadingShown);
        Assert.False(vm.ProfileShown);
        Assert.Equal("The chart and the answers", vm.ReadingSections[0].Heading);
        Assert.NotEmpty(vm.ReadingText());

        // Whole-sign houses move the planets to other houses: the reading follows the chart.
        var before = vm.Reading;
        vm.Chart = new ChartService(Repo.Ephemeris) { Settings = new ChartSettings(HouseSystem.WholeSign) }.Calculate(Person());
        Assert.NotSame(before, vm.Reading);
        Assert.True(vm.ReadingShown);

        // No reading while answering again, nor for someone with no answers.
        vm.TakeAgainCommand.Execute(null);
        Assert.Null(vm.Reading);
        Assert.False(vm.ReadingShown);
        vm.AbandonCommand.Execute(null);
        Assert.NotNull(vm.Reading);
        vm.DeleteAnswers();
        Assert.Null(vm.Reading);
    }
}
