using System.Text.Json;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

public class DailyInterpreterTests
{
    private static readonly DateTimeZone Chicago = DateTimeZoneProviders.Tzdb["America/Chicago"];
    private static readonly DailyInterpreter Interpreter = new(Repo.Data("daily.json"));

    private static DailyReading Read(DateOnly date, bool timed = true)
    {
        var natal = Repo.Charts.Calculate(Demo.Person(timed));
        var sky = new TransitService(Repo.Charts).Scan(natal, date, Chicago);
        return Interpreter.Compose(natal, sky, Chicago);
    }

    [Fact]
    public void The_corpus_has_a_line_for_every_mover_tone_and_natal_point()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Repo.Data("daily.json")));
        var transits = doc.RootElement.GetProperty("transits");
        string[] bodies = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto",
                           "North Node", "Chiron", "Lilith"];
        var missing =
            from mover in bodies
            from tone in new[] { "Conjunction", "Flow", "Tension" }
            from target in bodies.Concat(["Ascendant", "Midheaven"])
            let key = $"{mover}|{tone}|{target}"
            where !transits.TryGetProperty(key, out var v) || string.IsNullOrWhiteSpace(v.GetString())
            select key;
        Assert.Empty(missing);
    }

    [Fact]
    public void A_void_Moon_is_noted_with_its_hours_and_what_ends_it()
    {
        // On 2 October 2026 the Moon leaves Gemini for Cancer, in Chicago's afternoon.
        var note = Read(new DateOnly(2026, 10, 2)).Sections.Single(s => s.Heading == "Also in the sky")
            .Items.Single(i => i.Title!.StartsWith("☽ Moon void of course "));
        Assert.Matches("^☽ Moon void of course (until|from) ", note.Title);
        Assert.Matches("^its last aspect in Gemini is an? [a-z]+ to [A-Z][a-z]+  ·  it then enters Cancer$", note.Meta);
        Assert.Contains("void of course", note.Text);

        // It has nothing to do with the birth time.
        Assert.Contains(Read(new DateOnly(2026, 10, 2), timed: false).Sections.SelectMany(s => s.Items),
            i => i.Title == note.Title);
    }

    [Fact]
    public void The_same_chart_and_date_always_give_the_same_reading()
    {
        var a = Read(new DateOnly(2026, 10, 2));
        var b = Read(new DateOnly(2026, 10, 2));
        Assert.Equal(Flatten(a), Flatten(b));
    }

    [Fact]
    public void A_reading_has_the_glance_and_highlights_sections_with_text()
    {
        var reading = Read(new DateOnly(2026, 10, 2));
        Assert.Equal("The day at a glance", reading.Sections[0].Heading);
        Assert.Contains(reading.Sections, s => s.Heading == "Today's highlights");
        Assert.All(reading.Sections.SelectMany(s => s.Items), i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
        Assert.Contains(reading.Events, e => e.Shown);
    }

    [Fact]
    public void A_reading_carries_what_the_wheel_beside_it_draws()
    {
        var reading = Read(new DateOnly(2026, 10, 2));

        // The outer band: every body, as it stands at the middle of the day.
        Assert.Equal(Enum.GetValues<Planet>().Length, reading.Midday.Count);
        var noon = new LocalDateTime(2026, 10, 2, 12, 0).InZoneLeniently(Chicago).ToDateTimeUtc();
        double sunAtNoon = Repo.Charts.CalculateBody(SwissEphemeris.DateTimeToJulianDay(noon), Planet.Sun)!.Longitude;
        Assert.Equal(sunAtNoon, reading.Midday.First(p => p.Planet == Planet.Sun).Longitude, 3);

        // Each line: a mover in that band, to a point the birth chart has.
        var natal = Repo.Charts.Calculate(Demo.Person(true));
        Assert.All(reading.Events, e =>
        {
            Assert.Contains(reading.Midday, p => p.Planet == e.Mover);
            Assert.NotNull(natal.LongitudeOf(e.Target));
        });
    }

    [Fact]
    public void Without_a_birth_time_the_reading_says_so_and_mentions_no_houses()
    {
        var reading = Read(new DateOnly(2026, 10, 2), timed: false);
        string text = Flatten(reading);
        Assert.Contains("birth time is unknown", text);
        Assert.DoesNotContain("house", reading.Sections[0].Items.Select(i => i.Title ?? "").Aggregate("", string.Concat));
    }

    private static string Flatten(DailyReading r) =>
        string.Join("\n", r.Sections.SelectMany(s => s.Items.Select(i => $"{s.Heading}|{i.Title}|{i.Meta}|{i.Text}")));
}
