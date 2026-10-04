using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// The forecast: every pass a planet makes within orb of a natal point over a stretch
// of days. It must agree with the day-by-day scan the Daily view already trusts.
public class ForecastTests
{
    private static readonly DateTimeZone Zone = DateTimeZone.Utc;
    private static readonly DateOnly Start = new(2026, 10, 1);

    private static NatalChart Chart(string id = "albert-einstein") => Repo.Charts.Calculate(Repo.Figure(id));
    private static TransitService Transits => new(Repo.Charts);

    [Fact]
    public void Every_pass_is_in_order_within_orb_and_inside_its_own_dates()
    {
        var passes = Transits.Forecast(Chart(), Start, 91, Zone);
        Assert.True(passes.Count > 20);
        Assert.Equal(passes.OrderBy(p => p.PeakUtc).Select(p => p.PeakUtc), passes.Select(p => p.PeakUtc));

        var from = new DateTime(2026, 10, 1);
        var to = from.AddDays(91);
        Assert.All(passes, p =>
        {
            Assert.NotEqual(Planet.Moon, p.Mover);
            Assert.True(p.Aspect.IsMajor());
            Assert.InRange(p.MinOrb, 0, TransitService.Orb);
            Assert.InRange(p.PeakUtc, from.AddMinutes(-1), to.AddMinutes(1));
            if (p.EnterUtc is { } e) Assert.True(e <= p.PeakUtc.AddSeconds(1));
            if (p.LeaveUtc is { } l) Assert.True(l >= p.PeakUtc.AddSeconds(-1));
            if (p.IsExact) Assert.Equal(0, p.MinOrb);
            else Assert.True(p.MinOrb > 0);
        });
    }

    [Fact]
    public void An_exact_moment_really_is_exact_and_the_orb_really_is_one_degree_at_each_end()
    {
        var chart = Chart();
        foreach (var p in Transits.Forecast(chart, Start, 91, Zone).Where(p => p.IsExact).Take(40))
        {
            double natal = chart.LongitudeOf(p.Target)!.Value;
            double Off(DateTime utc)
            {
                double lon = Repo.Charts.CalculateBody(SwissEphemeris.DateTimeToJulianDay(utc), p.Mover)!.Longitude;
                double sep = Math.Abs(lon - natal) % 360; if (sep > 180) sep = 360 - sep;
                return Math.Abs(sep - p.Aspect.Angle());
            }

            // DateTimeToJulianDay keeps whole seconds, so allow what a body covers in one.
            Assert.All(p.ExactUtc, t => Assert.True(Off(t) < 0.001, $"{p.Mover} {p.Aspect} {p.Target.Name}: {Off(t)}"));
            if (p.EnterUtc is { } e) Assert.InRange(Off(e), 0.99, 1.01);
            if (p.LeaveUtc is { } l) Assert.InRange(Off(l), 0.99, 1.01);
        }
    }

    [Fact]
    public void It_agrees_with_the_daily_scan()
    {
        // Whatever the Daily view finds exact on a given day (Moon aside) must be in the
        // forecast, at the same moment.
        var chart = Chart();
        var transits = Transits;
        var passes = transits.Forecast(chart, Start, 30, Zone);
        int checkedCount = 0;

        for (int day = 0; day < 30; day++)
        {
            var sky = transits.Scan(chart, Start.AddDays(day), Zone);
            foreach (var e in sky.Events.Where(e => e.Mover != Planet.Moon && e.Phase == TransitPhase.Exact))
            {
                Assert.Contains(passes, p =>
                    p.Mover == e.Mover && p.Target == e.Target && p.Aspect == e.Aspect &&
                    p.ExactUtc.Any(t => Math.Abs((t - e.ExactUtc!.Value).TotalMinutes) < 2));
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 5, $"only {checkedCount} exact transits in the month to compare");
    }

    [Fact]
    public void Leaving_out_the_fast_planets_leaves_only_slow_ones_and_changes_nothing_else()
    {
        var chart = Chart();
        var all = Transits.Forecast(chart, Start, 91, Zone);
        var slow = Transits.Forecast(chart, Start, 91, Zone, includeFast: false);

        Assert.All(slow, p => Assert.True(p.IsBackground));
        Assert.Equal(all.Count(p => p.IsBackground), slow.Count);
        Assert.Contains(all, p => !p.IsBackground);
    }

    [Fact]
    public void A_chart_with_no_birth_time_has_no_transits_to_its_angles_or_Moon()
    {
        var untimed = Repo.Charts.Calculate(new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTimeKnown = false,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        });
        var passes = Transits.Forecast(untimed, Start, 91, Zone);
        Assert.NotEmpty(passes);
        Assert.DoesNotContain(passes, p => p.Target.IsAngle || p.Target == NatalPoint.Of(Planet.Moon));
        Assert.All(passes, p => Assert.Null(p.TargetHouse));
    }

