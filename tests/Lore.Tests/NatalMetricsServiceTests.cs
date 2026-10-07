using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The further measurements of a birth chart: derived points, the Moon's phase, declination
// contacts, the balance of the chart and its rulers.
public class NatalMetricsServiceTests
{
    private static Celebrity Timed => new()
    {
        Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTime = "12:00", BirthTimeKnown = true,
    };

    // A made-up chart with equal 30° houses from the Ascendant.
    private static NatalChart Chart(double asc, params PlanetPosition[] planets) => new()
    {
        Celebrity = Timed,
        Planets = planets,
        Houses = Enumerable.Range(1, 12).Select(i => new HouseCusp { House = i, Longitude = (asc + 30 * (i - 1)) % 360 }).ToList(),
        Aspects = [],
        Ascendant = asc,
        Midheaven = (asc + 270) % 360,
        Vertex = (asc + 200) % 360,
        Obliquity = 23.44,
    };

    private static PlanetPosition At(Planet planet, double longitude, double? declination = null, double latitude = 0) => new()
    {
        Planet = planet, Longitude = longitude, Latitude = latitude,
        HasEquatorial = declination is not null, Declination = declination ?? 0,
    };

    [Fact]
    public void Derived_points_are_opposite_their_partners_and_wrap_round_the_circle()
    {
        var m = NatalMetricsService.Compute(Chart(350, At(Planet.Sun, 10), At(Planet.Moon, 40), At(Planet.NorthNode, 200)));
        Assert.Equal(20, m.LongitudeOf("South Node")!.Value, 9);
        Assert.Equal(170, m.LongitudeOf("Descendant")!.Value, 9);
        Assert.Equal(80, m.LongitudeOf("Imum Coeli")!.Value, 9);   // MC at 260°
        Assert.Equal(190, m.LongitudeOf("Vertex")!.Value, 9);
    }

    [Fact]
    public void Fortune_and_Spirit_mirror_each_other_about_the_Ascendant_and_swap_by_night()
    {
        // Sun 10°, Moon 40°. Ascendant 60°: the Sun has risen, a day chart.
        var day = NatalMetricsService.Compute(Chart(60, At(Planet.Sun, 10), At(Planet.Moon, 40)));
        Assert.Equal(90, day.LongitudeOf("Part of Fortune")!.Value, 9);
        Assert.Equal(30, day.LongitudeOf("Part of Spirit")!.Value, 9);

        var night = NatalMetricsService.Compute(Chart(340, At(Planet.Sun, 10), At(Planet.Moon, 40)));
        Assert.Equal(310, night.LongitudeOf("Part of Fortune")!.Value, 9);
        Assert.Equal(10, night.LongitudeOf("Part of Spirit")!.Value, 9);
    }

    [Theory]
    [InlineData(0, MoonPhase.NewMoon, true, 0.0)]
    [InlineData(22.4, MoonPhase.NewMoon, true, null)]
    [InlineData(22.6, MoonPhase.WaxingCrescent, true, null)]
    [InlineData(90, MoonPhase.FirstQuarter, true, 0.5)]
    [InlineData(180, MoonPhase.FullMoon, false, 1.0)]
    [InlineData(270, MoonPhase.LastQuarter, false, 0.5)]
    [InlineData(350, MoonPhase.NewMoon, false, null)]
    public void The_lunar_phase_follows_the_Moons_distance_ahead_of_the_Sun(
        double elongation, MoonPhase phase, bool waxing, double? lit)
    {
        // The Sun late in Pisces, so that the Moon's longitude wraps past 360°.
        var moon = NatalMetricsService.Compute(Chart(0, At(Planet.Sun, 355), At(Planet.Moon, (355 + elongation) % 360))).Moon!;
        Assert.Equal(elongation, moon.Elongation, 9);
        Assert.Equal(phase, moon.Phase);
        Assert.Equal(waxing, moon.Waxing);
        if (lit is { } expected) Assert.Equal(expected, moon.Illumination, 9);
    }

