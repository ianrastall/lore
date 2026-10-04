using Lore.Models;
using Lore.Services;
using System.Text.Json;

namespace Lore.Tests;

// The natal report: the corpus covers what it says it covers, and a real chart is
// written from the bespoke lines rather than one stock sentence repeated.
public class ChartInterpreterTests
{
    private static readonly string[] Signs = Enum.GetValues<ZodiacSign>().Select(s => s.Name()).ToArray();
    private static readonly string[] Bodies = Enum.GetValues<Planet>().Select(p => p.Name()).ToArray();

    private static Dictionary<string, string> Section(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("interpretations.json")));
        return doc.RootElement.GetProperty(name).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static void Covers(string section, IEnumerable<string> keys)
    {
        var map = Section(section);
        var expected = keys.ToList();
        Assert.All(expected, k => Assert.True(map.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v), $"{section}: {k}"));
        Assert.Empty(map.Keys.Except(expected)); // a stray key is a misspelt one, and would never be read
    }

    [Fact]
    public void The_corpus_has_a_line_for_every_sign_house_and_element()
    {
        foreach (var section in new[] { "sunSigns", "moonSigns", "risingSigns", "stelliumSigns" })
            Covers(section, Signs);
        Covers("planetInSign", Bodies.SelectMany(b => Signs.Select(s => $"{b}|{s}")));
        Covers("planetInHouse", Bodies.SelectMany(b => Enumerable.Range(1, 12).Select(h => $"{b}|{h}")));

        var elements = Enum.GetNames<Element>();
        Covers("sunMoonBlend", elements.SelectMany(a => elements.Select(b => $"{a}|{b}")));
        Covers("elementStrong", elements);
        Covers("elementWeak", elements);
        Covers("grandTrines", elements);
        foreach (var section in new[] { "modalityStrong", "modalityWeak", "tSquares", "grandCrosses" })
            Covers(section, Enum.GetNames<Modality>());
    }

    [Fact]
    public void The_corpus_has_a_line_for_every_aspect_among_the_bodies_and_to_the_angles()
    {
        string[] planets = Bodies, tones = Enum.GetNames<TransitTone>();
        // Mercury is never more than 28° from the Sun and Venus never more than 48°, so
        // they meet it only by conjunction; nor can the two of them be 90° apart.
        var impossible = new HashSet<string>
        {
            "Sun|Flow|Mercury", "Sun|Tension|Mercury", "Sun|Flow|Venus", "Sun|Tension|Venus", "Mercury|Tension|Venus",
        };

        var keys = new List<string>();
        for (int i = 0; i < planets.Length; i++)
            for (int j = i + 1; j < planets.Length; j++)
                keys.AddRange(tones.Select(t => $"{planets[i]}|{t}|{planets[j]}"));
        foreach (var angle in new[] { "Ascendant", "Midheaven" })
            keys.AddRange(planets.SelectMany(p => tones.Select(t => $"{p}|{t}|{angle}")));

        Covers("aspects", keys.Except(impossible));
    }

    [Fact]
    public void A_real_report_is_written_from_the_bespoke_lines()
    {
        var interpreter = new ChartInterpreter(Repo.Data("interpretations.json"));
        var notes = Section("aspectNotes").Values.ToList();

        foreach (var id in new[] { "albert-einstein", "elvis-presley", "roger-federer", "karl-marx" })
        {
            var chart = Repo.Charts.Calculate(Repo.Figure(id));
            var report = interpreter.Interpret(chart);
            List<string> Paragraphs(string heading) => [.. report.Single(s => s.Heading == heading).Paragraphs];

            // Sun, Moon, how the two combine, Rising.
            var overview = Paragraphs("Overview");
            Assert.Equal(4, overview.Count);
            Assert.DoesNotContain("{name}", string.Concat(overview));

            // Every planet's line goes on to say something about its house, and no two alike.
            var planets = Paragraphs("The Planets");
            Assert.Equal(chart.Planets.Count, planets.Count);
            Assert.DoesNotContain(planets, p => p.Contains("colouring") || p.Contains("This colours"));
            Assert.All(planets, p => Assert.True(p.Count(c => c == '.') >= 2, p));

            // Every aspect gets the text written for that pair, never the stock note for
            // the kind of aspect, and so no two read alike.
            var aspects = Paragraphs("Major Aspects");
            Assert.DoesNotContain(aspects, p => notes.Any(p.Contains));
            var own = aspects.Select(p => p[(p.IndexOf("°).") + 3)..].Split(" Out of sign:")[0]).ToList();
            Assert.True(own.Count >= 10, id);
            Assert.Equal(own.Count, own.Distinct().Count());
        }
    }

    [Fact]
    public void A_tie_names_both_and_every_empty_element_is_mentioned()
    {
        // Three bodies in fire, three in air, none in earth or water.
        var chart = new NatalChart
        {
            Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTimeKnown = false },
            Planets =
            [
                new PlanetPosition { Planet = Planet.Sun, Longitude = 5 },        // Aries
                new PlanetPosition { Planet = Planet.Moon, Longitude = 125 },     // Leo
                new PlanetPosition { Planet = Planet.Mercury, Longitude = 245 },  // Sagittarius
                new PlanetPosition { Planet = Planet.Venus, Longitude = 65 },     // Gemini
                new PlanetPosition { Planet = Planet.Mars, Longitude = 185 },     // Libra
                new PlanetPosition { Planet = Planet.Jupiter, Longitude = 305 },  // Aquarius
            ],
            Houses = [],
            Aspects = [],
        };
        var balance = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(chart)
            .Single(s => s.Heading == "Elemental Balance").Paragraphs;

        Assert.StartsWith("The chart leans equally toward Fire (3 of 6 bodies)", balance[0]);
        Assert.StartsWith("And toward Air (3 of 6 bodies)", balance[1]);
        Assert.Contains(balance, p => p.StartsWith("No body is in an earth sign."));
        Assert.Contains(balance, p => p.StartsWith("No body is in a water sign."));
        Assert.StartsWith("Element tally", balance[^1]);
    }
}
