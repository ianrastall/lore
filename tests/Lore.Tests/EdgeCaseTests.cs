using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.Tests;

// Cases at the edges of the calendar, the clock and the globe, each of which once came
// out wrong (the review of 2.7.0, 9 October 2026).
public class EdgeCaseTests
{
    private static Celebrity At(string date, string time, double latitude, double longitude = 0, string? zone = null,
        bool timed = true) => new()
    {
        Id = "edge", Name = "Edge Case", Category = "Test",
        BirthDate = date, BirthTime = time, BirthTimeKnown = timed,
        BirthPlace = "Somewhere", Latitude = latitude, Longitude = longitude,
        TimeZoneId = zone, UtcOffsetFixed = zone is null, UtcOffsetHours = 0,
    };

    // ── Day or night inside the polar circles ─────────────────────────────────

    // At 69.65° the Sun does not set at midsummer nor rise at midwinter. The Ascendant is
    // by convention kept in the east there, so which half of the zodiac is "above" it
    // says nothing reliable; only the Sun's altitude does.
    [Theory]
    [InlineData("2026-06-21", "00:00", 69.65, true)]    // the midnight sun
    [InlineData("2026-12-21", "12:00", 69.65, false)]   // noon in the polar night
    [InlineData("2026-12-21", "00:00", -69.65, true)]   // and the same in the south
    [InlineData("2026-06-21", "12:00", -69.65, false)]
    public void Day_or_night_near_the_poles_follows_the_Suns_altitude(string date, string time, double latitude, bool day)
    {
        Assert.Equal(day, Repo.Charts.Calculate(At(date, time, latitude)).IsDayChart);
    }

    // ── Patterns across a sign boundary ───────────────────────────────────────

    [Fact]
    public void A_pattern_names_an_element_or_modality_only_if_all_its_points_share_it()
    {
        int mixed = 0;
        foreach (var figure in Repo.Figures)
        {
            var chart = Repo.Charts.Calculate(figure);
            ZodiacSign SignOf(NatalPoint p) => ZodiacSignExtensions.FromLongitude(chart.LongitudeOf(p)!.Value);
            foreach (var pattern in AspectPatternService.Detect(chart))
            {
                if (pattern.Type is PatternType.Stellium or PatternType.Yod or PatternType.MysticRectangle) continue;
                // (A kite's element is that of its trine, the first three points.)
                if (pattern.Element is { } element)
                    Assert.All(pattern.Points.Take(3), p => Assert.Equal(element, SignOf(p).GetElement()));
                else if (pattern.Modality is { } modality)
                    Assert.All(pattern.Points, p => Assert.Equal(modality, SignOf(p).GetModality()));
                else
                    mixed++;
            }
        }
        Assert.True(mixed > 0); // the figures are kept, only not described as of one kind
    }

    [Fact]
    public void A_grand_trine_across_mixed_signs_is_not_read_as_one_element()
    {
        // Janis Joplin: Sun in Capricorn, Saturn in Gemini, Neptune in Libra. (Pluto,
        // opposite the Sun, makes a kite of it.)
        var chart = Repo.Charts.Calculate(Repo.Figure("janis-joplin"));
        var trine = Assert.Single(AspectPatternService.Detect(chart), p =>
            p.Type == PatternType.Kite && p.Points.Contains(NatalPoint.Of(Planet.Saturn)) &&
            p.Points.Contains(NatalPoint.Of(Planet.Sun)) && p.Points.Contains(NatalPoint.Of(Planet.Neptune)));
        Assert.Null(trine.Element);

        var patterns = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(chart)
            .Single(s => s.Heading == "Chart Patterns");
        Assert.Contains(patterns.Paragraphs, p => p.StartsWith("Kite across mixed signs — Sun, Saturn, and Neptune form a grand trine"));
        Assert.DoesNotContain(patterns.Paragraphs, p => p.StartsWith("Kite in Earth") || p.StartsWith("Grand Trine in Earth"));
    }

    // ── The solar return at the turn of the year ──────────────────────────────

    [Fact]
    public void A_return_that_falls_on_31_December_is_in_force_that_day()
    {
        // Born at 00:00 UT on 1 January 2000: the Sun is back at 13:08 UT on 31 December
        // 2026, though that return belongs with the birthday of 1 January 2027.
        var natal = Repo.Charts.Calculate(At("2000-01-01", "00:00", 0));
        var timing = new TimingService(Repo.Charts);

        var onTheDay = timing.SolarReturnInForce(natal, new DateOnly(2026, 12, 31), DateTimeZone.Utc)!;
        Assert.Equal(new DateTime(2026, 12, 31, 13, 8, 30), onTheDay.Utc, TimeSpan.FromMinutes(1));

        var dayBefore = timing.SolarReturnInForce(natal, new DateOnly(2026, 12, 30), DateTimeZone.Utc)!;
        Assert.Equal(new DateTime(2025, 12, 31, 7, 17, 25), dayBefore.Utc, TimeSpan.FromMinutes(1));

        // The reader's own calendar decides the day: in Kiritimati (UT+14) that return
        // is on 1 January, and in Honolulu (UT−10) still on the 31st.
        var kiritimati = DateTimeZoneProviders.Tzdb["Pacific/Kiritimati"];
        Assert.Equal(dayBefore.Utc, timing.SolarReturnInForce(natal, new DateOnly(2026, 12, 31), kiritimati)!.Utc);
        Assert.Equal(onTheDay.Utc, timing.SolarReturnInForce(natal, new DateOnly(2027, 1, 1), kiritimati)!.Utc);
        var honolulu = DateTimeZoneProviders.Tzdb["Pacific/Honolulu"];
        Assert.Equal(onTheDay.Utc, timing.SolarReturnInForce(natal, new DateOnly(2026, 12, 31), honolulu)!.Utc);
    }