    [Fact]
    public void A_body_ahead_of_the_Sun_is_east_of_it_and_close_ones_are_combust()
    {
        var m = NatalMetricsService.Compute(Chart(0,
            At(Planet.Sun, 358), At(Planet.Mercury, 3), At(Planet.Venus, 320), At(Planet.Mars, 358.1), At(Planet.Uranus, 359)));
        var mercury = m.Solar.Single(r => r.Planet == Planet.Mercury);
        Assert.Equal(5, mercury.Elongation, 9);
        Assert.True(mercury.EastOfSun);
        Assert.Equal(SolarCondition.Combust, mercury.Condition);

        var venus = m.Solar.Single(r => r.Planet == Planet.Venus);
        Assert.False(venus.EastOfSun);
        Assert.Equal(SolarCondition.None, venus.Condition);

        Assert.Equal(SolarCondition.Cazimi, m.Solar.Single(r => r.Planet == Planet.Mars).Condition);
        // The traditional conditions are not read for the outer planets.
        Assert.Equal(SolarCondition.None, m.Solar.Single(r => r.Planet == Planet.Uranus).Condition);
    }

    [Fact]
    public void Parallels_need_the_same_side_of_the_equator_and_contra_parallels_opposite_sides()
    {
        var contacts = NatalMetricsService.Parallels(
        [
            At(Planet.Sun, 0, 20.0), At(Planet.Moon, 0, 20.6), At(Planet.Mars, 0, -19.5),
            At(Planet.Jupiter, 0, 0.3), At(Planet.Saturn, 0, -0.3),
            At(Planet.Venus, 0),   // declination unknown: must not be read as 0°
        ]);

        Assert.Contains(contacts, c => c is { A: Planet.Sun, B: Planet.Moon, Contra: false } && Math.Abs(c.Orb - 0.6) < 1e-9);
        Assert.Contains(contacts, c => c is { A: Planet.Sun, B: Planet.Mars, Contra: true } && Math.Abs(c.Orb - 0.5) < 1e-9);
        // Moon +20.6 and Mars −19.5 are 1.1° from equal: outside the orb.
        Assert.DoesNotContain(contacts, c => c is { A: Planet.Moon, B: Planet.Mars });
        // Either side of the equator and close to it: a contra-parallel, exact, not a parallel.
        var across = contacts.Single(c => c is { A: Planet.Jupiter, B: Planet.Saturn });
        Assert.True(across.Contra);
        Assert.Equal(0, across.Orb, 9);
        Assert.DoesNotContain(contacts, c => c.A == Planet.Venus || c.B == Planet.Venus);
        Assert.Equal(contacts.OrderBy(c => c.Orb), contacts);
    }

    [Fact]
    public void A_body_beyond_the_obliquity_is_out_of_bounds_by_the_difference()
    {
        var m = NatalMetricsService.Compute(Chart(0, At(Planet.Sun, 90, 23.44), At(Planet.Moon, 95, -25.44), At(Planet.Mars, 10)));
        var only = Assert.Single(m.OutOfBounds);
        Assert.Equal(Planet.Moon, only.Planet);
        Assert.Equal(2, only.Excess, 9);
    }

    [Fact]
    public void Nearest_angle_and_distance_into_the_house_wrap_round_the_circle()
    {
        // Ascendant 350°: a body at 2° is 12° past it, in the 1st; one at 345° is in the 12th.
        var m = NatalMetricsService.Compute(Chart(350, At(Planet.Sun, 2), At(Planet.Moon, 345)));
        var sun = m.Angularity.Single(a => a.Planet == Planet.Sun);
        Assert.Equal(("Ascendant", 1), (sun.NearestAngle, sun.House));
        Assert.Equal(12, sun.Distance, 9);
        Assert.Equal(12, sun.IntoHouse, 9);

        var moon = m.Angularity.Single(a => a.Planet == Planet.Moon);
        Assert.Equal(("Ascendant", 12), (moon.NearestAngle, moon.House));
        Assert.Equal(5, moon.Distance, 9);
        Assert.Equal(25, moon.IntoHouse, 9);
    }

