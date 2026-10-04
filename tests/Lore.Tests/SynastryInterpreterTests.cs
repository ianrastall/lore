using System.Text.Json;
using System.Text.RegularExpressions;
using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

public class SynastryInterpreterTests
{
    private static readonly SynastryInterpreter Interpreter = new(Repo.Data("synastry.json"));

    // A second made-up person to set beside the demo chart.
    private static Celebrity Partner(bool timed = true) => new()
    {
        Id = "partner", Name = "Sample Partner", Category = "Test",
        BirthDate = "1988-11-02", BirthTime = "21:15", BirthTimeKnown = timed,
        BirthPlace = "London", Latitude = 51.5074, Longitude = -0.1278,
        UtcOffsetHours = 0, TimeZoneId = "Europe/London",
    };

    private static SynastryReading Read(bool firstTimed = true, bool secondTimed = true) =>
        Interpreter.Compose(SynastryService.Compare(
            Repo.Charts.Calculate(Demo.Person(firstTimed)),
            Repo.Charts.Calculate(Partner(secondTimed))));

    [Fact]
    public void The_corpus_has_a_line_for_every_key_pair_and_tone()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("synastry.json")));
        var aspects = doc.RootElement.GetProperty("aspects");

        // Standard order, as the keys are written.
        string[] core = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Ascendant"];
        var pairs =
            (from i in Enumerable.Range(0, core.Length)
             from j in Enumerable.Range(i, core.Length - i)
             select (core[i], core[j]))
            .Concat(from personal in new[] { "Sun", "Moon", "Venus", "Mars" }
                    from outer in new[] { "Uranus", "Neptune", "Pluto" }
                    select (personal, outer));

        var missing =
            from pair in pairs
            from tone in new[] { "Conjunction", "Flow", "Tension" }
            let key = $"{pair.Item1}|{tone}|{pair.Item2}"
            where !aspects.TryGetProperty(key, out var v) || string.IsNullOrWhiteSpace(v.GetString())
            select key;
        Assert.Empty(missing);
    }

    [Fact]
    public void Corpus_lines_use_only_the_placeholders_the_interpreter_fills()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("synastry.json")));
        var allowed = new Dictionary<string, string[]>
        {
            ["aspects"] = ["{a}", "{b}"],
            ["sunElements"] = ["{a}", "{b}"],
            ["moonElements"] = ["{a}", "{b}"],
            ["overlayHouses"] = ["{guest}", "{host}", "{brings}"],
            ["notes"] = ["{name}"],
        };
        var stray =
            from section in doc.RootElement.EnumerateObject()
            where section.Value.ValueKind == JsonValueKind.Object
            from line in section.Value.EnumerateObject()
            from Match m in Regex.Matches(line.Value.GetString() ?? "", @"\{[^}]*\}")
            where !allowed.GetValueOrDefault(section.Name, []).Contains(m.Value)
            select $"{section.Name}/{line.Name}: {m.Value}";
        Assert.Empty(stray);
    }

    [Fact]
    public void The_same_two_charts_always_give_the_same_reading()
    {
        Assert.Equal(Flatten(Read()), Flatten(Read()));
    }

    [Fact]
    public void A_reading_names_both_people_and_leaves_no_placeholder_unfilled()
    {
        var reading = Read();
        Assert.Equal("Demo Person & Sample Partner", reading.Title);
        Assert.Equal("At a glance", reading.Sections[0].Heading);
        Assert.Contains(reading.Sections, s => s.Heading == "In each other's houses");

        string text = Flatten(reading);
        Assert.Contains("Demo", text);
        Assert.Contains("Sample", text);
        Assert.DoesNotContain("{", text);
        Assert.All(reading.Sections.SelectMany(s => s.Items), i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
    }

    [Fact]
    public void The_reading_shows_a_few_contacts_and_lists_them_all()
    {
        var reading = Read();
        int shown = reading.Aspects.Count(a => a.Shown);
        Assert.InRange(shown, 1, 11);
        Assert.Equal(shown, reading.Sections
            .Where(s => s.Heading is "Closest bonds" or "What comes easily" or "What takes work")
            .Sum(s => s.Items.Count));
        Assert.Equal(reading.Aspects.Count, reading.Trace.Count);
        Assert.Equal(reading.Aspects.OrderByDescending(a => a.Score).Select(a => a.Score), reading.Aspects.Select(a => a.Score));
    }

    [Fact]
    public void A_line_reads_the_same_whichever_person_is_listed_first()
    {
        // Venus square Mars has one corpus line; {a} must be the Venus person either way.
        var venus = Chart("Anna", (Planet.Venus, 100));
        var mars = Chart("Ben", (Planet.Mars, 10));

        string forward = Flatten(Interpreter.Compose(SynastryService.Compare(venus, mars)));
        string reverse = Flatten(Interpreter.Compose(SynastryService.Compare(mars, venus)));

        Assert.Contains("Ben can come on too strong or Anna seem to tease", forward);
        Assert.Contains("Ben can come on too strong or Anna seem to tease", reverse);
    }

    [Fact]
    public void A_pair_without_a_bespoke_line_gets_an_assembled_sentence()
    {
        var a = Chart("Anna", (Planet.Chiron, 100));
        var b = Chart("Ben", (Planet.Mercury, 10));
        string text = Flatten(Interpreter.Compose(SynastryService.Compare(a, b)));
        Assert.Contains("Anna's old wound and healing gift pulls against Ben's way of thinking and speaking.", text);
    }

    [Fact]
    public void Two_slow_planets_in_aspect_are_listed_but_not_written_up()
    {
        var a = Chart("Anna", (Planet.Neptune, 100));
        var b = Chart("Ben", (Planet.Pluto, 100));
        var reading = Interpreter.Compose(SynastryService.Compare(a, b));
        Assert.Single(reading.Aspects);
        Assert.DoesNotContain(reading.Aspects, x => x.Shown);
        Assert.Equal(SynastryTone.Light, reading.Tone);
    }

    [Fact]
    public void Without_a_birth_time_the_reading_says_whose_and_leaves_their_houses_out()
    {
        var reading = Read(secondTimed: false);
        string text = Flatten(reading);
        Assert.Contains("Sample has no recorded birth time", text);
        Assert.DoesNotContain("Moon signs", text);
        Assert.DoesNotContain("in Sample's", text);   // no planets placed in the untimed chart's houses
        Assert.Contains("in Demo's", text);
    }

    // An untimed chart holding just the given bodies, so the only contacts are the ones intended.
    private static NatalChart Chart(string name, params (Planet planet, double lon)[] bodies) => new()
    {
        Celebrity = new Celebrity { Id = name, Name = name, BirthDate = "2000-01-01", BirthTimeKnown = false },
        Planets = bodies.Select(x => new PlanetPosition { Planet = x.planet, Longitude = x.lon }).ToList(),
        Houses = Enumerable.Range(1, 12).Select(h => new HouseCusp { House = h, Longitude = (h - 1) * 30 }).ToList(),
        Aspects = [],
    };

    private static string Flatten(SynastryReading r) =>
        string.Join("\n", r.Sections.SelectMany(s => s.Items.Select(i => $"{s.Heading}|{i.Title}|{i.Meta}|{i.Text}")));
}
