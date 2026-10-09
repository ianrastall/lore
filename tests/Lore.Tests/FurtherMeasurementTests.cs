using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The measurements added in 2.8.0: the sky by equator and horizon, motion and stations,
// the lunations before birth, the planetary hour, midpoints, antiscia, almutens, the
// spread of the planets, and the kite and mystic rectangle.
public class FurtherMeasurementTests
{
    private static NatalChart Chart(string id)
    {
        var chart = Repo.Charts.Calculate(Repo.Figure(id));
        chart.Events = NatalEventsService.Compute(Repo.Charts, chart);
        return chart;
    }

    private static PlanetPosition At(Planet planet, double longitude, double speed = 1) =>
        new() { Planet = planet, Longitude = longitude, SpeedLongitude = speed };

    private static NatalChart Made(params PlanetPosition[] planets) => new()
    {
        Celebrity = Demo.Person(),
        Planets = planets,
        Houses = Enumerable.Range(1, 12).Select(h => new HouseCusp { House = h, Longitude = (h - 1) * 30 }).ToList(),
        Aspects = [],
        Ascendant = 0,
        Midheaven = 270,
    };

    // ── Horizon ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_point_on_the_meridian_stands_due_south_at_the_height_geometry_gives()
    {
        // At 50° north, a point on the equator crossing the meridian is 40° up, due south.
        var (altitude, azimuth) = NatalMetricsService.Horizon(armc: 100, latitude: 50, rightAscension: 100, declination: 0);
        Assert.Equal(40, altitude, 6);
        Assert.Equal(180, azimuth, 6);

        // Six hours later the same point is setting, due west.
        (altitude, azimuth) = NatalMetricsService.Horizon(armc: 190, latitude: 50, rightAscension: 100, declination: 0);
        Assert.Equal(0, altitude, 6);
        Assert.Equal(270, azimuth, 6);

        // The pole star's place: as high as the latitude, due north.
        (altitude, azimuth) = NatalMetricsService.Horizon(armc: 33, latitude: 50, rightAscension: 0, declination: 90);
        Assert.Equal(50, altitude, 6);
    }

    [Fact]
    public void Every_timed_figures_Sun_is_up_by_day_and_down_by_night_and_the_Midheaven_planets_are_high()
    {
        foreach (var f in Repo.Figures.Where(f => f.BirthTimeKnown).Take(200))
        {
            var chart = Repo.Charts.Calculate(f);
            var sky = NatalMetricsService.Compute(chart).Sky;
            Assert.Equal(13, sky.Count);
            Assert.Equal(chart.IsDayChart, sky.Single(s => s.Planet == Planet.Sun).Altitude >= 0);
            Assert.All(sky, s => Assert.InRange(s.Azimuth!.Value, 0, 360));
        }

        // Elvis Presley's Mars is in the tenth house, near the Midheaven: high in the south.
        var mars = NatalMetricsService.Compute(Chart("elvis-presley")).Sky.Single(s => s.Planet == Planet.Mars);
        Assert.InRange(mars.Altitude!.Value, 45, 55);
        Assert.InRange(mars.Azimuth!.Value, 135, 180);
    }

    [Fact]
    public void Without_a_birth_time_there_is_no_altitude_and_no_almuten()
    {
        var m = NatalMetricsService.Compute(Repo.Charts.Calculate(Demo.Person(timed: false)));
        Assert.All(m.Sky, s => { Assert.Null(s.Altitude); Assert.Null(s.Azimuth); Assert.NotNull(s.RightAscension); });
        Assert.Empty(m.Almutens);
        Assert.Empty(m.Distribution.Quadrants);
        Assert.DoesNotContain(m.Points, p => p.Name == "Anti-Vertex");
    }

