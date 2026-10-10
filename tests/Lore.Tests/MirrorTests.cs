using System.Text.Json;
using Lore.Models;
using Lore.Services;
using Lore.ViewModels;

namespace Lore.Tests;

// Everything that depends on a person's answers to the personality inventory: which
// facets speak for which planet, how their pattern picks an expression, how that is set
// against the planet's sign and its dignity, and what the Report, the Daily and the
// Forecast take from it.
public sealed class MirrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly InventoryService Inventory = new(Repo.Data("inventory.json"));
    private static readonly ChartInterpreter Report = new(Repo.Data("interpretations.json"));
    private static readonly MirrorInterpreter Mirror = new(Repo.Data("mirror.json"), Report);
    private static readonly DateOnly Day = new(2026, 10, 10);

    // Elvis Presley: Sun, Mercury and Venus in Capricorn, Moon in Pisces, Mars in Libra,
    // Jupiter in Scorpio, Saturn in Aquarius, Sagittarius rising.
    private static NatalChart Chart(string id = "elvis-presley") => Repo.Charts.Calculate(Repo.Figure(id));

    // A profile given directly as standard deviations from the reference mean: nought for
    // every facet and trait but those named. (A trait is otherwise the mean of its facets.)
    private static InventoryProfile Profile(params (string Key, double Z)[] set) =>
        Profile(set.ToDictionary(s => s.Key, s => s.Z));

    private static InventoryProfile Profile(IReadOnlyDictionary<string, double> z)
    {
        var q = Inventory.Instrument;
        return new InventoryProfile
        {
            Name = "Someone",
            TakenOn = Day,
            Domains = q.Domains.Select(d =>
            {
                var facets = q.Facets.Where(f => f.Domain == d.Key).Select(f =>
                {
                    double fz = z.GetValueOrDefault(f.Key);
                    return new FacetScore(f, (int)Math.Round(f.Mean + fz * f.Sd), fz);
                }).ToList();
                double dz = z.TryGetValue(d.Key, out double given) ? given : facets.Average(f => f.Z);
                return new DomainScore(d, d.Mean + dz * d.Sd, dz, facets);
            }).ToList(),
        };
    }

    private const double Hi = 1.5, Lo = -1.5;

    private static MirrorPlanet Planet(MirrorReading r, Planet planet) => r.Planets.Single(p => p.Planet == planet);

    private static string Para(IReadOnlyList<ReportSection> report, string heading, string containing) =>
        report.Single(s => s.Heading == heading).Paragraphs.Single(p => p.Contains(containing));

    // ── The corpus ────────────────────────────────────────────────────────────

    private static readonly string[] Levels = ["low", "mid", "high"];
    private static readonly string[] Seven = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn"];

    [Fact]
    public void Every_planet_read_has_dials_of_real_facets_and_an_expression_for_every_pattern()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var facets = Inventory.Instrument.Facets.Select(f => f.Key).ToHashSet();
        var planets = doc.RootElement.GetProperty("planets");
        Assert.Equal(Seven.Concat(["Uranus", "Neptune"]), planets.EnumerateObject().Select(p => p.Name));

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
            Assert.Equal(expressions.Count, expressions.Select(e => e.GetProperty("name").GetString()).Distinct().Count());
            for (int i = 0; i < expressions.Count; i++)
            {
                var e = expressions[i];
                string id = $"{planet.Name}/{e.GetProperty("name").GetString()}";
                bool catchAll = !e.GetProperty("when").EnumerateObject().Any();
                // The last expression, and only the last, takes whatever the others did not.
                Assert.True(catchAll == (i == expressions.Count - 1), id);
                Assert.True(e.GetProperty("text").GetString()!.Length > 80, id);
                Assert.Contains(e.GetProperty("lived").GetString(), new[] { "well", "strain", "plain" });
                Assert.All(e.GetProperty("when").EnumerateObject(), w =>
                {
                    Assert.Contains(w.Name, dials.Select(d => d.Name));
                    Assert.Contains(w.Value.GetString(), Levels);
                });

                // Ordinary answers add nothing to the Report or the day; anything else does.
                bool report = e.TryGetProperty("report", out var line);
                Assert.True(report != catchAll, id);
                if (report) Assert.StartsWith("By {name}'s answers", line.GetString());
                bool daily = e.TryGetProperty("hard", out var hard) & e.TryGetProperty("easy", out var easy);
                Assert.True(daily == (!catchAll && Seven.Contains(planet.Name)), id);
                if (daily)
                {
                    Assert.StartsWith("By your answers", hard.GetString());
                    Assert.StartsWith("By your answers", easy.GetString());
                }
            }

            if (Seven.Contains(planet.Name))
            {
                Assert.True(planet.Value.GetProperty("unlived").GetString()!.Length > 80, planet.Name);
                Assert.True(planet.Value.GetProperty("hardWon").GetString()!.Length > 80, planet.Name);
            }
        }

        // Every one of the thirty facets speaks for a planet, and none for two.
        Assert.Equal(used.Count, used.Distinct().Count());
        Assert.Equal(facets.Order(), used.Order());
        Assert.True(Mirror.IsAvailable);
    }

    [Fact]
    public void Every_sign_says_what_to_expect_of_every_dial_of_the_seven_planets_and_of_the_Ascendant()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var signs = Enum.GetValues<ZodiacSign>().Select(s => s.Name()).ToList();
        foreach (var planet in doc.RootElement.GetProperty("planets").EnumerateObject())
        {
            bool has = planet.Value.TryGetProperty("signs", out var table);
            // Uranus and Neptune stay years in a sign: it says nothing of one person.
            Assert.Equal(Seven.Contains(planet.Name), has);
            if (!has) continue;

            var dials = planet.Value.GetProperty("dials").EnumerateObject().Select(d => d.Name).Order().ToList();
            Assert.Equal(signs, table.EnumerateObject().Select(s => s.Name));
            foreach (var sign in table.EnumerateObject())
            {
                Assert.Equal(dials, sign.Value.EnumerateObject().Select(d => d.Name).Order());
                Assert.All(sign.Value.EnumerateObject(), d => Assert.Contains(d.Value.GetString(), Levels));
            }
            // No dial is expected the same in every sign: the sign has to matter.
            foreach (string dial in dials)
                Assert.True(table.EnumerateObject().Select(s => s.Value.GetProperty(dial).GetString()).Distinct().Count() == 3, $"{planet.Name}/{dial}");

            var nouns = planet.Value.GetProperty("nouns");
            Assert.All(dials, d =>
            {
                Assert.False(string.IsNullOrWhiteSpace(nouns.GetProperty(d).GetProperty("high").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(nouns.GetProperty(d).GetProperty("low").GetString()));
            });
        }

        var rising = doc.RootElement.GetProperty("rising");
        Assert.Equal(signs, rising.GetProperty("signs").EnumerateObject().Select(s => s.Name));
        Assert.Equal(["E"], rising.GetProperty("dial").EnumerateArray().Select(k => k.GetString()));
    }

    [Fact]
    public void Aspects_balance_and_forecast_have_a_sentence_for_every_case()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        var facets = Inventory.Instrument.Facets.Select(f => f.Key).ToHashSet();
        string[] lived = ["strain", "well", "plain"];

        var aspects = doc.RootElement.GetProperty("aspects");
        Assert.Equal(new[] { "Conjunction", "Flow", "Tension" }, aspects.EnumerateObject().Select(t => t.Name).Order());
        foreach (var tone in aspects.EnumerateObject())
            foreach (string a in lived)
                foreach (string b in lived)
                {
                    string line = tone.Value.GetProperty($"{a}|{b}").GetString()!;
                    Assert.StartsWith("By {name}'s answers", line);
                    foreach (string placeholder in new[] { "{a}", "{ea}", "{b}", "{eb}" }) Assert.Contains(placeholder, line);
                }

        foreach (var (section, names) in new[] { ("elements", Enum.GetNames<Element>()), ("modalities", Enum.GetNames<Modality>()) })
        {
            var entries = doc.RootElement.GetProperty(section);
            Assert.Equal(names.Order(), entries.EnumerateObject().Select(e => e.Name).Order());
            foreach (var entry in entries.EnumerateObject())
            {
                Assert.All(entry.Value.GetProperty("dial").EnumerateArray(), k => Assert.Contains(k.GetString()!.TrimStart('-'), facets));
                foreach (string which in new[] { "strongHigh", "strongLow", "weakHigh", "weakLow" })
                    Assert.StartsWith("By {name}'s answers", entry.Value.GetProperty(which).GetString());
            }
        }

        var forecast = doc.RootElement.GetProperty("forecast");
        foreach (string how in new[] { "strain", "well" })
            foreach (string kind in new[] { "hard", "easy" })
                Assert.StartsWith("By your answers your natal {planet} is {expression}.", forecast.GetProperty(how).GetProperty(kind).GetString());
    }

    // ── How a planet is being lived ───────────────────────────────────────────

    [Fact]
    public void The_pattern_of_a_planets_facets_picks_its_expression()
    {
        var chart = Chart();
        string Mars(params (string, double)[] set) => Planet(Mirror.Compose(chart, Profile(set)), Models.Planet.Mars).Expression;

        Assert.Equal("measured", Mars());
        Assert.Equal("direct", Mars(("E3", Hi), ("E4", Hi), ("E5", Hi)));
        Assert.Equal("combative", Mars(("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Hi)));
        Assert.Equal("held in", Mars(("E3", Lo), ("E4", Lo), ("E5", Lo), ("N2", Hi)));
        Assert.Equal("muted", Mars(("E3", Lo), ("E4", Lo), ("E5", Lo)));
        Assert.Equal("short-fused", Mars(("N2", Hi)));
        Assert.Equal("even-tempered", Mars(("N2", Lo)));

        // Three facets averaging over half a standard deviation are enough for a dial; one
        // facet on its own must be a whole one out, as the profile's own bands are.
        Assert.Equal("direct", Mars(("E3", 0.6), ("E4", 0.6), ("E5", 0.6)));
        Assert.Equal("measured", Mars(("N2", 0.9)));

        // A facet marked with a minus counts the other way up: modesty lowers the Sun's confidence.
        var modest = Mirror.Compose(chart, Profile(("A5", Hi), ("C1", Lo), ("C4", Lo)));
        Assert.Equal("unassuming", Planet(modest, Models.Planet.Sun).Expression);
        var proud = Mirror.Compose(chart, Profile(("A5", Lo), ("C1", Hi), ("C4", Hi), ("E6", Hi)));
        Assert.Equal("in full light", Planet(proud, Models.Planet.Sun).Expression);

        // Mercury has two sides, and Venus a third that adds a sentence of its own.
        var reading = Mirror.Compose(chart, Profile(("O5", Hi), ("A2", Lo), ("O2", Hi)));
        Assert.Equal("subtle", Planet(reading, Models.Planet.Mercury).Expression);
        Assert.Equal("moderate", Planet(reading, Models.Planet.Venus).Expression);
        Assert.EndsWith("The answers also describe a strong response to art and to natural beauty: they are noticed, sought out and felt.",
            Planet(reading, Models.Planet.Venus).Text);
        Assert.Equal("traditional", Planet(Mirror.Compose(chart, Profile(("O6", Lo))), Models.Planet.Uranus).Expression);
    }

    // ── Against the sign ──────────────────────────────────────────────────────

    [Fact]
    public void Each_dial_is_set_against_what_the_planets_sign_leads_one_to_expect()
    {
        // Mars in Libra: the sign suggests little push and little temper.
        var chart = Chart();
        var direct = Planet(Mirror.Compose(chart, Profile(("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Lo))), Models.Planet.Mars);

        var push = direct.Comparisons.Single(c => c.Dial == "push");
        Assert.Equal(("low", "high", 2), (push.Expected, push.Actual, push.Gap));
        Assert.Equal("far more push than Mars in Libra would suggest", push.Clause);
        var heat = direct.Comparisons.Single(c => c.Dial == "heat");
        Assert.Equal(0, heat.Gap);
        Assert.Equal("the coolness that Mars in Libra describes", heat.Clause);
        Assert.Equal("Against the sign: far more push than Mars in Libra would suggest; the coolness that Mars in Libra describes.", direct.AgainstSign);

        // Ordinary answers still sit somewhere against the sign, and the reading says where.
        var ordinary = Planet(Mirror.Compose(chart, Profile()), Models.Planet.Mars);
        Assert.Equal(new[] { 1, 1 }, ordinary.Comparisons.Select(c => c.Gap));
        Assert.Equal("more push than Mars in Libra would suggest", ordinary.Comparisons[0].Clause);

        // The same answers against another sign read differently: a Mars in Aries expects both.
        var aries = Repo.Figures.Where(f => f.BirthTimeKnown).Select(f => Repo.Charts.Calculate(f))
            .First(c => c.GetPlanet(Models.Planet.Mars)!.Sign == ZodiacSign.Aries);
        var bornOut = Planet(Mirror.Compose(aries, Profile(("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Hi))), Models.Planet.Mars);
        Assert.Equal(new[] { "the push that Mars in Aries describes", "the temper that Mars in Aries describes" },
            bornOut.Comparisons.Select(c => c.Clause));
        var short_ = Planet(Mirror.Compose(aries, Profile(("E3", Lo), ("E4", Lo), ("E5", Lo))), Models.Planet.Mars);
        Assert.Equal("far less push than Mars in Aries would suggest", short_.Comparisons[0].Clause);

        // Uranus and Neptune are read without their sign.
        var reading = Mirror.Compose(chart, Profile(("O6", Hi), ("O1", Hi)));
        Assert.Empty(Planet(reading, Models.Planet.Uranus).Comparisons);
        Assert.Equal("", Planet(reading, Models.Planet.Neptune).AgainstSign);
    }

    // ── The Chart and answers reading ─────────────────────────────────────────

    [Fact]
    public void The_reading_gives_each_planet_the_charts_account_the_answers_and_the_sign()
    {
        var chart = Chart();
        var reading = Mirror.Compose(chart, Profile(("E3", Lo), ("E4", Lo), ("E5", Lo), ("N2", Hi)));

        Assert.Equal(9, reading.Planets.Count);
        var lived = reading.Sections.Single(s => s.Heading == "How each planet is being lived").Items;
        Assert.Equal(9, lived.Count);

        var mars = lived.Single(i => i.Title == "♂ Mars: held in");
        Assert.Equal("Assertiveness low  ·  Activity Level low  ·  Excitement-Seeking low  ·  Anger high", mars.Meta);
        var lines = mars.Text.Split('\n');
        Assert.StartsWith("The chart: Mars in Libra, 10th house, the planet of drive, anger and assertion. ", lines[0]);
        Assert.Contains(Report.PlanetInSign(chart.GetPlanet(Models.Planet.Mars)!), lines[0]);
        Assert.StartsWith("The answers: The answers describe anger that is felt strongly and seldom acted on", lines[1]);
        Assert.Equal("Against the sign: the restraint that Mars in Libra describes; far more temper than Mars in Libra would suggest.", lines[2]);

        // It says what it is and what it is not, before anything else.
        var opening = reading.Sections[0].Items.Select(i => i.Text).ToList();
        Assert.Contains(opening, t => t.Contains("The chart is not changed"));
        Assert.Contains(opening, t => t.Contains("No research connects a birth chart with measured personality"));
        Assert.Contains(opening, t => t.Contains("Uranus and Neptune stay years in a sign"));
        Assert.DoesNotContain(reading.Sections.SelectMany(s => s.Items), i => i.Text.Contains('{'));

        string text = System.Text.Encoding.UTF8.GetString(MirrorInterpreter.ToText(reading));
        Assert.Contains("Elvis Presley — The chart and the answers", text);
        Assert.Contains("HOW EACH PLANET IS BEING LIVED", text);
    }

    [Fact]
    public void An_easy_placement_lived_with_strain_is_unlived_and_a_hard_one_lived_well_is_hard_won()
    {
        // Elvis Presley's Saturn, in Aquarius, its own sign, is well placed.
        var chart = Chart();
        int saturn = DignityService.Compute(chart).Planets.Single(p => p.Planet == Models.Planet.Saturn).Total;
        Assert.True(saturn >= MirrorInterpreter.EasyAt);

        var strained = Mirror.Compose(chart, Profile(("C5", Lo), ("C3", Lo), ("C2", Lo), ("C6", Lo), ("N4", Hi)));
        var s = Planet(strained, Models.Planet.Saturn);
        Assert.Equal(("exposed", MirrorExpectation.Easy, MirrorVerdict.Unlived), (s.Expression, s.Expectation, s.Verdict));
        var item = strained.Sections.Single(x => x.Heading == "Where the chart and the answers part company")
            .Items.Single(i => i.Title == "♄ Saturn: unlived");
        Assert.StartsWith($"In the chart it works easily (dignity +{saturn}: ", item.Meta);
        Assert.EndsWith("in the answers, exposed", item.Meta);
        Assert.StartsWith("The chart gives Saturn an easy place", item.Text);
        Assert.Contains(strained.ReportParagraphs, p => p.StartsWith("♄ Saturn, unlived: The chart gives Saturn an easy place"));

        var well = Mirror.Compose(chart, Profile(("C5", Hi), ("C3", Hi), ("C2", Hi), ("C6", Hi)));
        Assert.Equal(MirrorVerdict.AsWritten, Planet(well, Models.Planet.Saturn).Verdict);
        Assert.Contains(well.Sections.Single(x => x.Heading == "Where the chart and the answers part company").Items,
            i => i.Title == "As the chart has it" && i.Meta!.Contains("♄ Saturn"));

        // A badly placed Mars, answered as someone whose drive is direct.
        var hard = Repo.Figures.Where(f => f.BirthTimeKnown).Select(f => Repo.Charts.Calculate(f))
            .First(c => DignityService.Compute(c).Planets.Single(p => p.Planet == Models.Planet.Mars).Total <= MirrorInterpreter.StrainedAt);
        var won = Mirror.Compose(hard, Profile(("E3", Hi), ("E4", Hi), ("E5", Hi)));
        Assert.Equal(MirrorVerdict.HardWon, Planet(won, Models.Planet.Mars).Verdict);
        var wonItem = won.Sections.Single(x => x.Heading == "Where the chart and the answers part company")
            .Items.Single(i => i.Title == "♂ Mars: hard-won");
        Assert.StartsWith("In the chart it works with strain (dignity −", wonItem.Meta);
    }

    [Fact]
    public void Without_a_birth_time_there_is_no_dignity_no_houses_and_no_Ascendant_to_read()
    {
        var reading = Mirror.Compose(Repo.Charts.Calculate(Demo.Person(timed: false)), Profile(("N2", Hi), ("E", Hi)));
        Assert.DoesNotContain(reading.Sections, s => s.Heading == "Where the chart and the answers part company");
        Assert.Contains(reading.Sections[0].Items, i => i.Text.StartsWith("This chart has no birth time"));
        Assert.All(reading.Planets, p => Assert.Equal(MirrorExpectation.Unknown, p.Expectation));
        Assert.False(reading.OverviewLines.ContainsKey("Rising"));
        Assert.All(reading.Sections.Single(s => s.Heading == "How each planet is being lived").Items,
            i => Assert.DoesNotContain(" house", i.Text.Split('\n')[0]));
    }

    // ── In the Report ─────────────────────────────────────────────────────────

    [Fact]
    public void Ordinary_answers_leave_the_Report_as_it_was_but_for_saying_so()
    {
        var chart = Chart();
        var plain = Report.Interpret(chart);
        var reading = Mirror.Compose(chart, Profile());
        var shaped = Report.Interpret(chart, null, reading);

        Assert.All(reading.Planets, p => { Assert.True(p.IsOrdinary); Assert.Equal("", p.ReportLine); });
        foreach (var section in plain.Where(s => s.Heading != "Overview"))
            Assert.Equal(section.Paragraphs, shaped.Single(s => s.Heading == section.Heading).Paragraphs);

        // The Overview gains only the line on the five traits; the Report says at its end where to look.
        var overview = shaped.Single(s => s.Heading == "Overview").Paragraphs;
        Assert.Equal(plain.Single(s => s.Heading == "Overview").Paragraphs, overview.SkipLast(1));
        Assert.Equal("By Elvis's answers, taken as a whole: near the ordinary on all five of the broad traits.", overview[^1]);
        Assert.Equal("In the Light of the Answers", shaped[^1].Heading);
        Assert.StartsWith("Elvis answered Lore's personality inventory on 10 October 2026.", shaped[^1].Paragraphs[0]);
        Assert.Contains(shaped[^1].Paragraphs, p => p.StartsWith("For none of the seven traditional planets"));
    }

    [Fact]
    public void A_planets_paragraph_gains_how_it_is_lived_and_how_that_sits_with_its_sign()
    {
        var chart = Chart();
        var plain = Report.Interpret(chart);
        IReadOnlyList<ReportSection> Shaped(params (string, double)[] set) => Report.Interpret(chart, null, Mirror.Compose(chart, Profile(set)));
        string Was(string planet) => Para(plain, "The Planets", $" {planet} in ");
        string Now(IReadOnlyList<ReportSection> r, string planet) => Para(r, "The Planets", $" {planet} in ");

        // Mars direct, in Libra, which suggests restraint: the comparison follows on the dial it is about.
        var direct = Shaped(("E3", Hi), ("E4", Hi), ("E5", Hi));
        Assert.Equal(Was("Mars") + " By Elvis's answers this drive is in full and well-governed use. That is far more push than Mars in Libra would suggest.",
            Now(direct, "Mars"));
        Assert.Equal(Was("Venus"), Now(direct, "Venus"));   // nothing was answered of Venus
        Assert.Equal(Was("Pluto"), Now(direct, "Pluto"));   // and Pluto is not read

        // Where the answers bear the sign out, it says that instead.
        var wary = Shaped(("A1", Lo), ("A4", Lo));
        Assert.EndsWith("By Elvis's answers it comes with little trust and a readiness to argue. That bears out the wariness that Venus in Capricorn describes.",
            Now(wary, "Venus"));

        // A comparison about something the sentence does not mention is added as a thing apart, never as "That is".
        var temper = Shaped(("N2", Hi), ("E3", Hi), ("E4", Hi), ("E5", Hi));
        Assert.EndsWith("By Elvis's answers the drive is strong and so is the temper. That is far more push than Mars in Libra would suggest.", Now(temper, "Mars"));
        var taste = Shaped(("O2", Hi), ("E1", Hi), ("E2", Hi));
        Assert.EndsWith("By Elvis's answers it shows as ease and warmth in company. That is far more sociability than Venus in Capricorn would suggest. Art and beauty matter a great deal.",
            Now(taste, "Venus"));

        // Uranus and Neptune have no sign to be set against.
        Assert.Equal(Was("Uranus") + " By Elvis's answers convention and authority are readily questioned.", Now(Shaped(("O6", Hi)), "Uranus"));
    }

    [Fact]
    public void The_Overview_sets_the_Sun_the_Moon_and_the_rising_sign_against_the_answers()
    {
        var chart = Chart();
        var plain = Report.Interpret(chart).Single(s => s.Heading == "Overview").Paragraphs;
        // Sun in Capricorn (ambition expected) answered with self-doubt; Moon in Pisces
        // (worry expected) answered with worry; Sagittarius rising answered as reserved.
        var reading = Mirror.Compose(chart, Profile(
            ("C1", Lo), ("C4", Lo), ("A5", Hi), ("E6", Lo), ("N1", Hi), ("N3", Hi), ("N6", Hi), ("E", Lo), ("O", Hi)));
        var shaped = Report.Interpret(chart, null, reading).Single(s => s.Heading == "Overview").Paragraphs;

        Assert.Equal(plain[0] + " By Elvis's answers there is far less confidence and ambition than the Sun in Capricorn would suggest.", shaped[0]);
        Assert.Equal(plain[1] + " By Elvis's answers this bears out the worry and unease that the Moon in Pisces describes.", shaped[1]);
        Assert.Contains(shaped, p => p.EndsWith("(Rising in Sagittarius.) By Elvis's answers there is far less outgoingness than the Ascendant in Sagittarius would suggest."));
        Assert.Equal("By Elvis's answers, taken as a whole: high on Openness to Experience, low on Extraversion, and near the ordinary on the rest.", shaped[^1]);

        // How the two lights are lived is said under The Planets, not twice.
        var planets = Report.Interpret(chart, null, reading);
        Assert.EndsWith("By Elvis's answers little of this is on show at present: confidence and spirits are both low.", Para(planets, "The Planets", " Sun in "));
        Assert.EndsWith("By Elvis's answers there is more worry and strain here than this suggests.", Para(planets, "The Planets", " Moon in "));

        // An outgoing person with Sagittarius rising bears the sign out.
        var outgoing = Mirror.Compose(chart, Profile(("E", Hi)));
        Assert.Equal("By Elvis's answers this bears out the outgoingness that the Ascendant in Sagittarius describes.", outgoing.OverviewLines["Rising"]);
        Assert.Equal("By Elvis's answers, taken as a whole: high on Extraversion, and near the ordinary on the rest.", outgoing.Summary);
    }

    [Fact]
    public void An_aspect_between_two_planets_the_answers_speak_of_is_read_by_how_each_end_is_lived()
    {
        // Elvis Presley has the Sun square Mars.
        var chart = Chart();
        Assert.Contains(chart.Aspects, a => a.PlanetA == Models.Planet.Sun && a.PlanetB == Models.Planet.Mars && a.Type == AspectType.Square);
        NatalPoint sun = NatalPoint.Of(Models.Planet.Sun), mars = NatalPoint.Of(Models.Planet.Mars);

        var both = Mirror.Compose(chart, Profile(("C1", Lo), ("C4", Lo), ("A5", Hi), ("E6", Lo), ("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Hi)));
        const string line = "By Elvis's answers both ends of this are under strain, the Sun dimmed and Mars combative: the friction it describes is live.";
        Assert.Equal(line, both.AspectLine(sun, mars, AspectType.Square));
        Assert.Equal(line, both.AspectLine(mars, sun, AspectType.Square));   // whichever way round it is asked
        Assert.EndsWith(line, Para(Report.Interpret(chart, null, both), "Major Aspects", "Sun sits in dynamic tension with Mars"));
        Assert.Contains(both.Sections.Single(s => s.Heading == "Between the planets").Items, i => i.Title == "☉ Sun square ♂ Mars" && i.Text == line);

        // One end ordinary: nothing is said of the aspect.
        var one = Mirror.Compose(chart, Profile(("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Hi)));
        Assert.Equal("", one.AspectLine(sun, mars, AspectType.Square));
        Assert.DoesNotContain(one.Sections, s => s.Heading == "Between the planets");

        // Every planet marked out: the closest six aspects only, and no planet dwelt on twice.
        var all = Mirror.Compose(chart, Profile(Inventory.Instrument.Facets.ToDictionary(f => f.Key, f => f.Key.GetHashCode() % 2 == 0 ? Hi : Lo)));
        Assert.Equal(6, all.AspectLines.Count);
        Assert.All(all.AspectLines.Values, l => Assert.StartsWith("By Elvis's answers", l));
        Assert.DoesNotContain(all.AspectLines.Values, l => l.Contains('{'));
    }

    [Fact]
    public void An_element_or_mode_the_chart_leans_toward_or_lacks_is_set_against_the_answers()
    {
        // Elvis Presley's chart leans toward cardinal signs (six bodies of thirteen).
        var chart = Chart();
        Assert.Equal([Modality.Cardinal], ChartInterpreter.Leaning(NatalMetricsService.ModalityCounts(chart)).Strong);

        var leading = Mirror.Compose(chart, Profile(("E3", Hi), ("C4", Hi)));
        Assert.Equal("By Elvis's answers the initiative is in evidence: quick to take charge and set on achieving.", leading.ModalityLines[Modality.Cardinal]);
        Assert.EndsWith(leading.ModalityLines[Modality.Cardinal], Para(Report.Interpret(chart, null, leading), "Modal Balance", "leans toward Cardinal"));

        var led = Mirror.Compose(chart, Profile(("E3", Lo), ("C4", Lo)));
        Assert.StartsWith("By Elvis's answers little of this initiative is showing", led.ModalityLines[Modality.Cardinal]);
        Assert.Empty(Mirror.Compose(chart, Profile()).ModalityLines);   // ordinary answers: nothing

        // A chart with no water at all, answered by someone of strong feeling.
        var dry = Repo.Figures.Select(f => Repo.Charts.Calculate(f))
            .First(c => NatalMetricsService.ElementCounts(c)[Element.Water] == 0);
        var feeling = Mirror.Compose(dry, Profile(("O3", Hi), ("A6", Hi), ("N1", Hi)));
        Assert.Contains("there is water all the same", feeling.ElementLines[Element.Water]);
        Assert.Contains(feeling.ElementLines[Element.Water], string.Join(" ", Report.Interpret(dry, null, feeling).Single(s => s.Heading == "Elemental Balance").Paragraphs));

        // An even spread leans nowhere.
        var even = ChartInterpreter.Leaning(new Dictionary<Element, int> { [Element.Fire] = 3, [Element.Earth] = 3, [Element.Air] = 4, [Element.Water] = 3 });
        Assert.Empty(even.Strong);
        Assert.Empty(even.Empty);
    }

    // ── In the Daily and the Forecast ─────────────────────────────────────────

    private static readonly DailyInterpreter Daily = new(Repo.Data("daily.json"));
    private static readonly TransitService Transits = new(Repo.Charts);
    private static readonly NodaTime.DateTimeZone Utc = NodaTime.DateTimeZone.Utc;

    // Every one of the seven marked out.
    private static InventoryProfile Marked() => Profile(
        ("C1", Lo), ("C4", Lo), ("A5", Hi), ("E6", Lo),          // Sun dimmed
        ("N1", Hi), ("N3", Hi), ("N6", Hi),                      // Moon uneasy
        ("O5", Hi),                                              // Mercury inquiring
        ("E1", Lo), ("E2", Lo), ("A1", Lo), ("A4", Lo),          // Venus guarded
        ("E3", Hi), ("E4", Hi), ("E5", Hi), ("N2", Hi),          // Mars combative
        ("O4", Hi), ("A3", Hi), ("N5", Hi),                      // Jupiter lavish
        ("C5", Hi), ("C3", Hi), ("C2", Hi), ("C6", Hi));         // Saturn structured

    [Fact]
    public void A_days_reading_says_how_a_transit_meets_the_planet_as_it_is_lived_once_and_on_its_day()
    {
        var chart = Chart();
        var answers = Mirror.Compose(chart, Marked());
        var mars = Planet(answers, Models.Planet.Mars);
        Assert.StartsWith("By your answers you are forceful and quick to anger, and today offers a fight.", mars.DailyLine(TransitTone.Tension));
        Assert.StartsWith("By your answers you have force to spare.", mars.DailyLine(TransitTone.Flow));
        Assert.Equal(mars.DailyHard, mars.DailyLine(TransitTone.Conjunction));    // under strain, a conjunction is hard
        var saturn = Planet(answers, Models.Planet.Saturn);
        Assert.Equal(saturn.DailyEasy, saturn.DailyLine(TransitTone.Conjunction)); // lived well, it is easy

        int added = 0;
        for (int day = 0; day < 40; day++)
        {
            var date = new DateOnly(2026, 10, 1).AddDays(day);
            var before = Daily.Compose(chart, Transits.Scan(chart, date, Utc), Utc);
            var after = Daily.Compose(chart, Transits.Scan(chart, date, Utc), Utc, answers);
            var a = before.Sections.SelectMany(s => s.Items).ToList();
            var b = after.Sections.SelectMany(s => s.Items).ToList();
            // Nothing is added, removed or reordered: some items are a sentence longer.
            Assert.Equal(a.Select(i => i.Title), b.Select(i => i.Title));

            var targets = new List<string>();
            foreach (var (was, now) in a.Zip(b))
            {
                if (now.Text == was.Text) continue;
                Assert.StartsWith(was.Text + " By your answers", now.Text);
                added++;
                // Only under a transit by a planet, in the day's highlights, on the day it
                // is exact (or the Moon's, any day), and once for each natal planet.
                var e = after.Events.Single(x => now.Title == $"{x.Mover.Symbol()} {x.Mover.Name()} {x.Aspect.Verb()} your natal {x.Target.Symbol} {x.Target.Name}");
                Assert.True(e.Mover <= Models.Planet.Pluto && !e.IsBackground);
                Assert.True(e.Mover == Models.Planet.Moon || e.Phase == TransitPhase.Exact);
                Assert.Contains(now, after.Sections.Single(s => s.Heading == "Today's highlights").Items);
                targets.Add(e.Target.Name);
            }
            Assert.Equal(targets.Count, targets.Distinct().Count());
        }
        Assert.True(added > 20);
    }

    [Fact]
    public void A_pass_in_the_forecast_takes_the_line_for_a_stretch_of_days_once_a_month_for_each_planet()
    {
        var chart = Chart();
        var answers = Mirror.Compose(chart, Marked());
        var start = new DateOnly(2026, 10, 1);
        var passes = Transits.Forecast(chart, start, 91, Utc);
        var plain = Daily.ComposeForecast(chart, passes, start, 91, Utc);
        var shaped = Daily.ComposeForecast(chart, passes, start, 91, Utc, null, answers);

        Assert.Equal(plain.Sections.Select(s => s.Heading), shaped.Sections.Select(s => s.Heading));
        int added = 0;
        foreach (var (was, now) in plain.Sections.Zip(shaped.Sections))
        {
            Assert.Equal(was.Items.Select(i => i.Title), now.Items.Select(i => i.Title));
            var targets = new List<string>();
            foreach (var (a, b) in was.Items.Zip(now.Items))
            {
                if (a.Text == b.Text) continue;
                // "today" is for the Daily: a pass lasts days or weeks.
                Assert.StartsWith(a.Text + " By your answers your natal ", b.Text);
                Assert.DoesNotContain("oday", b.Text[a.Text.Length..]);
                Assert.Contains(" stretch", b.Text[a.Text.Length..]);
                string title = b.Title ?? "";
                targets.Add(title[title.LastIndexOf("your natal", StringComparison.Ordinal)..]);
                added++;
            }
            Assert.Equal(targets.Count, targets.Distinct().Count());   // once a month for each planet
        }
        Assert.True(added > 5);
        Assert.Equal("By your answers your natal Mars is combative. Expect this stretch to press on that.", Planet(answers, Models.Planet.Mars).ForecastLine(TransitTone.Tension));
        Assert.Equal("By your answers your natal Saturn is structured. This stretch plays to that strength.", Planet(answers, Models.Planet.Saturn).ForecastLine(TransitTone.Flow));
        Assert.Equal("", Planet(answers, Models.Planet.Neptune).ForecastLine(TransitTone.Flow));   // neither strained nor well
    }

    [Fact]
    public void One_persons_answers_never_colour_another_persons_readings()
    {
        var elvis = Chart();
        var marilyn = Chart("marilyn-monroe");
        var answers = Mirror.Compose(elvis, Marked());

        Assert.Equal(Report.Interpret(marilyn).SelectMany(s => s.Paragraphs), Report.Interpret(marilyn, null, answers).SelectMany(s => s.Paragraphs));

        var date = new DateOnly(2026, 10, 2);
        string Text(MirrorReading? a) => string.Join("|", Daily.Compose(marilyn, Transits.Scan(marilyn, date, Utc), Utc, a)
            .Sections.SelectMany(s => s.Items).Select(i => i.Text));
        Assert.Equal(Text(null), Text(answers));

        var passes = Transits.Forecast(marilyn, date, 31, Utc);
        Assert.Equal(Daily.ComposeForecast(marilyn, passes, date, 31, Utc).Sections.SelectMany(s => s.Items).Select(i => i.Text),
            Daily.ComposeForecast(marilyn, passes, date, 31, Utc, null, answers).Sections.SelectMany(s => s.Items).Select(i => i.Text));
    }

    // ── Across a whole population ─────────────────────────────────────────────

    // People made up from the published structure of the questionnaire (how strongly each
    // facet goes with each of the five traits: Kajonius & Johnson 2019), so that their
    // facets hang together as real people's do.
    private static List<Dictionary<string, double>> Population(int count)
    {
        var loadings = JsonSerializer.Deserialize<Dictionary<string, double[]>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", "ipip-neo-120-loadings.json")))!;
        Assert.Equal(30, loadings.Count);
        var random = new Random(20261010);
        double Normal() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

        var people = new List<Dictionary<string, double>>();
        for (int n = 0; n < count; n++)
        {
            var traits = Enumerable.Range(0, 5).Select(_ => Normal()).ToArray();
            var z = new Dictionary<string, double>();
            foreach (var (facet, load) in loadings)
            {
                double common = load.Zip(traits, (a, b) => a * b).Sum(), shared = load.Sum(a => a * a);
                double own = Math.Sqrt(Math.Max(0.15, 1 - shared));
                z[facet] = (common + own * Normal()) / Math.Sqrt(shared + own * own);
            }
            people.Add(z);
        }
        return people;
    }

    [Fact]
    public void Across_three_thousand_people_every_expression_is_met_and_none_swallows_the_rest()
    {
        var chart = Chart();
        var met = new Dictionary<string, int>();
        var marked = new List<int>();
        var people = Population(3000);
        foreach (var z in people)
        {
            var reading = Mirror.Compose(chart, Profile(z));
            foreach (var p in reading.Planets) met[$"{p.Planet.Name()}/{p.Expression}"] = met.GetValueOrDefault($"{p.Planet.Name()}/{p.Expression}") + 1;
            marked.Add(reading.Planets.Count(p => !p.IsOrdinary));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("mirror.json")));
        foreach (var planet in doc.RootElement.GetProperty("planets").EnumerateObject())
        {
            var expressions = planet.Value.GetProperty("expressions").EnumerateArray().ToList();
            int dials = planet.Value.GetProperty("dials").EnumerateObject().Count(d => !planet.Value.TryGetProperty("also", out _) || d.Name != "taste");
            foreach (var e in expressions)
            {
                string id = $"{planet.Name}/{e.GetProperty("name").GetString()}";
                double share = met.GetValueOrDefault(id) / (double)people.Count;
                // Every expression is somebody's: at least one person in a hundred.
                Assert.True(share >= 0.01, $"{id} is met by only {share:P1}");
                // And the catch-all is not most people's, unless the planet rests on one facet.
                if (!e.GetProperty("when").EnumerateObject().Any())
                    Assert.True(share <= (dials == 1 ? 0.75 : 0.55), $"{id} takes {share:P0}");
            }
        }
        // Nearly everyone is told something of at least three planets.
        Assert.True(marked.Count(m => m >= 3) >= 0.9 * people.Count);
        Assert.InRange(marked.Average(), 4.5, 6.5);
    }

    [Fact]
    public void For_any_person_and_any_chart_what_is_added_is_whole_marked_and_only_added()
    {
        var charts = Repo.Figures.Where((_, i) => i % 26 == 0).Select(f => Repo.Charts.Calculate(f)).ToList();
        var plain = charts.Select(c => Report.Interpret(c)).ToList();
        var people = Population(400);
        var date = new DateOnly(2026, 10, 1);

        for (int n = 0; n < people.Count; n++)
        {
            int k = n % charts.Count;
            var chart = charts[k];
            string name = chart.Celebrity.Name.Split(' ')[0];
            var reading = Mirror.Compose(chart, Profile(people[n]));

            // The reading itself: nothing left unfilled, nothing doubled.
            foreach (var item in reading.Sections.SelectMany(s => s.Items))
            {
                Assert.DoesNotContain("{", item.Text);
                Assert.DoesNotContain("  ", item.Text);
                Assert.False(string.IsNullOrWhiteSpace(item.Text));
            }

            // The Report: every paragraph it had is still there word for word, and anything
            // after it begins by saying it is from the answers.
            var shaped = Report.Interpret(chart, null, reading);
            foreach (var section in plain[k])
            {
                var now = shaped.Single(s => s.Heading == section.Heading).Paragraphs;
                Assert.True(now.Count >= section.Paragraphs.Count);
                for (int i = 0; i < section.Paragraphs.Count; i++)
                {
                    string was = section.Paragraphs[i];
                    Assert.True(now[i] == was || now[i].StartsWith(was + $" By {name}'s answers"), $"{chart.Celebrity.Name}: {now[i]}");
                    Assert.DoesNotContain("{", now[i]);
                    Assert.DoesNotContain("  ", now[i][was.Length..]);
                }
                Assert.All(now.Skip(section.Paragraphs.Count), p => Assert.StartsWith($"By {name}'s answers", p));
            }
            Assert.Equal("In the Light of the Answers", shaped[^1].Heading);

            // A day's reading and a forecast, on a few of them.
            if (n % 20 != 0) continue;
            var a = Daily.Compose(chart, Transits.Scan(chart, date.AddDays(n), Utc), Utc).Sections.SelectMany(s => s.Items).ToList();
            var b = Daily.Compose(chart, Transits.Scan(chart, date.AddDays(n), Utc), Utc, reading).Sections.SelectMany(s => s.Items).ToList();
            Assert.Equal(a.Count, b.Count);
            Assert.All(a.Zip(b), pair => Assert.True(pair.Second.Text == pair.First.Text || pair.Second.Text.StartsWith(pair.First.Text + " By your answers")));
        }
    }

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