    [Fact]
    public void The_balance_counts_every_body_once_and_adds_up()
    {
        var m = NatalMetricsService.Compute(Chart(0,
            At(Planet.Sun, 5), At(Planet.Moon, 125), At(Planet.Mercury, 35), At(Planet.Venus, 65), At(Planet.Mars, 95)));
        var d = m.Distribution;
        Assert.Equal(5, d.Total);
        Assert.Equal(2, d.Elements.Single(t => t.Name == "Fire").Count);
        Assert.Equal(5, d.Elements.Sum(t => t.Count));
        Assert.Equal(5, d.Modalities.Sum(t => t.Count));
        Assert.Equal(3, d.Polarities[0].Count);                    // fire, fire, air
        Assert.Equal(5, d.HouseTypes.Sum(t => t.Count));
        Assert.Equal(2, d.HouseTypes[0].Count);                    // houses 1 and 4
        Assert.Equal(0, d.Hemispheres[0].Count);                   // none in houses 7–12
    }

    [Fact]
    public void One_planet_in_its_own_sign_that_all_chains_reach_is_the_final_dispositor()
    {
        // Sun in Leo; Moon in Leo; Mercury in Cancer (→ Moon → Sun); Pluto in Gemini (→ Mercury …).
        var r = NatalMetricsService.Compute(Chart(125,
            At(Planet.Sun, 125), At(Planet.Moon, 140), At(Planet.Mercury, 100), At(Planet.Pluto, 70))).Rulers;
        Assert.Equal(Planet.Sun, r.FinalDispositor);
        Assert.Equal([Planet.Sun], r.InDomicile);
        Assert.Empty(r.Loops);
        Assert.Equal([Planet.Pluto, Planet.Mercury, Planet.Moon, Planet.Sun, Planet.Sun],
            r.Dispositions.Single(d => d.Planet == Planet.Pluto).Chain);
        Assert.Equal(Planet.Sun, r.ChartRuler);                    // Leo rising
        Assert.Null(r.ModernChartRuler);
    }

    [Fact]
    public void Two_planets_in_each_others_signs_are_a_mutual_reception_and_leave_no_final_dispositor()
    {
        // Venus in Aries, Mars in Taurus; the Sun in Aries leads into the pair.
        var r = NatalMetricsService.Compute(Chart(215,
            At(Planet.Sun, 10), At(Planet.Venus, 20), At(Planet.Mars, 40))).Rulers;
        Assert.Null(r.FinalDispositor);
        var loop = Assert.Single(r.Loops);
        Assert.Equal(2, loop.Count);
        Assert.Contains(Planet.Venus, loop);
        Assert.Contains(Planet.Mars, loop);
        Assert.Equal(Planet.Mars, r.ChartRuler);                   // Scorpio rising
        Assert.Equal(Planet.Pluto, r.ModernChartRuler);
        Assert.Equal(12, r.Houses.Count);
        Assert.Equal(new HouseRuler(1, ZodiacSign.Scorpio, Planet.Mars, ZodiacSign.Taurus, 7), r.Houses[0]);
    }