    // ── Motion ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_planet_faster_than_its_average_is_swift_and_the_outer_three_are_not_rated()
    {
        var m = NatalMetricsService.Compute(Made(
            At(Planet.Sun, 10, 1.0195), At(Planet.Mars, 100, -0.262), At(Planet.Uranus, 200, 0.05)));

        Assert.True(m.Motion.Single(p => p.Planet == Planet.Sun).Ratio > 1);
        Assert.Equal(0.5, m.Motion.Single(p => p.Planet == Planet.Mars).Ratio!.Value, 3); // backwards counts as speed
        Assert.Null(m.Motion.Single(p => p.Planet == Planet.Uranus).Ratio);
    }

    [Fact]
    public void The_stations_either_side_of_a_birth_are_found_and_dated()
    {
        // Elvis Presley, 8 January 1935. Uranus turned direct two days earlier (the
        // Worksheet marks it stationary); Mercury turned retrograde on 8 February.
        var stations = Chart("elvis-presley").Events!.Stations;

        var uranus = stations.Single(s => s.Planet == Planet.Uranus);
        Assert.False(uranus.Before!.TurnsRetrograde);
        Assert.InRange(uranus.Before.Days, 1.5, 3);
        Assert.Same(uranus.Before, uranus.Nearest);

        var mercury = stations.Single(s => s.Planet == Planet.Mercury);
        Assert.True(mercury.After!.TurnsRetrograde);
        Assert.Equal(new DateTime(1935, 2, 8), mercury.After.Utc.Date);

        // Each station is where the speed really is nothing, and the two alternate.
        Assert.All(stations, s =>
        {
            Assert.NotNull(s.Before);
            Assert.NotNull(s.After);
            Assert.NotEqual(s.Before!.TurnsRetrograde, s.After!.TurnsRetrograde);
            foreach (var point in new[] { s.Before, s.After })
            {
                double jd = SwissEphemeris.DateTimeToJulianDay(point.Utc);
                bool after = Repo.Charts.CalculateBody(jd + 1, s.Planet)!.SpeedLongitude < 0;
                bool before = Repo.Charts.CalculateBody(jd - 1, s.Planet)!.SpeedLongitude < 0;
                Assert.Equal(point.TurnsRetrograde, after);
                Assert.NotEqual(before, after);
            }
        });
    }

    // ── The Moon before birth ─────────────────────────────────────────────────

    [Fact]
    public void The_New_and_Full_Moon_before_birth_are_the_almanacs()
    {
        // Elvis Presley: the New Moon of 5 January 1935 (05:20 UT) was a partial eclipse
        // of the Sun; the Full Moon before it was on 20 December 1934.
        var events = Chart("elvis-presley").Events!;
        Assert.Equal(new DateTime(1935, 1, 5, 5, 20, 0), events.NewMoonBefore!.Utc, TimeSpan.FromMinutes(2));
        Assert.Equal("partial", events.NewMoonBefore.Eclipse);
        Assert.Equal(3.2, events.NewMoonBefore.DaysBefore, 1);
        Assert.Equal(new DateTime(1934, 12, 20), events.FullMoonBefore!.Utc.Date);
        Assert.Null(events.FullMoonBefore.Eclipse);
        Assert.Same(events.NewMoonBefore, events.Syzygy);

        // Albert Einstein was born six days after a Full Moon: that is his.
        var einstein = Chart("albert-einstein").Events!;
        Assert.Same(einstein.FullMoonBefore, einstein.Syzygy);
        Assert.Equal(new DateTime(1879, 3, 8), einstein.FullMoonBefore!.Utc.Date);
    }

    [Fact]
    public void Every_lunation_found_is_exact_and_no_more_than_a_month_back()
    {
        foreach (var f in Repo.Figures.Take(150))
        {
            var chart = Repo.Charts.Calculate(f);
            var events = NatalEventsService.Compute(Repo.Charts, chart);
            foreach (var lunation in new[] { events.NewMoonBefore!, events.FullMoonBefore! })
            {
                Assert.InRange(lunation.DaysBefore, 0, 29.9);
                double jd = SwissEphemeris.DateTimeToJulianDay(lunation.Utc);
                double sun = Repo.Charts.CalculateBody(jd, Planet.Sun)!.Longitude;
                double moon = Repo.Charts.CalculateBody(jd, Planet.Moon)!.Longitude;
                Assert.True(Repo.ArcMinutesBetween(moon, sun + (lunation.Full ? 180 : 0)) < 0.05, f.Name);
            }
        }
    }