    [Fact]
    public void The_reading_is_grouped_by_month_and_every_pass_gets_a_line()
    {
        var chart = Chart();
        var passes = Transits.Forecast(chart, Start, 91, Zone);
        var reading = new DailyInterpreter(Repo.Data("daily.json")).ComposeForecast(chart, passes, Start, 91, Zone);

        Assert.Equal("1 October 2026 to 30 December 2026", reading.RangeText);
        Assert.Equal(new[] { "October 2026", "November 2026", "December 2026" }, reading.Sections.Select(s => s.Heading));
        Assert.Equal(passes.Count, reading.Sections.Sum(s => s.Items.Count));
        Assert.All(reading.Sections.SelectMany(s => s.Items), i =>
        {
            Assert.Contains(" your natal ", i.Title);
            Assert.Matches("^(Exact|Closest|Already past exact|Still building) ", i.Meta!);
            Assert.False(string.IsNullOrWhiteSpace(i.Text));
        });

        string text = System.Text.Encoding.UTF8.GetString(DailyInterpreter.ForecastToText(reading));
        Assert.Contains("NOVEMBER 2026", text);
    }

    [Fact]
    public void A_planet_that_dips_into_orb_and_turns_back_between_samples_is_still_found()
    {
        // Find a moment Mercury stations retrograde: its speed goes from forward to
        // backward, so its longitude is at a maximum.
        double Jd(DateTime utc) => SwissEphemeris.DateTimeToJulianDay(utc);
        double Speed(double jd) => Repo.Charts.CalculateBody(jd, Planet.Mercury)!.SpeedLongitude;

        var day = new DateTime(2026, 1, 1);
        while (!(Speed(Jd(day)) > 0 && Speed(Jd(day.AddDays(1))) < 0)) day = day.AddDays(1);
        double lo = Jd(day), hi = Jd(day.AddDays(1));
        for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (Speed(mid) > 0) lo = mid; else hi = mid; }
        double peak = Repo.Charts.CalculateBody(lo, Planet.Mercury)!.Longitude;

        // A natal point 0.9995 deg beyond the furthest Mercury gets: inside the one-degree orb
        // only for the few hours around the station, and outside it at the half-day samples.
        var natal = new NatalChart
        {
            Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTimeKnown = false },
            Planets = [new PlanetPosition { Planet = Planet.Sun, Longitude = (peak + 0.9995) % 360 }],
            Houses = [],
            Aspects = [],
        };

        var start = DateOnly.FromDateTime(day).AddDays(-7);
        var pass = Assert.Single(Transits.Forecast(natal, start, 14, Zone),
            p => p.Mover == Planet.Mercury && p.Aspect == AspectType.Conjunction);
        Assert.False(pass.IsExact);
        Assert.InRange(pass.MinOrb, 0.999, 1.0);
        Assert.NotNull(pass.EnterUtc);
        Assert.NotNull(pass.LeaveUtc);
        Assert.InRange((pass.PeakUtc - SwissEphemeris.JulianDayToDateTime(lo)).TotalHours, -1, 1);
    }
}