    [Fact]
    public void An_unaspected_planet_is_one_with_no_major_aspect()
    {
        var chart = new NatalChart
        {
            Celebrity = Timed,
            Planets = [At(Planet.Sun, 0), At(Planet.Moon, 90), At(Planet.Mars, 150), At(Planet.Venus, 40)],
            Houses = [],
            Aspects =
            [
                new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Moon, Type = AspectType.Square, Orb = 0.5, IsApplying = true },
                new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Mars, Type = AspectType.Quincunx, Orb = 0.1 },
            ],
        };
        var a = NatalMetricsService.Compute(chart).Aspects;
        Assert.Equal([Planet.Venus, Planet.Mars], a.Unaspected.OrderBy(p => p == Planet.Mars));
        Assert.Equal((1, 1), (a.Applying, a.Separating));
        Assert.Equal(AspectType.Quincunx, a.Closest!.Type);
        Assert.Equal(new Tally("Sun", 2), a.PerBody[0]);
    }

    [Fact]
    public void A_real_chart_carries_the_retained_ephemeris_values()
    {
        var chart = Repo.Charts.Calculate(Repo.Figure("elvis-presley"));
        Assert.InRange(chart.Obliquity!.Value, 23.4, 23.5);
        Assert.All(chart.Planets, p => Assert.True(p.HasEquatorial));

        var sun = chart.GetPlanet(Planet.Sun)!;
        Assert.InRange(sun.Distance, 0.98, 1.02);                  // astronomical units
        Assert.InRange(chart.GetPlanet(Planet.Moon)!.Distance, 0.0023, 0.0028);
        // 8 January: the Sun in Capricorn, its right ascension a little past 18h (270°).
        Assert.InRange(sun.RightAscension, 285, 292);
        // The Midheaven is the ecliptic degree whose right ascension is the sidereal time.
        double eps = chart.Obliquity.Value * Math.PI / 180, mc = chart.Midheaven * Math.PI / 180;
        double ra = Math.Atan2(Math.Sin(mc) * Math.Cos(eps), Math.Cos(mc)) * 180 / Math.PI;
        Assert.True(Repo.ArcMinutesBetween(ra, chart.Armc) < 0.01);
        // The Vertex lies in the western half of the chart.
        Assert.InRange(((chart.Vertex - chart.Ascendant) % 360 + 360) % 360, 90, 270);
    }

    [Fact]
    public void A_chart_with_no_birth_time_keeps_only_what_needs_none()
    {
        var m = NatalMetricsService.Compute(Repo.Charts.Calculate(Demo.Person(timed: false)));
        Assert.Equal(["South Node"], m.Points.Select(p => p.Name));
        Assert.NotNull(m.Moon);
        Assert.Empty(m.Angularity);
        Assert.Empty(m.Distribution.HouseTypes);
        Assert.Empty(m.Rulers.Houses);
        Assert.Null(m.Rulers.ChartRuler);
        Assert.Equal(13, m.Rulers.Dispositions.Count);
    }

    // ── Dominants ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_planet_on_the_Ascendant_that_rules_it_dominates_the_chart()
    {
        // Aries rising with Mars two degrees from the Ascendant; the Sun in Leo, the
        // Moon in Taurus, and nothing in aspect (the made-up chart carries none).
        var chart = Chart(10, At(Planet.Sun, 130), At(Planet.Moon, 40), At(Planet.Mars, 12), At(Planet.Saturn, 200));
        var d = NatalMetricsService.Compute(chart).Dominants;

        Assert.Equal(Planet.Mars, d.Planet);
        Assert.Equal(d.Planets.OrderByDescending(p => p.Score).Select(p => p.Planet), d.Planets.Select(p => p.Planet));

        // Mars: 2° from the Ascendant is 10 × 0.8; in its own sign 5; rules the Ascendant 8.
        var mars = d.Planets[0];
        Assert.Equal(8 + 5 + 8, mars.Score, 6);
        Assert.Equal(["2°00' from the Ascendant 8.0", "in its own sign, Aries 5.0", "rules the Ascendant 8.0"], mars.Parts);

        // The Sun in Leo is in its own sign, and is not counted again for ruling itself;
        // as a light it starts with 3.
        Assert.Equal(3 + 5, d.Planets.Single(p => p.Planet == Planet.Sun).Score, 6);
        // The Moon is exalted in Taurus; Saturn in Libra is exalted and rules the Midheaven (Capricorn).
        Assert.Equal(3 + 4, d.Planets.Single(p => p.Planet == Planet.Moon).Score, 6);
        Assert.Equal(4 + 3, d.Planets.Single(p => p.Planet == Planet.Saturn).Score, 6);
    }

    [Fact]
    public void A_close_aspect_counts_for_more_than_a_wide_one_and_a_generational_one_for_half()
    {
        static Aspect Of(Planet a, Planet b, AspectType type, double orb) =>
            new() { PlanetA = a, PlanetB = b, Type = type, Orb = orb, Allowed = 8 };
        NatalChart With(params Aspect[] aspects) => new()
        {
            Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTimeKnown = false },
            Planets = [At(Planet.Venus, 95), At(Planet.Jupiter, 185), At(Planet.Uranus, 275), At(Planet.Neptune, 5)],
            Houses = [], Aspects = aspects,
        };
        double Score(NatalChart c, Planet p) => NatalMetricsService.Compute(c).Dominants.Planets.Single(x => x.Planet == p).Score;

        // A square is worth 3 at exact and half of that halfway out; both ends get it.
        // (Jupiter is exalted in Cancer, Venus in Pisces: neither is there.)
        Assert.Equal(3, Score(With(Of(Planet.Venus, Planet.Jupiter, AspectType.Square, 0)), Planet.Venus), 6);
        Assert.Equal(1.5, Score(With(Of(Planet.Venus, Planet.Jupiter, AspectType.Square, 4)), Planet.Jupiter), 6);
        Assert.Equal(0, Score(With(Of(Planet.Venus, Planet.Jupiter, AspectType.Square, 8)), Planet.Venus), 6);

        // Between two of the outermost three it counts half; a minor aspect not at all.
        Assert.Equal(1.5, Score(With(Of(Planet.Uranus, Planet.Neptune, AspectType.Square, 0)), Planet.Uranus), 6);
        Assert.Equal(0, Score(With(Of(Planet.Venus, Planet.Jupiter, AspectType.Quintile, 0)), Planet.Venus), 6);
    }

    [Fact]
    public void The_weighted_balance_counts_the_lights_and_the_Ascendant_three_times_over()
    {
        // Aries rising (fire, cardinal) with a Capricorn Midheaven; Sun in Leo, Moon in
        // Taurus, Mars in Aries, Saturn in Libra.
        var chart = Chart(10, At(Planet.Sun, 130), At(Planet.Moon, 40), At(Planet.Mars, 12), At(Planet.Saturn, 200), At(Planet.Chiron, 100));
        var d = NatalMetricsService.Compute(chart).Dominants;

        // Sun 3 + Moon 3 + Mars 2 + Saturn 1 + Ascendant 3 + Midheaven 1; Chiron is not counted.
        Assert.Equal(13, d.TotalWeight);
        Assert.Equal([("Fire", 8.0), ("Earth", 4.0), ("Air", 1.0), ("Water", 0.0)], d.Elements.Select(w => (w.Name, w.Points)));
        Assert.Equal([("Cardinal", 7.0), ("Fixed", 6.0), ("Mutable", 0.0)], d.Modalities.Select(w => (w.Name, w.Points)));
        Assert.Equal(("Aries", 5.0), (d.Signs[0].Name, d.Signs[0].Points));
        Assert.Equal(13, d.Signs.Sum(w => w.Points));
        Assert.DoesNotContain(d.Signs, w => w.Points == 0);
    }

    [Fact]
    public void Without_a_birth_time_the_angles_take_no_part_in_the_dominants()
    {
        var chart = Repo.Charts.Calculate(Demo.Person(timed: false));
        var d = NatalMetricsService.Compute(chart).Dominants;

        Assert.Equal(10, d.Planets.Count);
        Assert.DoesNotContain(d.Planets.SelectMany(p => p.Parts),
            part => part.Contains("Ascendant") || part.Contains("Midheaven") || part.Contains(" from the "));
        Assert.Equal(3 + 3 + 2 + 2 + 2 + 5, d.TotalWeight);

        // The worksheet shows both tables, and says the angles are missing.
        var sheet = WorksheetService.Build(chart);
        Assert.Equal(10, sheet.Sections.Single(s => s.Title == "Dominant planets").Rows.Count);
        Assert.Contains("With no birth time", sheet.Sections.Single(s => s.Title == "Dominant planets").Note);
        Assert.Equal(3, sheet.Sections.Single(s => s.Title == "Weighted balance").Rows.Count);
    }

    [Fact]
    public void Every_bundled_figure_has_a_dominant_planet_and_a_balance_that_adds_up()
    {
        foreach (var f in Repo.Figures)
        {
            var d = NatalMetricsService.Compute(Repo.Charts.Calculate(f)).Dominants;
            Assert.NotNull(d.Planet);
            Assert.Equal(10, d.Planets.Count);
            Assert.All(d.Planets, p => Assert.True(p.Score >= 0));
            Assert.Equal(d.TotalWeight, d.Elements.Sum(w => w.Points), 9);
            Assert.Equal(d.TotalWeight, d.Modalities.Sum(w => w.Points), 9);
            Assert.Equal(f.BirthTimeKnown ? 21 : 17, d.TotalWeight);
        }
    }
}