    // ── Planetary hour ────────────────────────────────────────────────────────

    [Fact]
    public void The_planetary_day_runs_from_sunrise_not_from_midnight()
    {
        // Albert Einstein, 11:30 on Friday 14 March 1879: Venus's day, well after sunrise.
        var einstein = Chart("albert-einstein").Events!.Hour!;
        Assert.Equal(Planet.Venus, einstein.DayRuler);
        Assert.True(einstein.ByDay);
        Assert.Equal(6, einstein.Hour);
        Assert.Equal(Planet.Mars, einstein.HourRuler); // Venus, Mercury, Moon, Saturn, Jupiter, Mars

        // Elvis Presley, 04:35 on Tuesday 8 January 1935: before sunrise, so still Monday's
        // day, the Moon's.
        var elvis = Chart("elvis-presley").Events!.Hour!;
        Assert.Equal(Planet.Moon, elvis.DayRuler);
        Assert.False(elvis.ByDay);
        Assert.Equal(22, elvis.Hour);
        Assert.Equal(Planet.Moon, elvis.HourRuler);

        var untimed = Repo.Charts.Calculate(Demo.Person(timed: false));
        Assert.Null(NatalEventsService.Compute(Repo.Charts, untimed).Hour);
    }

    // ── Midpoints and antiscia ────────────────────────────────────────────────

    [Fact]
    public void A_midpoint_is_taken_the_short_way_round_and_found_from_either_end_of_its_axis()
    {
        Assert.Equal(0, NatalMetricsService.MidpointOf(350, 10), 9);
        Assert.Equal(0, NatalMetricsService.MidpointOf(10, 350), 9);
        Assert.Equal(60, NatalMetricsService.MidpointOf(30, 90), 9);

        // Sun 10°, Moon 50°: midpoint 30°. Mars stands on it; Saturn opposite it.
        var m = NatalMetricsService.Compute(Made(
            At(Planet.Sun, 10), At(Planet.Moon, 50), At(Planet.Mars, 30.5), At(Planet.Saturn, 209.2)));

        var onSunMoon = m.MidpointContacts.Where(c => c.A == NatalPoint.Of(Planet.Sun) && c.B == NatalPoint.Of(Planet.Moon)).ToList();
        Assert.Equal(new[] { Planet.Mars, Planet.Saturn }, onSunMoon.Select(c => c.Point.Body));
        Assert.Equal(0.5, onSunMoon[0].Orb, 6);
        Assert.Equal(0.8, onSunMoon[1].Orb, 6);
    }

    [Fact]
    public void Antiscia_mirror_in_the_solstice_axis_and_contra_antiscia_in_the_equinox_axis()
    {
        // 10° Gemini (70°) mirrors to 20° Cancer (110°), and by contra-antiscion to 20° Capricorn (290°).
        var m = NatalMetricsService.Compute(Made(
            At(Planet.Sun, 70), At(Planet.Venus, 110.4), At(Planet.Mars, 289.5), At(Planet.Jupiter, 200)));

        var sun = m.Antiscia.Single(a => a.Point == NatalPoint.Of(Planet.Sun));
        Assert.Equal(110, sun.Longitude, 9);
        Assert.Equal(290, sun.Contra, 9);

        var venus = Assert.Single(m.AntisciaContacts, c => c.B == NatalPoint.Of(Planet.Venus) && c.A == NatalPoint.Of(Planet.Sun));
        Assert.False(venus.Contra);
        Assert.Equal(0.4, venus.Orb, 6);
        var mars = Assert.Single(m.AntisciaContacts, c => c.B == NatalPoint.Of(Planet.Mars) && c.A == NatalPoint.Of(Planet.Sun));
        Assert.True(mars.Contra);
        Assert.Equal(0.5, mars.Orb, 6);
    }

