using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// Sunrise, sunset and the planetary hours at the reader's own place.
public class PlanetaryHoursTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly HomePlace Greenwich = new("London, United Kingdom", 51.5074, -0.1278);
    private static readonly TransitService Transits = new(Repo.Charts);
    private static readonly NatalChart Natal = Repo.Charts.Calculate(Demo.Person());

    private static DaySky Scan(DateOnly date, HomePlace? home) => Transits.Scan(Natal, date, London, home);

    private static void Near(DateTime expected, DateTime actual, double minutes = 3) =>
        Assert.True(Math.Abs((actual - expected).TotalMinutes) < minutes, $"expected about {expected:u}, got {actual:u}");

    [Fact]
    public void Sunrise_and_sunset_agree_with_the_almanac()
    {
        // London at the summer solstice of 2026: sunrise 04:43 and sunset 21:21 BST.
        var summer = Scan(new DateOnly(2026, 6, 21), Greenwich).Hours!;
        Near(new DateTime(2026, 6, 21, 3, 43, 0), summer.SunriseUtc);
        Near(new DateTime(2026, 6, 21, 20, 21, 0), summer.SunsetUtc);
        Near(new DateTime(2026, 6, 22, 3, 43, 0), summer.NextSunriseUtc);

        // And at the winter solstice: 08:04 and 15:53 GMT.
        var winter = Scan(new DateOnly(2026, 12, 21), Greenwich).Hours!;
        Near(new DateTime(2026, 12, 21, 8, 4, 0), winter.SunriseUtc);
        Near(new DateTime(2026, 12, 21, 15, 53, 0), winter.SunsetUtc);
    }

    [Fact]
    public void The_day_and_the_night_are_each_cut_into_twelve_equal_hours()
    {
        var h = Scan(new DateOnly(2026, 6, 21), Greenwich).Hours!;
        Assert.Equal(12, h.Day.Count);
        Assert.Equal(12, h.Night.Count);

        var all = h.Day.Concat(h.Night).ToList();
        Assert.Equal(h.SunriseUtc, all[0].StartUtc);
        Assert.True(Math.Abs((h.Day[^1].EndUtc - h.SunsetUtc).TotalSeconds) < 1);
        Assert.True(Math.Abs((h.Night[^1].EndUtc - h.NextSunriseUtc).TotalSeconds) < 1);
        for (int i = 1; i < all.Count; i++)
            Assert.True(Math.Abs((all[i].StartUtc - all[i - 1].EndUtc).TotalSeconds) < 1);

        // Midsummer: the hours of the day are long (about 83 minutes) and those of the night short (about 37).
        Assert.All(h.Day, x => Assert.InRange((x.EndUtc - x.StartUtc).TotalMinutes, 82, 84));
        Assert.All(h.Night, x => Assert.InRange((x.EndUtc - x.StartUtc).TotalMinutes, 36, 38));
    }

    [Fact]
    public void The_hours_run_in_the_Chaldean_order_from_the_ruler_of_the_day()
    {
        // 21 June 2026 is a Sunday.
        var h = Scan(new DateOnly(2026, 6, 21), Greenwich).Hours!;
        Assert.Equal(Planet.Sun, h.DayRuler);
        Assert.Equal(
            [Planet.Sun, Planet.Venus, Planet.Mercury, Planet.Moon, Planet.Saturn, Planet.Jupiter, Planet.Mars,
             Planet.Sun, Planet.Venus, Planet.Mercury, Planet.Moon, Planet.Saturn],
            h.Day.Select(x => x.Ruler));
        Assert.Equal(Planet.Jupiter, h.Night[0].Ruler);
        Assert.Equal(Planet.Mercury, h.Night[^1].Ruler);

        // The hour after the last of the night is the first of Monday, and is the Moon's:
        // that is why the days of the week come in the order they do.
        Planet[] week = [Planet.Sun, Planet.Moon, Planet.Mars, Planet.Mercury, Planet.Jupiter, Planet.Venus, Planet.Saturn];
        for (int i = 0; i < 7; i++)
        {
            var day = Scan(new DateOnly(2026, 6, 21).AddDays(i), Greenwich).Hours!;
            Assert.Equal(week[i], day.DayRuler);
            Assert.Equal(week[i], day.Day[0].Ruler);
        }
    }

    [Fact]
    public void Where_the_Sun_does_not_set_there_are_no_hours()
    {
        var tromso = new HomePlace("Tromsø, Norway", 69.6492, 18.9553);
        var oslo = DateTimeZoneProviders.Tzdb["Europe/Oslo"];
        Assert.Null(Transits.Scan(Natal, new DateOnly(2026, 6, 21), oslo, tromso).Hours);   // midnight Sun
        Assert.Null(Transits.Scan(Natal, new DateOnly(2026, 12, 21), oslo, tromso).Hours);  // polar night
        Assert.NotNull(Transits.Scan(Natal, new DateOnly(2026, 3, 20), oslo, tromso).Hours);
    }

    [Fact]
    public void The_reading_gives_the_hours_only_when_a_place_is_set()
    {
        var interpreter = new DailyInterpreter(Repo.Data("daily.json"));
        var date = new DateOnly(2026, 6, 21);

        var without = interpreter.Compose(Natal, Scan(date, null), London);
        Assert.Null(Scan(date, null).Hours);
        Assert.DoesNotContain(without.Sections, s => s.Heading == "Planetary hours");

        var with = interpreter.Compose(Natal, Scan(date, Greenwich), London);
        var section = with.Sections[^1];
        Assert.Equal("Planetary hours", section.Heading);
        Assert.StartsWith("☉ Sun's day  ·  sunrise ", section.Items[0].Title);
        Assert.StartsWith("at London, United Kingdom  ·  ", section.Items[0].Meta);
        Assert.StartsWith("Sunday is the Sun's day", section.Items[0].Text);
        Assert.Equal("each a twelfth of the daylight: 83 minutes", section.Items[1].Meta);
        Assert.Equal(12, section.Items[1].Text.Split('\n').Length);
        Assert.Equal(12, section.Items[2].Text.Split('\n').Length);
        Assert.EndsWith("☉ Sun", section.Items[1].Text.Split('\n')[0]);

        // Everything above it is the same reading as before.
        Assert.Equal(without.Sections.Select(s => s.Heading), with.Sections.SkipLast(1).Select(s => s.Heading));
    }

    [Fact]
    public void The_place_is_saved_and_cleared()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-home-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(new SettingsService(dir).LoadHome());

            new SettingsService(dir).SaveHome(Greenwich);
            Assert.Equal(Greenwich, new SettingsService(dir).LoadHome());
            // It is kept apart from the calculation settings, which are untouched.
            Assert.Equal(ChartSettings.Default, new SettingsService(dir).Load());

            File.WriteAllText(Path.Combine(dir, "home.json"), """{ "Name": "Nowhere", "Latitude": 123, "Longitude": 0 }""");
            Assert.Null(new SettingsService(dir).LoadHome());
            File.WriteAllText(Path.Combine(dir, "home.json"), "{ not json");
            Assert.Null(new SettingsService(dir).LoadHome());

            new SettingsService(dir).SaveHome(Greenwich);
            new SettingsService(dir).SaveHome(null);
            Assert.Null(new SettingsService(dir).LoadHome());
            Assert.False(File.Exists(Path.Combine(dir, "home.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
