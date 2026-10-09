using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// Aspects to the Ascendant and Midheaven: found by the chart itself, and taking part in
// the patterns.
public class AngleAspectTests
{
    [Fact]
    public void A_timed_chart_has_aspects_to_its_angles_and_an_untimed_one_has_none()
    {
        // Einstein under the standard orbs: Moon and Neptune to the Midheaven; Neptune,
        // Venus and Saturn to the Ascendant.
        var chart = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        Assert.Equal(5, chart.AngleAspects.Count);
        Assert.Contains(chart.AngleAspects, a =>
            a.Planet == Planet.Moon && a.Angle == NatalPoint.Midheaven && a.Type == AspectType.Square);
        Assert.All(chart.AngleAspects, a => Assert.True(a.Orb <= a.Allowed));
        Assert.Equal(22, chart.Aspects.Count); // the planets' own aspects are unchanged

        var untimed = new Celebrity
        {
            Id = "t", Name = "T", BirthDate = "1990-06-21", BirthTimeKnown = false,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        };
        Assert.Empty(Repo.Charts.Calculate(untimed).AngleAspects);
    }

    [Fact]
    public void The_worksheet_and_the_report_carry_them()
    {
        var chart = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        // (The Worksheet also aspects the Vertex and the two lots, which the report does not.)
        Assert.Equal(27, WorksheetService.Build(chart).Aspects.Count(a =>
            a.B.Kind is not (NatalPointKind.Vertex or NatalPointKind.Fortune or NatalPointKind.Spirit)));

        var aspects = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(chart)
            .Single(s => s.Heading == "Major Aspects").Paragraphs;
        Assert.Equal(27, aspects.Count);
        Assert.Contains(aspects, p => p.StartsWith("Moon ") && p.Contains(" the Midheaven (Square"));
    }

    // Sun 0°, Moon 180°, with the Ascendant at 90° square to both.
    private static NatalChart WithAngleAtTheApex(bool timed) => new()
    {
        Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTime = timed ? "12:00" : null, BirthTimeKnown = timed },
        Planets =
        [
            new PlanetPosition { Planet = Planet.Sun, Longitude = 0 },
            new PlanetPosition { Planet = Planet.Moon, Longitude = 180 },
        ],
        Houses = [],
        Aspects = [new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Moon, Type = AspectType.Opposition }],
        AngleAspects =
        [
            new AngleAspect { Planet = Planet.Sun, Angle = NatalPoint.Ascendant, Type = AspectType.Square },
            new AngleAspect { Planet = Planet.Moon, Angle = NatalPoint.Ascendant, Type = AspectType.Square },
        ],
        Ascendant = 90,
        Midheaven = 10,
    };

    [Fact]
    public void An_angle_can_stand_at_the_apex_of_a_T_square()
    {
        var pattern = Assert.Single(AspectPatternService.Detect(WithAngleAtTheApex(timed: true)));
        Assert.Equal(PatternType.TSquare, pattern.Type);
        Assert.Equal(NatalPoint.Ascendant, pattern.Apex);
        Assert.Equal(Modality.Cardinal, pattern.Modality); // 90° is 0° Cancer

        // Without a birth time there is no Ascendant to take part.
        Assert.Empty(AspectPatternService.Detect(WithAngleAtTheApex(timed: false)));
    }

    [Fact]
    public void Patterns_among_the_planets_alone_are_found_as_before()
    {
        // Every pattern found among the bodies must still be found once the angles join in.
        foreach (var id in new[] { "albert-einstein", "elvis-presley", "roger-federer", "karl-marx" })
        {
            var chart = Repo.Charts.Calculate(Repo.Figure(id));
            var bodiesOnly = new NatalChart
            {
                Celebrity = chart.Celebrity, Planets = chart.Planets, Houses = chart.Houses, Aspects = chart.Aspects,
                Ascendant = chart.Ascendant, Midheaven = chart.Midheaven, // AngleAspects left empty
            };
            string Key(AspectPattern p) => $"{p.Type}:{string.Join(",", p.Points.Select(x => x.Name).OrderBy(x => x))}";

            var before = AspectPatternService.Detect(bodiesOnly).Select(Key).ToHashSet();
            var after = AspectPatternService.Detect(chart).Select(Key).ToHashSet();
            Assert.True(before.IsSubsetOf(after), id);
            Assert.All(after.Except(before), k => Assert.True(k.Contains("Ascendant") || k.Contains("Midheaven"), k));
        }
    }

    [Fact]
    public void Out_of_sign_aspects_are_recognised()
    {
        // 28 Aries and 2 Taurus: 4 degrees apart, but in neighbouring signs.
        Assert.True(AspectType.Conjunction.IsOutOfSign(28, 32));
        Assert.False(AspectType.Conjunction.IsOutOfSign(22, 26));
        // 29 Aries and 1 Virgo (151): a trine by degree (122 apart), but Aries to Virgo is five signs.
        Assert.True(AspectType.Trine.IsOutOfSign(29, 151));
        Assert.False(AspectType.Trine.IsOutOfSign(10, 130));
        // Across 0 Aries: 29 Pisces and 1 Libra are 178 apart but seven signs round one way, five the other.
        Assert.True(AspectType.Opposition.IsOutOfSign(359, 181));
        Assert.False(AspectType.Opposition.IsOutOfSign(359, 179));
        // The semi-square has no sign relationship to be out of.
        Assert.False(AspectType.SemiSquare.IsOutOfSign(28, 73));
    }

    [Fact]
    public void Real_charts_have_some_and_the_worksheet_and_report_mark_them()
    {
        var interpreter = new ChartInterpreter(Repo.Data("interpretations.json"));
        int found = 0, all = 0;
        foreach (var f in Repo.Figures.Take(40))
        {
            var chart = Repo.Charts.Calculate(f);
            all += chart.Aspects.Count + chart.AngleAspects.Count;
            int here = chart.Aspects.Count(a => a.OutOfSign) + chart.AngleAspects.Count(a => a.OutOfSign);
            found += here;

            Assert.Equal(here, WorksheetService.Build(chart).Aspects.Count(a =>
                a.OutOfSign && a.B.Kind is not (NatalPointKind.Vertex or NatalPointKind.Fortune or NatalPointKind.Spirit)));
            Assert.Equal(here, interpreter.Interpret(chart).Single(s => s.Heading == "Major Aspects")
                .Paragraphs.Count(p => p.Contains("Out of sign: the two are in ")));
        }
        // With 8 degree orbs on 30 degree signs, roughly one aspect in seven straddles a boundary.
        Assert.InRange(found / (double)all, 0.05, 0.3);
    }
}