    // ── A forecast pass broken by a station ───────────────────────────────────

    [Fact]
    public void A_planet_that_stations_just_outside_the_orb_makes_two_passes_not_one()
    {
        // Mercury turns retrograde on 26 February 2026 a hair over a degree past the
        // square to Andrew Garfield's node: out of orb at 03:00 UT and back in at 10:36,
        // with a sample inside the orb on either side.
        var natal = Repo.Charts.Calculate(Repo.Figure("andrew-garfield"));
        var passes = new TransitService(Repo.Charts).Forecast(natal, new DateOnly(2026, 2, 11), 31, DateTimeZone.Utc)
            .Where(p => p.Mover == Planet.Mercury && p.Target == NatalPoint.Of(Planet.NorthNode) && p.Aspect == AspectType.Square)
            .OrderBy(p => p.PeakUtc).ToList();

        Assert.Equal(2, passes.Count);
        Assert.Equal(new DateTime(2026, 2, 21, 7, 13, 11), passes[0].EnterUtc!.Value, TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTime(2026, 2, 26, 3, 0, 2), passes[0].LeaveUtc!.Value, TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTime(2026, 2, 26, 10, 36, 25), passes[1].EnterUtc!.Value, TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTime(2026, 3, 3, 9, 17, 39), passes[1].LeaveUtc!.Value, TimeSpan.FromMinutes(1));
    }

    // ── An unknown time that still has a time written beside it ───────────────

    [Fact]
    public void A_chart_marked_time_unknown_is_cast_for_noon_whatever_time_it_carries()
    {
        var stale = At("1980-05-17", "00:00", 51.5, -0.13, "Europe/London", timed: false);
        Assert.Equal(new TimeOnly(12, 0), stale.GetBirthTime());
        Assert.Equal(new DateTime(1980, 5, 17, 11, 0, 0), BirthTimeResolver.ToUtc(stale)); // noon BST
        Assert.Equal(ZodiacSign.Cancer, Repo.Charts.Calculate(stale).GetPlanet(Planet.Moon)!.Sign);
    }

    // ── Clock times across a change of the clocks ─────────────────────────────

    [Fact]
    public void The_birth_time_check_gives_real_clock_times_when_the_clocks_change_inside_it()
    {
        // London, 25 March 1990: at 01:00 the clocks went to 02:00. Two hours either
        // side of 00:30 runs from 22:30 the evening before to 03:30, and no change can
        // be timed between 01:00 and 02:00, an hour that day did not have.
        var person = At("1990-03-25", "00:30", 51.5, -0.13, "Europe/London");
        var result = TimeSensitivityService.Analyse(Repo.Charts, person, 120)!;

        Assert.Equal("22:30 to 03:30, clock time at the birthplace", result.Window);
        Assert.Contains(result.Changes, c => c.StartsWith("Midheaven") && c.Contains("until 02:44"));
        Assert.DoesNotContain(result.Changes, c => c.Contains("until 01:"));
    }

    // ── A calendar day that a place never had ─────────────────────────────────

    [Fact]
    public void The_day_before_one_the_calendar_skipped_still_has_an_end()
    {
        // Samoa crossed the date line at the end of 29 December 2011: the next day there
        // was the 31st.
        var apia = DateTimeZoneProviders.Tzdb["Pacific/Apia"];
        var person = At("2011-12-29", "", -13.83, -171.77, "Pacific/Apia", timed: false);

        var (start, end, _) = BirthTimeResolver.LocalDay(person);
        Assert.Equal(TimeSpan.FromHours(24), end - start);
        Assert.NotNull(TimeSensitivityService.AnalyseDay(Repo.Charts, person));

        // And for a reader there, looking at anyone's chart on that day.
        var natal = Repo.Charts.Calculate(Demo.Person());
        Assert.NotNull(new TimingService(Repo.Charts).SolarReturnInForce(natal, new DateOnly(2011, 12, 29), apia));
        Assert.NotEmpty(new TransitService(Repo.Charts).Scan(natal, new DateOnly(2011, 12, 29), apia).Midday);
    }

    // ── The true Lilith turning back ──────────────────────────────────────────

    [Fact]
    public void The_true_Lilith_is_marked_retrograde_when_it_runs_backwards_and_the_mean_one_never()
    {
        double jd = SwissEphemeris.DateTimeToJulianDay(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
        PlanetPosition Lilith(LilithType type) =>
            new ChartService(Repo.Ephemeris) { Settings = new ChartSettings { Lilith = type } }
                .CalculateSky(jd).Single(p => p.Planet == Planet.Lilith);

        var real = Lilith(LilithType.True);
        Assert.True(real.SpeedLongitude < 0);
        Assert.True(real.IsRetrograde);
        Assert.Contains("℞", real.MotionMark);
        Assert.False(Lilith(LilithType.Mean).IsRetrograde);

        // The node runs backwards as a matter of course, true or mean, and is not marked.
        var node = new ChartService(Repo.Ephemeris) { Settings = new ChartSettings { Node = NodeType.True } }
            .CalculateSky(jd).Single(p => p.Planet == Planet.NorthNode);
        Assert.False(node.IsRetrograde);
    }
}