    // ── Almutens, quadrants, spread ───────────────────────────────────────────

    [Fact]
    public void The_almuten_is_the_planet_with_most_rulership_and_ties_are_kept()
    {
        // 26°51' Virgo: Mercury rules the sign (5), is exalted there (4) and holds the face (1).
        var points = DignityService.RulershipPoints(ZodiacSign.Virgo, 26.85, isDay: false);
        Assert.Equal(10, points[Planet.Mercury]);
        Assert.Equal(3, points[Planet.Moon]);      // the night ruler of earth
        Assert.Equal(0, points[Planet.Venus]);     // the day ruler, in a night chart
        Assert.Equal(15, points.Values.Sum());     // 5 + 4 + 3 + 2 + 1, every degree of Virgo

        var almutens = NatalMetricsService.Compute(Chart("elvis-presley")).Almutens;
        Assert.Equal(new[] { "Sun", "Moon", "Ascendant", "Midheaven", "Part of Fortune" }, almutens.Select(a => a.Point));
        Assert.Equal(new[] { Planet.Mercury }, almutens.Single(a => a.Point == "Midheaven").Rulers);
        // His Sun, at 17° Capricorn: Saturn by sign, Mars by exaltation and face. Level.
        Assert.Equal(new[] { Planet.Mars, Planet.Saturn }, almutens.Single(a => a.Point == "Sun").Rulers);
    }

    [Fact]
    public void Quadrants_and_houses_add_up_and_the_spread_is_the_circle_less_its_widest_gap()
    {
        foreach (var f in Repo.Figures.Where(f => f.BirthTimeKnown).Take(100))
        {
            var m = NatalMetricsService.Compute(Repo.Charts.Calculate(f));
            Assert.Equal(m.Distribution.Total, m.Distribution.Quadrants.Sum(q => q.Count));
            Assert.Equal(m.Distribution.Total, m.Distribution.PerHouse.Sum());
            Assert.Equal(360, m.Spread!.Arc + m.Spread.LargestGap, 9);
            Assert.InRange(m.Spread.LargestGap, 36, 360); // ten planets cannot leave less
        }

        // Three planets between 350° and 40°: 50° of spread, across the start of the circle.
        var spread = NatalMetricsService.Compute(Made(At(Planet.Sun, 10), At(Planet.Mars, 350), At(Planet.Venus, 40))).Spread!;
        Assert.Equal(50, spread.Arc, 9);
        Assert.Equal(Planet.Mars, spread.First);
        Assert.Equal(Planet.Venus, spread.Last);
        Assert.Equal(3, spread.SignsOccupied);
    }

    // ── Kite and mystic rectangle ─────────────────────────────────────────────

    private static NatalChart WithAspects(params (Planet A, Planet B, AspectType Type)[] aspects)
    {
        var bodies = aspects.SelectMany(a => new[] { a.A, a.B }).Distinct().ToList();
        return new NatalChart
        {
            Celebrity = Demo.Person(timed: false),
            Planets = bodies.Select((b, i) => At(b, i * 31)).ToList(),
            Houses = [],
            Aspects = aspects.Select(a => new Aspect { PlanetA = a.A, PlanetB = a.B, Type = a.Type }).ToList(),
        };
    }

