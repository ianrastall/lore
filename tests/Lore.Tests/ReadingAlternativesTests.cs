using System.Globalization;
using System.Text.Json;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

public sealed class ReadingAlternativesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lore-prose-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private sealed class Fixture
    {
        public Dictionary<string, string> Lines { get; init; } = new() { ["greeting"] = "Hello {name}." };
        public int Score { get; init; } = 7;
    }
    public void Dispose()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (string file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
    private string Original(string kind)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, kind + ".json");
        File.Copy(Repo.Data(kind + ".json"), path, true);
        return path;
    }
    private ReadingAlternatives<Fixture> FixtureWith(string path, params string[] choices)
    {
        Directory.CreateDirectory(_directory);
        string baseline = Path.Combine(_directory, "interpretations.json");
        File.WriteAllText(baseline, JsonSerializer.Serialize(new Fixture()));
        var catalog = new ReadingAlternatives.Catalog { Version = "test", Readings = new()
        { ["interpretations"] = new() { [path] = choices } } };
        File.WriteAllText(Path.Combine(_directory, ReadingAlternatives.FileName), JsonSerializer.Serialize(catalog));
        return ReadingAlternatives<Fixture>.Load(baseline, new Fixture(), Options)!;
    }

    [Fact]
    public void Every_bundled_alternative_targets_existing_prose_and_preserves_its_placeholders()
    {
        var catalog = ReadingAlternatives.Read(Repo.Data(ReadingAlternatives.FileName));
        Assert.Equal(new[] { "daily", "davison", "interpretations", "mirror", "synastry" }, catalog.Readings.Keys.Order());
        foreach (string kind in catalog.Readings.Keys)
        {
            string file = Repo.Data(kind + ".json");
            var baseline = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file))!;
            var variants = ReadingAlternatives<Dictionary<string, JsonElement>>.Load(file, baseline, Options);
            Assert.NotNull(variants);
            Assert.NotEmpty(variants.For("a real reading"));
        }
        // The actual typed readers validate the same paths against what they consume.
        _ = new ChartInterpreter(Repo.Data("interpretations.json"));
        _ = new DailyInterpreter(Repo.Data("daily.json"));
        _ = new SynastryInterpreter(Repo.Data("synastry.json"));
        _ = new DavisonInterpreter(Repo.Data("davison.json"));
        Assert.True(new MirrorInterpreter(Repo.Data("mirror.json")).IsAvailable);
    }

    [Theory]
    [InlineData("missing/greeting", "Good morning {name}.")]
    [InlineData("score", "A different score.")]
    [InlineData("lines/greeting", "The person has no placeholder.")]
    [InlineData("lines/greeting", "Hello {name}.")]
    [InlineData("lines/greeting", "")]
    public void Invalid_entries_fail_instead_of_silently_changing_the_reading(string path, string prose) =>
        Assert.Throws<InvalidDataException>(() => FixtureWith(path, prose));

    [Fact]
    public void Selection_is_stable_and_each_reading_has_an_independent_snapshot()
    {
        var variants = FixtureWith("lines/greeting", "Welcome {name}.", "Good morning {name}.");
        var first = variants.For("one");
        string expected = first.Lines["greeting"];
        first.Lines["greeting"] = "A consumer changed its own copy.";
        Assert.Equal(expected, variants.For("one").Lines["greeting"]);
        Assert.Equal(7, variants.For("one").Score);
        var seen = Enumerable.Range(0, 80).Select(i => variants.For(i.ToString(CultureInfo.InvariantCulture)).Lines["greeting"]).Distinct();
        Assert.Equal(3, seen.Count()); // originals remain in the range
        Parallel.For(0, 24, _ => Assert.Equal(expected, variants.For("one").Lines["greeting"]));
    }

    [Fact]
    public void Three_pair_lines_can_use_distinct_alternatives_without_changing_the_tone()
    {
        var variants = FixtureWith("lines/greeting", "Welcome {name}.", "Good morning {name}.");
        var used = new HashSet<string>();
        for (int i = 0; i < 3; i++) variants.Pick("lines/greeting", "same seed", used);
        Assert.Equal(3, used.Count);
    }

    [Theory]
    [InlineData(SynastryTone.Light)] [InlineData(SynastryTone.Harmonious)]
    [InlineData(SynastryTone.Mixed)] [InlineData(SynastryTone.Challenging)]
    [InlineData(SynastryTone.Soulmates)] [InlineData(SynastryTone.Adversaries)]
    public void A_trio_does_not_repeat_a_pair_summary_even_when_all_three_labels_match(SynastryTone tone)
    {
        var charts = Repo.Sample.Take(3).Select(Repo.Charts.Calculate).ToArray();
        var calculated = TrioService.Compare(charts[0], charts[1], charts[2]);
        var trio = new Trio { Charts = charts, GrandTrines = calculated.GrandTrines,
            Tone = calculated.Tone, Pairs = calculated.Pairs.Select(p => p with
            { Compatibility = new Compatibility(tone, 5, 5) }).ToArray() };
        var interpreter = new SynastryInterpreter(Repo.Data("synastry.json"));
        var pairs = interpreter.ComposeTrio(trio).Sections.Single(s => s.Heading == "The three pairs").Items;
        Assert.Equal(3, pairs.Count);
        Assert.Equal(3, pairs.Select(p => p.Text).Distinct().Count());
        Assert.All(pairs, p => Assert.StartsWith(tone.Label(), p.Meta));
        Assert.Equal(trio.Pairs.Select(p => p.Compatibility), calculated.Pairs.Select(_ => new Compatibility(tone, 5, 5)));
    }

    [Fact]
    public void Seeds_ignore_names_culture_and_the_order_of_a_group()
    {
        var chart = Repo.Charts.Calculate(Demo.Person());
        var other = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        string expected = ReadingAlternatives.Seed(chart);
        string group = ReadingAlternatives.GroupSeed(chart, other);
        Assert.Equal(group, ReadingAlternatives.GroupSeed(other, chart));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(expected, ReadingAlternatives.Seed(chart));
            Assert.Equal(group, ReadingAlternatives.GroupSeed(other, chart));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        var person = JsonSerializer.Deserialize<Celebrity>(JsonSerializer.Serialize(Demo.Person()).Replace("Demo Person", "Renamed person"))!;
        Assert.Equal(expected, ReadingAlternatives.Seed(Repo.Charts.Calculate(person)));
    }

    [Fact]
    public void Expanded_synastry_preserves_scores_contacts_and_selection()
    {
        var original = new SynastryInterpreter(Original("synastry"));
        var expanded = new SynastryInterpreter(Repo.Data("synastry.json"));
        var charts = Repo.Sample.Take(10).Select(Repo.Charts.Calculate).ToArray();
        for (int i = 0; i < charts.Length - 1; i++)
        {
            var a = original.Compose(SynastryService.Compare(charts[i], charts[i + 1]));
            var b = expanded.Compose(SynastryService.Compare(charts[i], charts[i + 1]));
            Assert.Equal(a.Compatibility, b.Compatibility);
            Assert.Equal(a.Trace, b.Trace);
            Assert.Equal(a.Aspects.Select(x => (x.Score, x.Shown)), b.Aspects.Select(x => (x.Score, x.Shown)));
            // Reversing a comparison can choose different contacts because of the
            // existing per-point limits. The shared tone paragraph must remain stable.
            var reverse = expanded.Compose(SynastryService.Compare(charts[i + 1], charts[i]));
            Assert.Equal(b.Sections[0].Items[0].Text, reverse.Sections[0].Items[0].Text);
        }
    }

    [Fact]
    public void Expanded_daily_preserves_transit_ranking_selection_and_tone()
    {
        var original = new DailyInterpreter(Original("daily"));
        var expanded = new DailyInterpreter(Repo.Data("daily.json"));
        var chart = Repo.Charts.Calculate(Demo.Person());
        var transits = new TransitService(Repo.Charts);
        var date = new DateOnly(2026, 11, 1);
        var a = original.Compose(chart, transits.Scan(chart, date, DateTimeZone.Utc), DateTimeZone.Utc);
        var b = expanded.Compose(chart, transits.Scan(chart, date, DateTimeZone.Utc), DateTimeZone.Utc);
        Assert.Equal(a.Tone, b.Tone);
        Assert.Equal(a.Trace, b.Trace);
        Assert.Equal(a.Events.Select(x => (x.Score, x.Shown, x.PeakUtc)), b.Events.Select(x => (x.Score, x.Shown, x.PeakUtc)));
    }

    [Fact]
    public void Concurrent_composition_keeps_each_charts_wording_in_its_own_reading()
    {
        var natal = new ChartInterpreter(Repo.Data("interpretations.json"));
        var synastry = new SynastryInterpreter(Repo.Data("synastry.json"));
        var charts = Repo.Sample.Take(12).Select(Repo.Charts.Calculate).ToArray();
        string Read(int i) => JsonSerializer.Serialize(new { Natal = natal.Interpret(charts[i]), Pair = synastry.Compose(SynastryService.Compare(charts[i], charts[(i + 1) % charts.Length])) });
        var expected = Enumerable.Range(0, charts.Length).Select(Read).ToArray();
        Parallel.For(0, charts.Length * 2, i => Assert.Equal(expected[i % charts.Length], Read(i % charts.Length)));
    }
}
