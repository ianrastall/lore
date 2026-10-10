using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The Davison chart: one real chart for two people, cast for the midpoint of their
// births in time and in place.
public class DavisonTests
{
    private static readonly SynastryInterpreter Interpreter = new(Repo.Data("synastry.json"));

    private static NatalChart Chart(string id) => Repo.Charts.Calculate(Repo.Figure(id));

    private static NatalChart Born(string date, string time, double latitude, double longitude, bool timed = true) =>
        Repo.Charts.Calculate(new Celebrity
        {
            Id = $"{date}{longitude}", Name = $"Born {date}", BirthDate = date, BirthTime = time, BirthTimeKnown = timed,
            BirthPlace = "Somewhere", Latitude = latitude, Longitude = longitude, UtcOffsetHours = 0, UtcOffsetFixed = true,
        });

    [Fact]
    public void It_is_cast_for_the_moment_and_the_place_halfway_between()
    {
        var a = Born("1990-01-01", "06:00", 40, 10);
        var b = Born("1994-01-01", "18:00", 60, 50);
        var d = Repo.Charts.Davison(a, b);

        Assert.Equal(new DateTime(1992, 1, 2, 0, 0, 0), d.CalculatedForUtc); // 1,461½ days apart, so 730¾ on
        Assert.Equal(50, d.Celebrity.Latitude, 9);
        Assert.Equal(30, d.Celebrity.Longitude, 9);
        Assert.True(d.Timed);

        // It is an ordinary chart: the one anybody born then and there would have.
        var born = Born("1992-01-02", "00:00", 50, 30);
        Assert.Equal(born.Ascendant, d.Ascendant, 9);
        Assert.Equal(born.Planets.Select(p => p.Longitude), d.Planets.Select(p => p.Longitude));

        // And it is the same whichever of the two is named first.
        var other = Repo.Charts.Davison(b, a);
        Assert.Equal(d.CalculatedForUtc, other.CalculatedForUtc);
        Assert.Equal(d.Ascendant, other.Ascendant, 9);
    }

    [Theory]
    [InlineData(170, -170, 180)]   // either side of the date line: midway is the date line, not Greenwich
    [InlineData(-170, 170, 180)]
    [InlineData(-120, -60, -90)]
    [InlineData(-10, 30, 10)]
    public void The_longitude_is_midway_round_the_shorter_side(double first, double second, double expected)
    {
        var d = Repo.Charts.Davison(Born("1990-01-01", "06:00", 0, first), Born("1990-01-01", "06:00", 0, second));
        double off = Math.Abs(((d.Celebrity.Longitude - expected) % 360 + 540) % 360 - 180);
        Assert.True(off < 1e-9, $"{d.Celebrity.Longitude}");
        Assert.InRange(d.Celebrity.Longitude, -180, 180);
    }

    [Fact]
    public void A_person_with_themselves_gives_their_own_chart()
    {
        var einstein = Chart("albert-einstein");
        var d = Repo.Charts.Davison(einstein, einstein);
        Assert.Equal(einstein.CalculatedForUtc, d.CalculatedForUtc);
        Assert.Equal(einstein.Ascendant, d.Ascendant, 9);
        Assert.Equal(einstein.Midheaven, d.Midheaven, 9);
    }

    [Fact]
    public void The_synastry_reading_says_what_it_is_and_where_its_own_reading_is()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        Assert.DoesNotContain(Interpreter.Compose(SynastryService.Compare(a, b)).Sections, s => s.Heading == "The Davison chart");

