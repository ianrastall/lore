using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// The forecast's sky calendar: New and Full Moons, eclipses, stations and the slow
// planets' changes of sign. The dates are checked against the published almanac.
public class SkyCalendarTests
{
    private static readonly DateTimeZone Zone = DateTimeZone.Utc;
    private static readonly TransitService Transits = new(Repo.Charts);
    private static readonly NatalChart Einstein = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));

    private static readonly IReadOnlyList<SkyEvent> Year2026 = Transits.SkyCalendar(Einstein, new DateOnly(2026, 1, 1), 365, Zone);

    private static PlanetPosition At(DateTime utc, Planet p) =>
        Repo.Charts.CalculateBody(SwissEphemeris.DateTimeToJulianDay(utc), p)!;

    [Fact]
    public void A_year_has_its_New_and_Full_Moons_each_exact_to_the_second()
    {
        var newMoons = Year2026.Where(e => e.Kind is SkyEventKind.NewMoon or SkyEventKind.SolarEclipse).ToList();
        var fullMoons = Year2026.Where(e => e.Kind is SkyEventKind.FullMoon or SkyEventKind.LunarEclipse).ToList();
        Assert.InRange(newMoons.Count, 12, 13);
        Assert.InRange(fullMoons.Count, 12, 13);

        Assert.All(newMoons, e => Assert.True(Repo.ArcMinutesBetween(At(e.Utc, Planet.Moon).Longitude, At(e.Utc, Planet.Sun).Longitude) < 0.01));
        Assert.All(fullMoons, e => Assert.True(Repo.ArcMinutesBetween(At(e.Utc, Planet.Moon).Longitude, At(e.Utc, Planet.Sun).Longitude + 180) < 0.01));
        Assert.All(newMoons.Concat(fullMoons), e =>
        {
            Assert.Equal(Planet.Moon, e.Body);
            Assert.Equal(ZodiacSignExtensions.FromLongitude(e.Longitude), e.Sign);
            Assert.Equal(Einstein.GetHouseForLongitude(e.Longitude), e.NatalHouse);
        });

        // They alternate, about a fortnight apart.
        var lunations = Year2026.Where(e => e.Body == Planet.Moon).ToList();
        for (int i = 1; i < lunations.Count; i++)
            Assert.InRange((lunations[i].Utc - lunations[i - 1].Utc).TotalDays, 13.5, 16);
    }

    [Fact]
    public void The_eclipses_of_2026_are_found_on_their_dates_and_of_their_kinds()
    {
        var eclipses = Year2026.Where(e => e.IsEclipse).ToList();
        Assert.Equal(
            [
                (SkyEventKind.SolarEclipse, new DateOnly(2026, 2, 17), "annular"),
                (SkyEventKind.LunarEclipse, new DateOnly(2026, 3, 3), "total"),
                (SkyEventKind.SolarEclipse, new DateOnly(2026, 8, 12), "total"),
                (SkyEventKind.LunarEclipse, new DateOnly(2026, 8, 28), "partial"),
            ],
            eclipses.Select(e => (e.Kind, DateOnly.FromDateTime(e.Utc), e.EclipseType!)));

        Assert.All(Year2026.Where(e => !e.IsEclipse), e => Assert.Null(e.EclipseType));
    }

    [Fact]
    public void Stations_are_where_a_planet_stops_and_a_retrograde_says_when_it_ends()
    {
        var stations = Year2026.Where(e => e.Kind == SkyEventKind.Station).ToList();
        Assert.All(stations, e =>
        {
            Assert.True(Math.Abs(At(e.Utc, e.Body).SpeedLongitude) < 1e-5, $"{e.Body} {e.Utc:u}");
            Assert.True(At(e.Utc.AddDays(2), e.Body).IsRetrograde == e.Retrograde);
            Assert.Equal(e.Retrograde, e.DirectUtc is not null);
        });

        // Mercury turns retrograde three times in 2026: 26 February, 29 June, 24 October.
        var mercury = stations.Where(e => e.Body == Planet.Mercury && e.Retrograde).ToList();
        Assert.Equal([new DateOnly(2026, 2, 26), new DateOnly(2026, 6, 29), new DateOnly(2026, 10, 24)],
            mercury.Select(e => DateOnly.FromDateTime(e.Utc)));
        Assert.All(mercury, e => Assert.InRange((e.DirectUtc!.Value - e.Utc).TotalDays, 19, 25));

        // Each retrograde ends at the same planet's next station, which is direct.
        foreach (var e in stations.Where(e => e.Retrograde))
        {
            var next = stations.FirstOrDefault(s => s.Body == e.Body && s.Utc > e.Utc);
            if (next is not null) Assert.True(!next.Retrograde && Math.Abs((next.Utc - e.DirectUtc!.Value).TotalSeconds) < 2);
            else Assert.True(e.DirectUtc > new DateTime(2027, 1, 1)); // past the end of the forecast
        }

        // The Sun, Moon, node and Lilith never appear.
        Assert.DoesNotContain(stations, e => e.Body is Planet.Sun or Planet.Moon or Planet.NorthNode or Planet.Lilith);
    }

    [Fact]
    public void The_slow_planets_changes_of_sign_are_found()
    {
        var ingresses = Year2026.Where(e => e.Kind == SkyEventKind.Ingress).ToList();
        Assert.All(ingresses, e =>
        {
            Assert.True(e.Body is Planet.Jupiter or Planet.Saturn or Planet.Uranus or Planet.Neptune or Planet.Pluto);
            // A minute before it is in the neighbouring sign, a minute after in the one named.
            Assert.Equal(e.Sign, At(e.Utc.AddMinutes(1), e.Body).Sign);
            var before = At(e.Utc.AddMinutes(-1), e.Body).Sign;
            Assert.Equal((ZodiacSign)(((int)e.Sign + (e.Retrograde ? 1 : 11)) % 12), before);
            Assert.Equal(e.Retrograde, At(e.Utc, e.Body).IsRetrograde);
            Assert.Equal(Einstein.GetHouseForLongitude(At(e.Utc.AddMinutes(1), e.Body).Longitude), e.NatalHouse);
        });

        // 2026: Neptune into Aries (26 January), Saturn into Aries (14 February), Uranus
        // into Gemini (26 April), Jupiter into Leo (30 June).
        Assert.Equal(
            [
                (Planet.Neptune, ZodiacSign.Aries, new DateOnly(2026, 1, 26)),
                (Planet.Saturn, ZodiacSign.Aries, new DateOnly(2026, 2, 14)),
                (Planet.Uranus, ZodiacSign.Gemini, new DateOnly(2026, 4, 26)),
                (Planet.Jupiter, ZodiacSign.Leo, new DateOnly(2026, 6, 30)),
            ],
            ingresses.Select(e => (e.Body, e.Sign, DateOnly.FromDateTime(e.Utc))));
        Assert.DoesNotContain(ingresses, e => e.Retrograde);

        // Going backwards over a boundary is an ingress into the sign before: Uranus
        // slipped back from Gemini into Taurus in November 2025.
        var back = Transits.SkyCalendar(Einstein, new DateOnly(2025, 10, 1), 91, Zone)
            .Single(e => e.Kind == SkyEventKind.Ingress && e.Body == Planet.Uranus);
        Assert.True(back.Retrograde);
        Assert.Equal(ZodiacSign.Taurus, back.Sign);
        Assert.Equal(new DateOnly(2025, 11, 8), DateOnly.FromDateTime(back.Utc));
    }

    [Fact]
    public void Everything_is_in_order_inside_the_stretch_and_the_same_for_everyone()
    {
        Assert.Equal(Year2026.OrderBy(e => e.Utc).Select(e => e.Utc), Year2026.Select(e => e.Utc));
        Assert.All(Year2026, e => Assert.InRange(e.Utc, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1)));

        // A chart with no birth time gets the same events, without houses.
        var untimed = Repo.Charts.Calculate(Demo.Person(timed: false));
        var theirs = Transits.SkyCalendar(untimed, new DateOnly(2026, 1, 1), 365, Zone);
        Assert.Equal(Year2026.Select(e => (e.Kind, e.Utc, e.Body)), theirs.Select(e => (e.Kind, e.Utc, e.Body)));
        Assert.All(theirs, e => Assert.Null(e.NatalHouse));
    }

    [Fact]
    public void The_forecast_sets_the_sky_among_the_transits_in_date_order()
    {
        var start = new DateOnly(2026, 8, 1);
        var interpreter = new DailyInterpreter(Repo.Data("daily.json"));
        var passes = Transits.Forecast(Einstein, start, 91, Zone);
        var sky = Transits.SkyCalendar(Einstein, start, 91, Zone);

        var without = interpreter.ComposeForecast(Einstein, passes, start, 91, Zone);
        var with = interpreter.ComposeForecast(Einstein, passes, start, 91, Zone, sky);
        Assert.Empty(without.Sky);
        Assert.Equal(sky, with.Sky);
        Assert.Equal(without.Sections.Sum(s => s.Items.Count) + sky.Count, with.Sections.Sum(s => s.Items.Count));

        var august = with.Sections.Single(s => s.Heading == "August 2026").Items;
        var eclipse = august.Single(i => i.Title!.StartsWith("☉ Solar eclipse (total) at "));
        Assert.Contains("Leo", eclipse.Title);
        Assert.Contains("a New Moon", eclipse.Meta);
        Assert.Contains("house (", eclipse.Meta);
        Assert.Contains("A solar eclipse is a New Moon", eclipse.Text);
        Assert.Contains(august, i => i.Title!.StartsWith("☽ Lunar eclipse (partial) at "));
        Assert.Contains(with.Sections.SelectMany(s => s.Items), i => i.Title!.StartsWith("☽ Full Moon at "));

        var mercury = with.Sections.SelectMany(s => s.Items).Single(i => i.Title!.StartsWith("☿ Mercury turns retrograde at "));
        Assert.Contains("retrograde until ", mercury.Meta);
        Assert.Contains("Mercury slows to a stop and turns back.", mercury.Text);
        Assert.Contains("Double-check details", mercury.Text); // Mercury's own line follows

        // No line is left with a placeholder in it.
        Assert.DoesNotContain(with.Sections.SelectMany(s => s.Items), i => i.Text.Contains('{'));
        Assert.Contains("Solar eclipse (total)", System.Text.Encoding.UTF8.GetString(DailyInterpreter.ForecastToText(with)));
    }
}
