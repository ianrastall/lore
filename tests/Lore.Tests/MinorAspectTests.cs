using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The minor aspects: off unless asked for, never displacing a major aspect that is
// closer, and never reaching transits or synastry.
public class MinorAspectTests
{
    private static readonly OrbSettings WithMinors = OrbSettings.Lore with { MinorAspects = true };

    private static NatalChart Calculate(string id, OrbSettings orbs) =>
        new ChartService(Repo.Ephemeris) { Settings = new ChartSettings { Orbs = orbs } }.Calculate(Repo.Figure(id));

    private static string Key(Aspect a) => $"{a.PlanetA}-{a.Type}-{a.PlanetB}";

    [Fact]
    public void They_are_off_by_default()
    {
        Assert.False(OrbSettings.Lore.MinorAspects);
        Assert.Equal(AspectTypeExtensions.Majors, OrbSettings.Lore.Types());
        foreach (var f in Repo.Figures.Take(40))
        {
            var chart = Repo.Charts.Calculate(f);
            Assert.All(chart.Aspects, a => Assert.True(a.Type.IsMajor(), f.Name));
            Assert.All(chart.AngleAspects, a => Assert.True(a.Type.IsMajor(), f.Name));
        }
    }

    [Fact]
    public void Switching_them_on_adds_minor_aspects_and_leaves_every_major_one_in_place()
    {
        int added = 0;
        foreach (var f in Repo.Figures.Take(40))
        {
            var before = Calculate(f.Id, OrbSettings.Lore).Aspects.Select(Key).ToHashSet();
            var after = Calculate(f.Id, WithMinors);

            Assert.True(before.IsSubsetOf(after.Aspects.Select(Key).ToHashSet()), f.Name);
            var extra = after.Aspects.Where(a => !before.Contains(Key(a))).ToList();
            Assert.All(extra, a =>
            {
                Assert.False(a.Type.IsMajor());
                Assert.True(a.Orb <= 2);
                Assert.Equal(2, a.Allowed);
            });
            added += extra.Count;
        }
        Assert.True(added > 40, $"only {added} minor aspects across forty charts");
    }

    [Fact]
    public void Where_a_wide_major_orb_and_a_minor_aspect_overlap_the_closer_one_is_recorded()
    {
        // With a 15° sextile orb, a 46° separation is "within orb" of the sextile (14°
        // off) but only 1° from a semi-square. Find such pairs and check which was kept.
        var orbs = new OrbSettings(15, 15, 15, 15, 15) { MinorAspects = true, Minor = 2 };
        int overlaps = 0;
        foreach (var f in Repo.Figures.Take(60))
        {
            var chart = Calculate(f.Id, orbs);
            foreach (var a in chart.Aspects.Where(a => !a.Type.IsMajor()))
            {
                var pa = chart.GetPlanet(a.PlanetA)!.Longitude;
                var pb = chart.GetPlanet(a.PlanetB)!.Longitude;
                double sep = Math.Abs(pa - pb) % 360; if (sep > 180) sep = 360 - sep;
                double nearestMajor = AspectTypeExtensions.Majors.Min(t => Math.Abs(sep - t.Angle()));
                Assert.True(a.Orb <= nearestMajor, $"{f.Name}: {Key(a)}");
                if (nearestMajor <= 15) overlaps++;
            }
        }
        Assert.True(overlaps > 0);
    }

    [Fact]
    public void Transits_and_synastry_still_use_only_the_five_major_aspects()
    {
        var charts = new ChartService(Repo.Ephemeris) { Settings = new ChartSettings { Orbs = WithMinors } };
        var a = charts.Calculate(Repo.Figure("albert-einstein"));
        var b = charts.Calculate(Repo.Figure("elvis-presley"));

        Assert.All(SynastryService.Compare(a, b).Aspects, x => Assert.True(x.Type.IsMajor()));
        var day = new TransitService(charts).Scan(a, new DateOnly(2026, 10, 2), NodaTime.DateTimeZone.Utc);
        Assert.All(day.Events, e => Assert.True(e.Aspect.IsMajor()));
    }

    // Sun 0° and Moon 60° in sextile, both quincunx Saturn at 210°.
    private static NatalChart YodChart(bool quincunxes) => new()
    {
        Celebrity = new Celebrity { Id = "t", Name = "T", BirthDate = "2000-01-01", BirthTimeKnown = false },
        Planets =
        [
            new PlanetPosition { Planet = Planet.Sun, Longitude = 0 },
            new PlanetPosition { Planet = Planet.Moon, Longitude = 60 },
            new PlanetPosition { Planet = Planet.Saturn, Longitude = 210 },
        ],
        Houses = [],
        Aspects = quincunxes
            ?
            [
                new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Moon, Type = AspectType.Sextile },
                new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Saturn, Type = AspectType.Quincunx },
                new Aspect { PlanetA = Planet.Moon, PlanetB = Planet.Saturn, Type = AspectType.Quincunx },
            ]
            : [new Aspect { PlanetA = Planet.Sun, PlanetB = Planet.Moon, Type = AspectType.Sextile }],
    };

    [Fact]
    public void A_Yod_is_found_when_the_quincunxes_are_there_and_described_in_the_report()
    {
        var yod = Assert.Single(AspectPatternService.Detect(YodChart(quincunxes: true)));
        Assert.Equal(PatternType.Yod, yod.Type);
        Assert.Equal(NatalPoint.Of(Planet.Saturn), yod.Apex);
        Assert.Empty(AspectPatternService.Detect(YodChart(quincunxes: false)));

        var report = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(YodChart(quincunxes: true));
        Assert.Contains(report.Single(s => s.Heading == "Chart Patterns").Paragraphs,
            p => p.StartsWith("Yod — Sun and Moon, in sextile, both stand quincunx to Saturn."));
        Assert.Contains(report.Single(s => s.Heading == "Major Aspects").Paragraphs,
            p => p.StartsWith("Sun has to keep adjusting to Saturn (Quincunx ⚻") && p.Contains("A minor aspect"));
    }

    [Fact]
    public void The_named_sets_keep_their_names_with_minor_aspects_on()
    {
        Assert.Equal("Standard", WithMinors.PresetName);
        Assert.EndsWith("(Standard); minor aspects 2°", WithMinors.Describe());
        Assert.EndsWith("(Standard); minor aspects off", OrbSettings.Lore.Describe());
        Assert.NotEqual(OrbSettings.Lore, WithMinors);
        Assert.True((OrbSettings.Tight with { MinorAspects = true, Minor = 99 }).Clamped() is { MinorAspects: true, Minor: OrbSettings.Max });
        Assert.Equal("Semi-square", AspectType.SemiSquare.Name());
    }
}