    [Fact]
    public void A_grand_trine_with_a_point_opposite_one_corner_and_sextile_the_others_is_a_kite()
    {
        var chart = WithAspects(
            (Planet.Sun, Planet.Mars, AspectType.Trine), (Planet.Mars, Planet.Jupiter, AspectType.Trine),
            (Planet.Sun, Planet.Jupiter, AspectType.Trine),
            (Planet.Saturn, Planet.Sun, AspectType.Opposition),
            (Planet.Saturn, Planet.Mars, AspectType.Sextile), (Planet.Saturn, Planet.Jupiter, AspectType.Sextile));

        var kite = Assert.Single(AspectPatternService.Detect(chart), p => p.Type == PatternType.Kite);
        Assert.Equal(NatalPoint.Of(Planet.Saturn), kite.Apex);
        Assert.Equal(NatalPoint.Of(Planet.Sun), kite.Points[0]); // the corner Saturn opposes
        Assert.Equal(4, kite.Points.Count);
        // The trine is told as the kite, not a second time on its own.
        Assert.DoesNotContain(AspectPatternService.Detect(chart), p => p.Type == PatternType.GrandTrine);
    }

    [Fact]
    public void Two_oppositions_joined_by_sextiles_and_trines_are_a_mystic_rectangle()
    {
        var chart = WithAspects(
            (Planet.Sun, Planet.Moon, AspectType.Opposition), (Planet.Mars, Planet.Venus, AspectType.Opposition),
            (Planet.Sun, Planet.Mars, AspectType.Sextile), (Planet.Moon, Planet.Venus, AspectType.Sextile),
            (Planet.Sun, Planet.Venus, AspectType.Trine), (Planet.Moon, Planet.Mars, AspectType.Trine));

        var found = AspectPatternService.Detect(chart);
        Assert.Single(found, p => p.Type == PatternType.MysticRectangle);
        Assert.DoesNotContain(found, p => p.Type is PatternType.GrandCross or PatternType.TSquare);

        // With squares where the sextiles and trines were, it is a grand cross instead.
        var cross = WithAspects(
            (Planet.Sun, Planet.Moon, AspectType.Opposition), (Planet.Mars, Planet.Venus, AspectType.Opposition),
            (Planet.Sun, Planet.Mars, AspectType.Square), (Planet.Moon, Planet.Venus, AspectType.Square),
            (Planet.Sun, Planet.Venus, AspectType.Square), (Planet.Moon, Planet.Mars, AspectType.Square));
        Assert.DoesNotContain(AspectPatternService.Detect(cross), p => p.Type == PatternType.MysticRectangle);
    }

    // ── On the Worksheet ──────────────────────────────────────────────────────

    [Fact]
    public void The_Worksheet_carries_the_new_tables_and_leaves_out_what_was_not_looked_for()
    {
        var shown = WorksheetService.Build(Chart("elvis-presley"));
        string text = System.Text.Encoding.UTF8.GetString(WorksheetService.ToText(shown));

        foreach (string title in new[] { "Motion", "Equator and horizon", "Midpoints", "Antiscia", "Triplicity rulers", "Almutens" })
            Assert.Contains(shown.Sections, s => s.Title == title && s.Rows.Count > 0);
        Assert.Contains("Julian day:       2427810.94097", text);
        Assert.Contains("Night chart — the Sun is 29°39' below the horizon", text);
        Assert.Contains("Day of the Moon, hour of the Moon — the 10th hour of the night", text);
        Assert.Contains("(a partial solar eclipse) — 3.2 days before; the lunation before birth", text);
        Assert.Contains("turned direct 2.1 days before", text);
        Assert.Contains("Anti-Vertex        28°19'10\" Capricorn", text);
        Assert.Contains(shown.Aspects, a => a.B == NatalPoint.Spirit);

        // A chart that was only calculated, not looked around: no lunations, stations or hour.
        var bare = WorksheetService.Build(Repo.Charts.Calculate(Repo.Figure("elvis-presley")));
        Assert.DoesNotContain(bare.Facts, f => f.Label == "Planetary hour");
        Assert.DoesNotContain("Nearest station", bare.Sections.Single(s => s.Title == "Motion").Headers);
        Assert.DoesNotContain(bare.Sections.Single(s => s.Title == "The Moon's phase").Rows, r => r[0] == "Age");
    }
}
