using System.Text.Json;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;
using NodaTime;

namespace Lore.Tests;

// The self-report beyond the personality inventory: the character scales and the life
// areas, and what the answers now reach: the four bodies the inventory could not read, the
// houses, the chart's patterns, Timing, a day's choice of transits, and the two- and
// three-person readings, where they change the scoring as well as the words.
public sealed class SelfReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly InventoryService Inventory = new(Repo.Data("inventory.json"));
    private static readonly CharacterService Character = new(Repo.Data("character.json"));
    private static readonly LifeAreaService Life = new(Repo.Data("lifeareas.json"));
    private static readonly ChartInterpreter Report = new(Repo.Data("interpretations.json"));
    private static readonly MirrorInterpreter Mirror = new(Repo.Data("mirror.json"), Report);
    private static readonly SynastryInterpreter Synastry = new(Repo.Data("synastry.json"));
    private static readonly DailyInterpreter Daily = new(Repo.Data("daily.json"));
    private static readonly TransitService Transits = new(Repo.Charts);
    private static readonly DateTimeZone Utc = DateTimeZone.Utc;
    private static readonly DateOnly Day = new(2026, 10, 31);

    private static NatalChart Chart(string id = "elvis-presley") => Repo.Charts.Calculate(Repo.Figure(id));

    // Everyone at the middle of the scale but the scales named, given as average answers.
    private static CharacterProfile Scales(params (string Key, double Mean)[] set)
    {
        var given = set.ToDictionary(s => s.Key, s => s.Mean);
        var q = Character.Instrument;
        return new CharacterProfile
        {
            Name = "Someone",
            TakenOn = Day,
            Dimensions = q.Dimensions.Select(d =>
            {
                var own = q.Scales.Where(s => s.Dimension == d.Key).Select(s => new CharacterScore(s, given.GetValueOrDefault(s.Key, 3.5))).ToList();
                return new CharacterDimensionScore(d, own.Average(s => s.Mean), own);
            }).ToList(),
        };
    }

    private static InventoryProfile Facets(params (string Key, double Z)[] set)
    {
        var z = set.ToDictionary(s => s.Key, s => s.Z);
        var q = Inventory.Instrument;
        return new InventoryProfile
        {
            Name = "Someone",
            TakenOn = Day,
            Domains = q.Domains.Select(d =>
            {
                var facets = q.Facets.Where(f => f.Domain == d.Key).Select(f => new FacetScore(f, 12, z.GetValueOrDefault(f.Key))).ToList();
                return new DomainScore(d, d.Mean, facets.Average(f => f.Z), facets);
            }).ToList(),
        };
    }

    private static LifeAreasReport Areas(DateOnly taken, params (int House, int Rating)[] set)
    {
        var given = set.ToDictionary(s => s.House, s => s.Rating);
        return Life.Score(Enumerable.Range(1, 12).Select(h => given.GetValueOrDefault(h, 3)).ToArray(), taken);
    }

    private static MirrorPlanet Planet(MirrorReading r, Planet planet) => r.Planets.Single(p => p.Planet == planet);

    // Marks most of the personal planets as strained, or as lived well.
    private static InventoryProfile Strained() => Facets(
        ("E3", 1.5), ("E4", 1.5), ("E5", 1.5), ("N2", 1.5),      // Mars combative
        ("N1", 1.5), ("N3", 1.5), ("N6", 1.5),                   // Moon uneasy
        ("E1", -1.5), ("E2", -1.5), ("A1", -1.5), ("A4", -1.5),  // Venus guarded
        ("C1", -1.5), ("C4", -1.5), ("A5", 1.5), ("E6", -1.5));  // Sun dimmed

    private static InventoryProfile Steady() => Facets(
        ("E3", 1.5), ("E4", 1.5), ("E5", 1.5),                   // Mars direct
        ("C1", 1.5), ("C4", 1.5), ("A5", -1.5), ("E6", 1.5),     // Sun bright
        ("N1", -1.5), ("N3", -1.5), ("N6", -1.5));               // Moon settled

    // ── The instruments ───────────────────────────────────────────────────────

    [Fact]
    public void The_character_scales_are_whole_and_scored_with_their_reversed_statements()
    {
        Assert.True(Character.IsAvailable);
        Assert.Equal(96, Character.Count);
        Assert.Equal(["SD", "CO", "ST"], Character.Instrument.Dimensions.Select(d => d.Key));
        Assert.All(Character.Instrument.Scales, s =>
        {
            Assert.InRange(s.Alpha, 0.6, 0.9);
            foreach (string line in new[] { s.Measures, s.Low, s.Typical, s.High }) Assert.False(string.IsNullOrWhiteSpace(line));
        });

        // Agreeing with everything: the statements keyed the other way count against.
        var agree = Character.Score(Enumerable.Repeat(5, 96).ToArray(), "Ann", Day);
        var satisfaction = agree.Dimensions[0].Scales.Single(s => s.Scale.Key == "SD1");
        Assert.Equal((1 * 5 + 9 * 1) / 10.0, satisfaction.Mean, 6);
        Assert.Equal(ScoreBand.VeryLow, satisfaction.Band);
        var empathy = agree.Dimensions[1].Scales.Single(s => s.Scale.Key == "CO2");
        Assert.Equal(5, empathy.Mean, 6);
        Assert.Equal(ScoreBand.VeryHigh, empathy.Band);

        Assert.Throws<InvalidOperationException>(() => Character.Score(new int[96], "Ann", Day));
        Assert.Contains("Generated by Lore", System.Text.Encoding.UTF8.GetString(Character.ToText(agree)));
    }

    [Fact]
    public void A_character_score_is_placed_on_its_own_scale_and_says_so()
    {
        Assert.Equal(ScoreBand.Typical, new CharacterScore(new CharacterScale(), 3.0).Band);
        Assert.Equal(ScoreBand.Low, new CharacterScore(new CharacterScale(), 2.9).Band);
        Assert.Equal(ScoreBand.Typical, new CharacterScore(new CharacterScale(), 4.0).Band);
        Assert.Equal(ScoreBand.High, new CharacterScore(new CharacterScale(), 4.1).Band);
        Assert.Contains("no reference group", Character.Instrument.Instrument.Norms, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_damaged_character_or_life_area_file_offers_nothing()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "broken.json");
        File.WriteAllText(path, "{\"items\": []}");
        Assert.False(new CharacterService(path).IsAvailable);
        Assert.False(new LifeAreaService(path).IsAvailable);
        File.WriteAllText(path, "not json");
        Assert.False(new CharacterService(path).IsAvailable);
        Assert.False(new LifeAreaService(path).IsAvailable);
    }

    [Fact]
    public void The_life_areas_are_one_to_a_house_and_only_the_ends_of_the_scale_count()
    {
        Assert.True(Life.IsAvailable);
        Assert.Equal(Enumerable.Range(1, 12), Life.Instrument.Areas.Select(a => a.House));

        var report = Areas(Day, (7, 1), (10, 2), (2, 5), (4, 4));
        Assert.Equal([2, 7, 10], report.Ratings.Where(r => r.IsMarked).Select(r => r.Area.House));
        Assert.True(report.Ratings[6].IsLow);
        Assert.True(report.Ratings[1].IsHigh);
        Assert.False(report.Ratings[3].IsMarked);
        Assert.Equal("Going well", report.Ratings[3].Choice);
    }

    // ── The reading ───────────────────────────────────────────────────────────

    [Fact]
    public void The_character_answers_read_the_four_bodies_the_inventory_cannot()
    {
        var chart = Chart();
        var personality = Mirror.Compose(chart, Facets());
        Assert.DoesNotContain(personality.Planets, p => p.Planet is Models.Planet.Pluto or Models.Planet.Chiron or Models.Planet.NorthNode or Models.Planet.Lilith);

        var both = Mirror.Compose(chart, new SelfReport { Personality = Facets(), Character = Scales(("CO4", 2.5), ("SD5", 2.5), ("SD2", 4.5), ("ST2", 4.5)) });
        Assert.Equal("consumed", Planet(both, Models.Planet.Pluto).Expression);
        Assert.Equal(MirrorLived.Strain, Planet(both, Models.Planet.Pluto).Lived);
        Assert.Equal("on its way", Planet(both, Models.Planet.NorthNode).Expression);
        Assert.StartsWith("By Elvis's answers grudges hold", Planet(both, Models.Planet.Pluto).ReportLine);
        Assert.Contains(Report.Interpret(chart, null, both).Single(s => s.Heading == "The Planets").Paragraphs,
            p => p.Contains("Pluto in") && p.Contains("By Elvis's answers grudges hold"));
    }

    [Fact]
    public void The_character_answers_alone_make_a_reading_of_what_they_speak_of()
    {
        var reading = Mirror.Compose(Chart(), new SelfReport { Character = Scales(("SD4", 2.5)) });
        Assert.Equal([Models.Planet.Pluto, Models.Planet.NorthNode, Models.Planet.Chiron, Models.Planet.Lilith], reading.Planets.Select(p => p.Planet));
        Assert.Equal("", reading.Summary);
        Assert.Contains(reading.Sections, s => s.Heading == "How far it has been grown into");
        Assert.DoesNotContain(reading.Sections, s => s.Heading == "Where the chart and the answers part company");
    }

    [Fact]
    public void How_far_a_planet_has_been_grown_into_follows_its_scales()
    {
        var chart = Chart();
        var low = Mirror.Compose(chart, new SelfReport { Personality = Facets(), Character = Scales(("SD5", 2.5), ("CO4", 2.5)) });
        Assert.Equal(MirrorReach.Lower, Planet(low, Models.Planet.Mars).Reach);
        Assert.Contains("lower side", Planet(low, Models.Planet.Mars).ReachText);
        Assert.Equal(MirrorReach.Unmarked, Planet(low, Models.Planet.Sun).Reach);

        var high = Mirror.Compose(chart, new SelfReport { Personality = Facets(), Character = Scales(("SD5", 4.5), ("CO4", 4.5)) });
        Assert.Equal(MirrorReach.Higher, Planet(high, Models.Planet.Mars).Reach);

        // With no character answers nothing is said either way.
        Assert.All(Mirror.Compose(chart, Facets()).Planets, p => Assert.Equal(MirrorReach.Unmarked, p.Reach));

        var report = Report.Interpret(chart, null, low).Single(s => s.Heading == "In the Light of the Answers").Paragraphs;
        Assert.Contains(report, p => p.StartsWith("♂ Mars: The character answers show this Mars on its lower side"));
    }

    [Fact]
    public void A_single_facet_at_the_end_of_the_profile_is_spoken_of_where_its_dial_hides_it()
    {
        var chart = Chart();
        // Anxiety is very high and low mood rather low: the Moon's dial averages to the ordinary.
        var reading = Mirror.Compose(chart, Facets(("N1", 2.4), ("N3", -1)));
        var moon = Planet(reading, Models.Planet.Moon);
        Assert.True(moon.IsOrdinary);
        Assert.Single(moon.Extremes);
        Assert.Contains("anxiety runs very high", moon.ReportLine);

        // An expression that already rests on the dial says it, and it is not said twice.
        var marked = Mirror.Compose(chart, Facets(("N1", 2.4), ("N3", 2.4), ("N6", 2.4)));
        Assert.False(Planet(marked, Models.Planet.Moon).IsOrdinary);
        Assert.Empty(Planet(marked, Models.Planet.Moon).Extremes);

        Assert.All(Mirror.Compose(chart, Facets()).Planets, p => Assert.Empty(p.Extremes));
    }

    [Fact]
    public void A_planet_the_answers_mark_out_is_read_in_its_house()
    {
        var chart = Chart();
        var reading = Mirror.Compose(chart, Strained());
        var mars = Planet(reading, Models.Planet.Mars);
        Assert.NotEmpty(mars.HouseLine);
        Assert.Contains(mars.HouseLine, mars.ReportLine);
        Assert.Empty(Planet(Mirror.Compose(chart, Facets()), Models.Planet.Mars).HouseLine);

        // Without a birth time there is no house.
        var untimed = Mirror.Compose(Repo.Charts.Calculate(Demo.Person(timed: false)), Strained());
        Assert.All(untimed.Planets, p => Assert.Empty(p.HouseLine));
    }

    [Fact]
    public void Every_house_every_extreme_and_every_pattern_has_its_words()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var planets = doc.RootElement.GetProperty("planets");
        string[] seven = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn"];
        foreach (string name in seven)
            for (int house = 1; house <= 12; house++)
                foreach (string lived in new[] { "strain", "well" })
                    Assert.True(planets.GetProperty(name).GetProperty("houses").GetProperty(house.ToString()).GetProperty(lived).GetString()!.Length > 10, $"{name} {house} {lived}");

        var scales = Character.Instrument.Scales.Select(s => s.Key).ToList();
        foreach (var planet in planets.EnumerateObject())
        {
            Assert.All(planet.Value.GetProperty("reach").EnumerateArray(), k => Assert.Contains(k.GetString()!, scales));
            Assert.Contains("higher side", planet.Value.GetProperty("higher").GetString());
            Assert.Contains("lower side", planet.Value.GetProperty("lower").GetString());
        }

        var extremes = doc.RootElement.GetProperty("extremes");
        Assert.Equal(Inventory.Instrument.Facets.Select(f => f.Key).Order(), extremes.EnumerateObject().Select(e => e.Name).Order());
        foreach (var e in extremes.EnumerateObject())
            foreach (string end in new[] { "high", "low" })
                Assert.StartsWith("By {name}'s answers", e.Value.GetProperty(end).GetString());

        var patterns = doc.RootElement.GetProperty("patterns");
        foreach (string type in Enum.GetNames<PatternType>())
            foreach (string lived in new[] { "strain", "well" })
                Assert.Contains("{points}", patterns.GetProperty(type).GetProperty(lived).GetString());
    }

    [Fact]
    public void Across_a_population_every_character_expression_is_met_and_nothing_is_left_unfilled()
    {
        var random = new Random(20261031);
        double Answer() => Math.Clamp(3.5 + (random.NextDouble() + random.NextDouble() - 1) * 2.2, 1, 5);
        var chart = Chart();
        var met = new Dictionary<string, int>();
        const int People = 2000;
        for (int n = 0; n < People; n++)
        {
            var profile = Scales(Character.Instrument.Scales.Select(s => (s.Key, Answer())).ToArray());
            var reading = Mirror.Compose(chart, new SelfReport { Character = profile, Personality = Facets() });
            foreach (var p in reading.Planets.Where(p => p.Planet >= Models.Planet.Pluto))
                met[$"{p.Planet.Name()}/{p.Expression}"] = met.GetValueOrDefault($"{p.Planet.Name()}/{p.Expression}") + 1;
            if (n % 100 == 0)
                Assert.All(reading.Sections.SelectMany(s => s.Items), i => Assert.DoesNotContain("{", i.Text));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        foreach (string body in new[] { "Pluto", "North Node", "Chiron", "Lilith" })
            foreach (var e in doc.RootElement.GetProperty("planets").GetProperty(body).GetProperty("expressions").EnumerateArray())
            {
                string id = $"{body}/{e.GetProperty("name").GetString()}";
                Assert.True(met.GetValueOrDefault(id) >= People / 500, $"{id} is met by {met.GetValueOrDefault(id)}");
            }
    }

    [Fact]
    public void What_has_moved_between_sittings_is_more_than_a_days_mood()
    {
        var chart = Chart();
        var report = new SelfReport
        {
            Personality = Facets(("N2", 1.5), ("E1", 0.5)),
            EarlierPersonality = Facets(("N2", -0.5), ("E1", 0)),
            Character = Scales(("SD2", 4.2), ("SD5", 3.7)),
            EarlierCharacter = Scales(("SD2", 3.2), ("SD5", 3.5)),
        };
        var moved = Mirror.Compose(chart, report).Sections.Single(s => s.Heading == "What has moved").Items;
        Assert.Contains("Anger: up", moved[0].Text);
        Assert.DoesNotContain("Friendliness", moved[0].Text);
        Assert.Contains("Optimism: up", moved[1].Text);
        Assert.DoesNotContain("Impulse Control", moved[1].Text);

        Assert.DoesNotContain(Mirror.Compose(chart, Facets()).Sections, s => s.Heading == "What has moved");
    }

    [Fact]
    public void Life_areas_reach_the_day_the_forecast_and_the_year_and_go_stale()
    {
        var chart = Chart();
        var fresh = Mirror.Compose(chart, new SelfReport { LifeAreas = Areas(Day, (7, 1), (2, 5)) }, today: Day);
        Assert.Equal([2, 7], fresh.Houses.Keys.Order());
        Assert.Contains("presses on that", fresh.LifeLine(7, TransitTone.Tension));
        Assert.Contains("some help", fresh.LifeLine(7, TransitTone.Flow));
        Assert.Contains("adds to it", fresh.LifeLine(2, TransitTone.Flow));
        Assert.Contains("press on it", fresh.LifeForecastLine(7, TransitTone.Tension));
        Assert.Contains("partnership", fresh.LifeForecastLine(7, TransitTone.Tension));
        Assert.Contains("ground of the year", fresh.LifeYear(7));
        Assert.Equal("", fresh.LifeYear(1));

        // Six months on, the ratings are shown and no longer used.
        var stale = Mirror.Compose(chart, new SelfReport { LifeAreas = Areas(Day, (7, 1)) }, today: Day.AddDays(LifeAreasReport.FreshDays + 1));
        Assert.Empty(stale.Houses);
        Assert.Contains(stale.Sections.SelectMany(s => s.Items), i => i.Text.Contains("more than six months old"));

        // A chart with no birth time has no houses to put them in.
        var untimed = Mirror.Compose(Repo.Charts.Calculate(Demo.Person(timed: false)), new SelfReport { LifeAreas = Areas(Day, (7, 1)) }, today: Day);
        Assert.Empty(untimed.Houses);
        Assert.Contains(untimed.Sections.SelectMany(s => s.Items), i => i.Text.Contains("no birth time"));
    }

    [Fact]
    public void A_transit_to_a_planet_in_a_troubled_house_says_so_once()
    {
        var chart = Chart();
        var answers = Mirror.Compose(chart, new SelfReport { LifeAreas = Areas(Day, Enumerable.Range(1, 12).Select(h => (h, 1)).ToArray()) }, today: Day);
        int said = 0;
        for (int day = 0; day < 40; day++)
        {
            var reading = Daily.Compose(chart, Transits.Scan(chart, Day.AddDays(day), Utc), Utc, answers);
            var lines = reading.Sections.SelectMany(s => s.Items).Select(i => i.Text).Where(t => t.Contains("You have said")).ToList();
            said += lines.Count;
            Assert.Equal(lines.Count, lines.Distinct().Count());
        }
        Assert.True(said > 5, $"only {said}");
    }

    [Fact]
    public void Marked_planets_change_what_a_day_spends_its_slots_on()
    {
        var chart = Chart();
        var answers = Mirror.Compose(chart, Strained());
        int differ = 0;
        for (int day = 0; day < 120; day++)
        {
            var sky = Transits.Scan(chart, Day.AddDays(day), Utc);
            var plain = Daily.Compose(chart, sky, Utc).Events.Where(e => e.Shown).Select(e => e.Target.Name + e.Mover).ToHashSet();
            var lived = Daily.Compose(chart, Transits.Scan(chart, Day.AddDays(day), Utc), Utc, answers).Events.Where(e => e.Shown).Select(e => e.Target.Name + e.Mover).ToHashSet();
            if (!plain.SetEquals(lived)) differ++;
        }
        Assert.True(differ > 3, $"only {differ} days differed");
    }

    [Fact]
    public void Timing_speaks_of_the_lord_the_year_and_the_planets_in_contact()
    {
        var chart = Chart();
        var timing = new TimingService(Repo.Charts);
        var profected = timing.Profect(chart, Day)!;
        var report = new SelfReport { Personality = Strained(), LifeAreas = Areas(Day, (profected.House, 1)) };
        var answers = Mirror.Compose(chart, report, today: Day);

        var reading = timing.Compose(chart, Day, Utc, answers: answers);
        string Lord() => reading.Sections.Single(s => s.Heading.StartsWith("Annual profection")).Items[^1].Text;
        Assert.Contains("ground of the year", Lord());

        // Someone else's answers change nothing.
        var other = Mirror.Compose(Chart("marilyn-monroe"), report, today: Day);
        var ignored = timing.Compose(chart, Day, Utc, answers: other);
        Assert.Equal(timing.Compose(chart, Day, Utc).Sections.SelectMany(s => s.Items).Select(i => i.Text),
            ignored.Sections.SelectMany(s => s.Items).Select(i => i.Text));
    }

    [Fact]
    public void A_pattern_most_of_whose_planets_are_marked_alike_gains_a_sentence()
    {
        // A library figure with a T-square or the like, marked strained throughout.
        var all = Inventory.Instrument.Facets.ToDictionary(f => f.Key, f => f.Key[0] == 'N' ? 1.8 : -1.8);
        var profile = Facets(all.Select(x => (x.Key, x.Value)).ToArray());
        foreach (var figure in Repo.Figures.Take(60))
        {
            var chart = Repo.Charts.Calculate(figure);
            var reading = Mirror.Compose(chart, profile);
            if (reading.PatternLines.Count == 0) continue;
            string line = reading.PatternLines.Values.First();
            Assert.DoesNotContain("{", line);
            var patterns = Report.Interpret(chart, null, reading).Single(s => s.Heading == "Chart Patterns").Paragraphs;
            Assert.Contains(patterns, p => p.EndsWith(line));
            return;
        }
        Assert.Fail("no chart among the first sixty had a marked pattern");
    }

    // ── Two people ────────────────────────────────────────────────────────────

    [Fact]
    public void The_answers_move_the_balance_of_a_pair_and_the_charts_own_stays()
    {
        NatalChart a = Chart(), b = Chart("marilyn-monroe");
        var s = SynastryService.Compare(a, b);
        var plain = SynastryScoring.Assess(s.Aspects);
        Assert.Equal(plain, SynastryScoring.Assess(s.Aspects, null, null));

        var hard = Mirror.Compose(a, Strained());
        var easy = Mirror.Compose(a, Steady());
        double strained = SynastryScoring.Assess(s.Aspects, hard, null).Share, steady = SynastryScoring.Assess(s.Aspects, easy, null).Share;
        Assert.True(strained < plain.Share, $"{strained} against {plain.Share}");
        Assert.True(steady > plain.Share, $"{steady} against {plain.Share}");

        // Ordinary answers mark nothing, so they change nothing.
        Assert.Equal(plain.Share, SynastryScoring.Assess(s.Aspects, Mirror.Compose(a, Facets()), null).Share, 9);

        var reading = Synastry.Compose(s, hard, null);
        Assert.Equal(plain.Tone, reading.Tone);
        Assert.Equal(plain.Share, reading.Compatibility.Share, 9);
        Assert.NotNull(reading.AsLived);
        Assert.Contains(reading.Sections[0].Items, i => i.Title == "By the answers");
        Assert.Null(Synastry.Compose(s).AsLived);
    }

    // The weights are meant to let the answers move a pair by a band and not across the
    // scale, over a population whose answers look like people's.
    [Fact]
    public void The_answers_move_a_pair_by_a_band_and_not_across_the_scale()
    {
        var random = new Random(20261031);
        double Normal() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        InventoryProfile Someone()
        {
            var q = Inventory.Instrument;
            var trait = q.Domains.ToDictionary(d => d.Key, _ => Normal());
            return Facets(q.Facets.Select(f => (f.Key, 0.65 * trait[f.Domain] + 0.76 * Normal())).ToArray());
        }

        var charts = Repo.Figures.Where((_, i) => i % 7 == 0).Take(120).Select(f => Repo.Charts.Calculate(f)).ToList();
        int[] moved = new int[5];
        for (int n = 0; n < 800; n++)
        {
            int i = random.Next(charts.Count), j = random.Next(charts.Count - 1);
            if (j >= i) j++;
            var contacts = SynastryService.Compare(charts[i], charts[j]).Aspects;
            var lived = SynastryScoring.Assess(contacts, Mirror.Compose(charts[i], Someone()), Mirror.Compose(charts[j], Someone()));
            moved[Math.Abs(SynastryScoring.Assess(contacts).Level - lived.Level)]++;
        }
        Assert.True(moved[0] >= 800 * 0.55, string.Join(",", moved));   // most stay where they are
        Assert.True(moved[1] >= 800 * 0.10, string.Join(",", moved));   // and the answers do matter
        Assert.True(moved[2] + moved[3] + moved[4] <= 800 * 0.02, string.Join(",", moved));
    }

    [Fact]
    public void Someone_elses_answers_are_not_read_into_a_pair()
    {
        NatalChart a = Chart(), b = Chart("marilyn-monroe"), c = Chart("albert-einstein");
        var s = SynastryService.Compare(a, b);
        var wrong = Mirror.Compose(c, Strained());
        Assert.Null(Synastry.Compose(s, wrong, wrong).AsLived);
    }

    [Fact]
    public void Contacts_and_the_two_sets_of_answers_are_spoken_of_when_both_have_answered()
    {
        NatalChart a = Chart(), b = Chart("marilyn-monroe");
        var first = Mirror.Compose(a, Strained());
        var second = Mirror.Compose(b, Facets(("N2", 1.5), ("E3", 1.5), ("E4", 1.5), ("E5", 1.5)));
        var reading = Synastry.Compose(SynastryService.Compare(a, b), first, second);

        var both = reading.Sections.Single(s => s.Heading == "The two sets of answers");
        Assert.Contains(both.Items, i => i.Title == "Two tempers");
        var text = reading.Sections.SelectMany(s => s.Items).Select(i => $"{i.Title} {i.Meta} {i.Text}").ToList();
        Assert.All(text, t => Assert.DoesNotContain("{", t));
        Assert.Contains(text, t => t.Contains("By the answers Elvis's") || t.Contains("By the answers Marilyn's"));

        // One person's answers alone do not make that section.
        Assert.DoesNotContain(Synastry.Compose(SynastryService.Compare(a, b), first, null).Sections, s => s.Heading == "The two sets of answers");
    }

    [Fact]
    public void The_rules_for_two_sets_of_answers_name_dials_that_exist()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("synastry.json")));
        using var mirror = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var dials = mirror.RootElement.GetProperty("planets").EnumerateObject()
            .SelectMany(p => p.Value.GetProperty("dials").EnumerateObject().Select(d => $"{p.Name}.{d.Name}")).ToHashSet();
        foreach (var rule in doc.RootElement.GetProperty("livedPairs").EnumerateArray())
            foreach (var when in rule.GetProperty("when").EnumerateArray())
            {
                var parts = when.GetString()!.Split('=');
                Assert.Contains(parts[0], dials);
                Assert.Contains(parts[1], new[] { "low", "mid", "high" });
            }
        foreach (var tone in doc.RootElement.GetProperty("livedContacts").EnumerateObject())
            foreach (string key in new[] { "strain|strain", "strain|well", "well|well", "strain|plain", "well|plain" })
                Assert.StartsWith("By the answers", tone.Value.GetProperty(key).GetString());
    }

    [Fact]
    public void A_search_is_ranked_as_lived_for_someone_with_answers()
    {
        var people = Repo.Figures.Take(40).ToList();
        var chart = Chart();
        var plain = SynastryScoring.Rank(Repo.Charts, chart, people);
        var hard = Mirror.Compose(chart, Strained());
        var lived = SynastryScoring.Rank(Repo.Charts, chart, people, answersOf: c => c.Celebrity.Id == chart.Celebrity.Id ? hard : null);
        Assert.Equal(plain.Count, lived.Count);
        Assert.True(lived.Average(m => m.Compatibility.Share) < plain.Average(m => m.Compatibility.Share));
    }

    // ── In the view ───────────────────────────────────────────────────────────

    private static Celebrity Person(string id = "user-1", string name = "Ann Example") => new()
    {
        Id = id, Name = name, Category = UserChartService.MyChartsCategory,
        BirthDate = "1980-05-17", BirthTime = "14:20", BirthTimeKnown = true,
        BirthPlace = "Leeds, United Kingdom", Latitude = 53.8, Longitude = -1.55, TimeZoneId = "Europe/London",
    };

    private InventoryViewModel Model(InventoryStore store) =>
        new(Inventory, store, Mirror, Character, Life) { Chart = Repo.Charts.Calculate(Person()) };

    private static void AnswerAll(InventoryViewModel vm, int answer = 4)
    {
        for (int guard = 0; guard < 40; guard++)
        {
            foreach (var q in vm.Questions) q.AnswerIndex = answer - 1;
            if (vm.IsLastPage) break;
            vm.NextCommand.Execute(null);
        }
        vm.FinishCommand.Execute(null);
    }

    [Fact]
    public void The_three_questionnaires_are_kept_apart_and_all_reach_the_reading()
    {
        var store = new InventoryStore(_dir);
        var vm = Model(store);
        Assert.Equal(InventoryStage.Introduction, vm.Stage);
        Assert.Null(vm.Reading);

        vm.Part = InventoryPart.LifeAreas;
        Assert.Equal("Ann Example — Life areas", vm.Title);
        vm.BeginCommand.Execute(null);
        Assert.Equal(12, vm.Questions.Count);
        AnswerAll(vm, 1);
        Assert.Equal(InventoryStage.Profile, vm.Stage);
        Assert.True(vm.LifeAreasShown);
        Assert.Equal(12, vm.LifeRatings.Count);
        Assert.NotNull(vm.Reading);     // the life areas alone make a reading
        Assert.Equal(12, vm.Reading!.Houses.Count);

        // The reading is of all three, and says so whichever part it was opened from.
        vm.ShowReading = true;
        Assert.Equal("Ann Example — Chart and answers", vm.Title);
        vm.ShowReading = false;
        Assert.Equal("Ann Example — Life areas", vm.Title);

        vm.Part = InventoryPart.Character;
        Assert.Equal(InventoryStage.Introduction, vm.Stage);
        Assert.NotNull(vm.Reading);     // going to another part keeps it
        vm.BeginCommand.Execute(null);
        Assert.Equal(InventoryViewModel.PageSize, vm.Questions.Count);
        AnswerAll(vm, 5);
        Assert.True(vm.CharacterShown);
        Assert.Contains(vm.Reading!.Planets, p => p.Planet == Models.Planet.Pluto);

        vm.Part = InventoryPart.Personality;
        Assert.Equal(InventoryStage.Introduction, vm.Stage);
        vm.BeginCommand.Execute(null);
        Assert.Null(vm.Reading);        // answering the main one again takes it away, as ever
        AnswerAll(vm);
        Assert.True(vm.ProfileShown);
        Assert.Contains(vm.Reading!.Planets, p => p.Planet == Models.Planet.Sun);
        Assert.Contains(vm.Reading.Planets, p => p.Planet == Models.Planet.Pluto);

        Assert.True(store.Flush());
        var later = new InventoryStore(_dir);
        Assert.Single(later.Get("user-1"));
        Assert.Single(later.Get(SelfReportService.StoreKey("user-1", InventoryPart.Character)));
        Assert.Single(later.Get(SelfReportService.StoreKey("user-1", InventoryPart.LifeAreas)));
    }

    [Fact]
    public async Task Deleting_a_persons_answers_takes_all_three()
    {
        var store = new InventoryStore(_dir);
        var vm = Model(store);
        vm.Part = InventoryPart.LifeAreas;
        vm.BeginCommand.Execute(null);
        AnswerAll(vm);
        vm.Part = InventoryPart.Character;
        vm.BeginCommand.Execute(null);
        AnswerAll(vm);
        Assert.NotNull(vm.Reading);

        Assert.True(await vm.DeleteAnswersAsync());
        Assert.Null(vm.Reading);
        Assert.Empty(store.Get(SelfReportService.StoreKey("user-1", InventoryPart.Character)));
        Assert.Empty(store.Get(SelfReportService.StoreKey("user-1", InventoryPart.LifeAreas)));
        Assert.Empty(store.Get("user-1"));
    }

    [Fact]
    public void Older_answers_with_nothing_but_the_personality_inventory_still_read()
    {
        var store = new InventoryStore(_dir);
        store.Save("user-1", [new InventorySitting { Started = "2026-10-10", Completed = "2026-10-10", Answers = Enumerable.Repeat(4, 120).ToArray() }]);
        var vm = Model(store);
        Assert.Equal(InventoryStage.Profile, vm.Stage);
        Assert.True(vm.ProfileShown);
        Assert.NotNull(vm.Reading);
        Assert.DoesNotContain(vm.Reading!.Planets, p => p.Planet == Models.Planet.Pluto);
    }

    [Fact]
    public void A_second_persons_answers_can_be_read_for_the_views_of_two_and_three()
    {
        var store = new InventoryStore(_dir);
        store.Save("user-2", [new InventorySitting { Started = "2026-10-10", Completed = "2026-10-10", Answers = Enumerable.Repeat(5, 120).ToArray() }]);
        var vm = Model(store);
        var other = Repo.Charts.Calculate(Person("user-2", "Ben Other"));
        Assert.True(vm.ReadingFor(other)?.IsFor(other));
        Assert.Null(vm.ReadingFor(Chart()));   // a bundled figure has none
    }

    [Fact]
    public void A_finder_can_look_among_one_category()
    {
        var vm = new SynastryViewModel();
        vm.SetPeople([Person(), Repo.Figure("elvis-presley"), Repo.Figure("albert-einstein")]);
        Assert.Equal(SynastryViewModel.EveryScope, vm.FinderScopes[0]);
        Assert.Contains(UserChartService.MyChartsCategory, vm.FinderScopes);
        Assert.Contains("Musician", vm.FinderScopes);
        Assert.Equal(vm.FinderScopes.Count, vm.FinderScopes.Distinct().Count());
        Assert.Equal(SynastryViewModel.EveryScope, vm.SelectedScope);

        // The box's list is not replaced by the same list, which would empty the box.
        var scopes = vm.FinderScopes;
        vm.SetPeople([Person(), Repo.Figure("elvis-presley"), Repo.Figure("albert-einstein")]);
        Assert.Same(scopes, vm.FinderScopes);

        // A box emptying itself is not a choice.
        vm.SelectedScope = "Musician";
        vm.SelectedScope = null;
        Assert.Equal("Musician", vm.FinderScope);

        // A category that has gone takes the choice with it.
        vm.SetPeople([Person(), Repo.Figure("albert-einstein")]);
        Assert.Equal(SynastryViewModel.EveryScope, vm.FinderScope);
        Assert.Equal(SynastryViewModel.EveryScope, vm.SelectedScope);
    }

    [Fact]
    public async Task A_list_scored_with_the_answers_says_so()
    {
        var chart = Repo.Charts.Calculate(Person());
        var vm = new SynastryViewModel(Repo.Charts, Synastry) { Chart = chart };
        vm.SetPeople([.. Repo.Figures.Take(5)]);
        for (int i = 0; vm.IsSearching || vm.Matches.Count == 0; i++)
        {
            Assert.True(i < 200, "the search did not finish");
            await Task.Delay(50);
        }
        Assert.DoesNotContain("answers", vm.MatchesSummary);

        vm.Answers = Mirror.Compose(chart, Strained());
        for (int i = 0; vm.IsSearching || vm.Matches.Count == 0; i++)
        {
            Assert.True(i < 200, "the search did not finish");
            await Task.Delay(50);
        }
        Assert.Contains("taking the answers into account", vm.MatchesSummary);
    }

    [Fact]
    public void One_trio_is_not_called_trios()
    {
        var vm = new SynastryViewModel { Chart = Repo.Charts.Calculate(Person()) };
        vm.Trios = [new TrioFind(Person("a", "Ben"), Person("b", "Cara"), TrioTone.Mixed, 0, 0.5, 1)];
        Assert.Contains("Tried 1 trio from", vm.FinderSummary);
    }
}