        var davison = Repo.Charts.Davison(a, b);
        var section = Interpreter.Compose(SynastryService.Compare(a, b, davison)).Sections[^1];
        Assert.Equal("The Davison chart", section.Heading);
        Assert.Contains(" UT  ·  ", section.Items[0].Meta);
        Assert.Contains("halfway between the two births", section.Items[0].Text);
        Assert.Contains("reading, a wheel and a worksheet of its own", section.Items[1].Text);
    }

    // ── Its own reading ───────────────────────────────────────────────────────

    private static readonly DavisonInterpreter Davison = new(Repo.Data("davison.json"));

    private static DavisonReading Read(NatalChart a, NatalChart b) =>
        Davison.Compose(SynastryService.Compare(a, b, Repo.Charts.Davison(a, b)))!;

    [Fact]
    public void The_corpus_has_every_sign_every_house_and_every_key_aspect()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Repo.Data("davison.json")));
        bool Has(string section, string key) =>
            doc.RootElement.GetProperty(section).TryGetProperty(key, out var v) && !string.IsNullOrWhiteSpace(v.GetString());

        var missing = new List<string>();
        foreach (var sign in Enum.GetValues<ZodiacSign>())
            foreach (string section in new[] { "sun", "moon", "rising" })
                if (!Has(section, sign.Name())) missing.Add($"{section}/{sign.Name()}");
        foreach (string planet in new[] { "Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn" })
            for (int house = 1; house <= 12; house++)
                if (!Has("houses", $"{planet}|{house}")) missing.Add($"houses/{planet}|{house}");
        for (int house = 1; house <= 12; house++)
            if (!Has("houseAreas", house.ToString())) missing.Add($"houseAreas/{house}");

        // Every pair of the seven, in every tone the sky allows: the Sun and Mercury are
        // never more than 28° apart, the Sun and Venus 48°, Mercury and Venus 76°.
        string[] seven = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn"];
        for (int i = 0; i < seven.Length; i++)
            for (int j = i + 1; j < seven.Length; j++)
                foreach (string tone in new[] { "Conjunction", "Flow", "Tension" })
                {
                    string pair = $"{seven[i]}|{seven[j]}";
                    if (tone != "Conjunction" && pair is "Sun|Mercury" or "Sun|Venus") continue;
                    if (tone == "Tension" && pair == "Mercury|Venus") continue;
                    if (!Has("aspects", $"{seven[i]}|{tone}|{seven[j]}")) missing.Add($"aspects/{seven[i]}|{tone}|{seven[j]}");
                }
        foreach (string point in new[] { "Sun", "Moon", "Venus", "Mars", "Saturn", "Ascendant", "Midheaven" })
            if (!Has("contacts", point)) missing.Add($"contacts/{point}");
        Assert.Empty(missing);

        // And no placeholder the interpreter does not fill.
        var stray =
            from section in doc.RootElement.EnumerateObject()
            where section.Value.ValueKind == System.Text.Json.JsonValueKind.Object
            from line in section.Value.EnumerateObject()
            from System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(line.Value.GetString() ?? "", @"\{[^}]*\}")
            where !(section.Name == "contacts" && m.Value is "{name}" or "{theme}")
               && !(section.Name == "toneLinks" && m.Value is "{first}" or "{second}")
            select $"{section.Name}/{line.Name}: {m.Value}";
        Assert.Empty(stray);
    }

    [Fact]
    public void With_both_birth_times_the_reading_has_its_character_houses_aspects_contacts_and_positions()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        var reading = Read(a, b);
        var d = reading.Chart;

        Assert.Equal("Albert Einstein & Elvis Presley", reading.Title);
        Assert.Equal(
            new[] { "The relationship's own chart", "Its character", "Where its life is lived", "How it works", "Each of you in it", "Where everything stands" },
            reading.Sections.Select(s => s.Heading));
        Assert.All(reading.Sections.SelectMany(s => s.Items), i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
        Assert.DoesNotContain(reading.Sections.SelectMany(s => s.Items), i => i.Text.Contains('{'));

        // Sun, Moon and rising sign, each named as the chart has it.
        var character = reading.Sections[1].Items;
        Assert.Equal($"☉ Sun in {d.GetPlanet(Planet.Sun)!.Sign.Name()}", character[0].Title);
        Assert.Equal($"☽ Moon in {d.GetPlanet(Planet.Moon)!.Sign.Name()}", character[1].Title);
        Assert.Equal($"↑ {ZodiacSignExtensions.FromLongitude(d.Ascendant).Name()} rising", character[2].Title);

        // The seven planets, each in the house the chart puts it in.
        var houses = reading.Sections[2].Items;
        Assert.Equal(7, houses.Count);
        Assert.Equal(
            $"♀ Venus in the {ChartInterpreter.Ordinal(d.GetHouseForLongitude(d.GetPlanet(Planet.Venus)!.Longitude))} house",
            houses[3].Title);

        // No more than seven aspects, none of them the generation's own.
        var aspects = reading.Sections[3].Items;
        Assert.InRange(aspects.Count, 1, 7);
        Assert.DoesNotContain(aspects, i =>
            new[] { "Uranus", "Neptune", "Pluto" }.Count(slow => i.Title!.Contains(slow)) == 2);

        // Both angles, then all thirteen bodies, each with its house.
        var lines = reading.Sections[^1].Items[0].Text.Split('\n');
        Assert.Equal(2 + d.Planets.Count, lines.Length);
        Assert.StartsWith("Ascendant ", lines[0]);
        Assert.All(lines.Skip(2), l => Assert.EndsWith(" house", l));
    }

    [Fact]
    public void With_a_birth_time_missing_the_angles_houses_and_Moon_are_left_out()
    {
        var a = Chart("albert-einstein");
        var b = Born("1935-01-08", "12:00", 34.26, -88.7, timed: false);
        var reading = Read(a, b);
        Assert.False(reading.Chart.Timed);

        Assert.DoesNotContain(reading.Sections, s => s.Heading == "Where its life is lived");
        Assert.Contains(reading.Sections[0].Items, i => i.Text.StartsWith("A birth time is missing"));
        Assert.Equal("☉ Sun in " + reading.Chart.GetPlanet(Planet.Sun)!.Sign.Name(), Assert.Single(reading.Sections[1].Items).Title);
        Assert.DoesNotContain(reading.Sections.SelectMany(s => s.Items), i =>
            (i.Title ?? "").Contains("Moon") || (i.Title ?? "").Contains("Ascendant") || (i.Title ?? "").Contains("Midheaven"));

        var lines = reading.Sections[^1].Items[0].Text.Split('\n');
        Assert.Equal(reading.Chart.Planets.Count - 1, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("Moon") || l.Contains("Ascendant") || l.Contains(" house"));
    }

    [Fact]
    public void A_person_whose_planet_stands_on_a_point_of_the_chart_is_named_there()
    {
        // Two people born the same day at the same place, twelve hours apart: the Davison
        // chart is six hours from each, so the slow planets of both stand on its own.
        var a = Born("1990-06-15", "06:00", 51.5, -0.13);
        var b = Born("1990-06-15", "18:00", 51.5, -0.13);
        var contacts = Read(a, b).Sections.Single(s => s.Heading == "Each of you in it").Items;

        Assert.StartsWith("A planet in one person's own chart", contacts[0].Text);
        Assert.InRange(contacts.Count, 2, 7); // the opening line and up to six contacts
        Assert.Contains(contacts.Skip(1), i => i.Title!.EndsWith("on the Davison ♄ Saturn"));
        Assert.All(contacts.Skip(1), i =>
        {
            Assert.Contains(" on the Davison ", i.Title);
            Assert.StartsWith("Conjunction ☌  ·  orb ", i.Meta);
        });
    }

    [Fact]
    public void The_same_two_people_always_get_the_same_reading_whichever_is_first_in_its_substance()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        var one = Read(a, b);
        var again = Read(a, b);
        Assert.Equal(one.Sections.SelectMany(s => s.Items).Select(i => i.Text), again.Sections.SelectMany(s => s.Items).Select(i => i.Text));

        // The chart is the same whoever is named first, and so is what is read from it.
        var swapped = Read(b, a);
        Assert.Equal(one.Sections[1].Items.Select(i => i.Text), swapped.Sections[1].Items.Select(i => i.Text));
        Assert.Equal(one.Sections[2].Items.Select(i => i.Text), swapped.Sections[2].Items.Select(i => i.Text));
    }

    [Fact]
    public void Every_pair_of_the_first_forty_figures_can_be_read()
    {
        var charts = Repo.Figures.Take(40).Select(f => Repo.Charts.Calculate(f)).ToList();
        for (int i = 0; i < charts.Count; i++)
            for (int j = i + 1; j < charts.Count; j++)
            {
                var reading = Read(charts[i], charts[j]);
                Assert.True(reading.Sections.Count >= 5, reading.Title);
                Assert.All(reading.Sections.SelectMany(s => s.Items), x => Assert.False(string.IsNullOrWhiteSpace(x.Text)));
            }
    }

    [Fact]
    public void Its_worksheet_says_it_is_not_a_birth_and_gives_the_moment_it_is_cast_for()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        var d = Repo.Charts.Davison(a, b);
        d.Events = NatalEventsService.Compute(Repo.Charts, d);
        var w = WorksheetService.Build(d);

        string Fact(string label) => w.Facts.Single(f => f.Label == label).Value;
        Assert.Equal("Not a birth: the moment halfway between two births", Fact("Born"));
        Assert.Equal(d.CalculatedForUtc.ToString("yyyy-MM-dd HH:mm:ss") + " UT", Fact("Universal Time"));
        Assert.StartsWith("None", Fact("Time conversion"));
        Assert.Contains("midway between", Fact("Place"));
        Assert.Contains(w.Sections, s => s.Title == "Midpoints");
        Assert.Contains(w.Facts, f => f.Label == "Planetary hour");

        // With a time missing it says so, and does not claim noon.
        var untimed = Repo.Charts.Davison(a, Born("1935-01-08", "12:00", 34.26, -88.7, timed: false));
        var born = WorksheetService.Build(untimed).Facts.Single(f => f.Label == "Born").Value;
        Assert.Contains("a birth time is missing", born);
        Assert.DoesNotContain("noon", born);
    }

    [Fact]
    public void The_reading_and_its_worksheet_make_a_PDF()
    {
        NatalChart a = Chart("albert-einstein"), b = Chart("elvis-presley");
        var synastry = SynastryService.Compare(a, b, Repo.Charts.Davison(a, b));
        var reading = Davison.Compose(synastry)!;

        byte[] pdf = SynastryExportService.DavisonToPdf(synastry, reading, [], WorksheetService.Build(reading.Chart));
        Assert.True(pdf.Length > 20_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
