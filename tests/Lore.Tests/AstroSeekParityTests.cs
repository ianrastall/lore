using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// What was added so that Lore covers the ground of Astro-Seek's chart image: the true
// (osculating) Lilith, the quintile and biquintile, aspects to the Vertex and the Part of
// Fortune, the stationary mark, and the two Worksheet tables (by degree; elements by mode).
public class AstroSeekParityTests
{
    private static NatalChart Calculate(Celebrity c, ChartSettings settings) =>
        new ChartService(Repo.Ephemeris) { Settings = settings }.Calculate(c);

    private static readonly Celebrity Einstein = Repo.Figure("albert-einstein");

    private static readonly ChartSettings AsAstroSeek =
        new(HouseSystem.WholeSign, NodeType.True, LilithType.True) { Orbs = OrbSettings.Lore with { MinorAspects = true } };

    [Fact]
    public void True_Lilith_is_the_engines_osculating_apogee()
    {
        var chart = Calculate(Einstein, AsAstroSeek);
        Assert.Equal(LilithType.True, chart.Settings.Lilith);

        // The true apogee swings either side of the mean one, by up to about 30°.
        // (Checked by hand on 6 October 2026 against an Astro-Seek chart cast with the
        // true node, true Lilith and whole-sign houses: every position agreed to the minute.)
        int moved = 0;
        foreach (var f in Repo.Figures.Take(30))
        {
            double a = Calculate(f, AsAstroSeek).GetPlanet(Planet.Lilith)!.Longitude;
            double b = Calculate(f, new ChartSettings()).GetPlanet(Planet.Lilith)!.Longitude;
            Assert.InRange(Repo.ArcMinutesBetween(a, b) / 60, 0, 31);
            if (Repo.ArcMinutesBetween(a, b) > 60) moved++;
        }
        Assert.True(moved > 20);
    }

    [Fact]
    public void The_mean_Lilith_is_still_what_a_chart_gets_unless_asked()
    {
        var mean = Calculate(Einstein, new ChartSettings(HouseSystem.WholeSign));
        Assert.Equal(Repo.Charts.Calculate(Einstein).GetPlanet(Planet.Lilith)!.Longitude, mean.GetPlanet(Planet.Lilith)!.Longitude);
        Assert.Equal(LilithType.Mean, ChartSettings.Default.Lilith);

        // Nothing but Lilith moves when the setting changes.
        var real = Calculate(Einstein, new ChartSettings(HouseSystem.WholeSign, Lilith: LilithType.True));
        foreach (var p in mean.Planets.Where(p => p.Planet != Planet.Lilith))
            Assert.Equal(p.Longitude, real.GetPlanet(p.Planet)!.Longitude);
    }

    [Fact]
    public void Each_chart_carries_the_node_and_Lilith_of_the_other_kind_for_the_Worksheet()
    {
        var mean = Calculate(Einstein, new ChartSettings(HouseSystem.WholeSign));
        var real = Calculate(Einstein, AsAstroSeek);

        Assert.Equal(["North Node (true)", "Lilith (true)"], mean.Alternates.Select(a => a.Name));
        Assert.Equal(["North Node (mean)", "Lilith (mean)"], real.Alternates.Select(a => a.Name));
        Assert.Equal(real.GetPlanet(Planet.NorthNode)!.Longitude, mean.Alternates[0].Position.Longitude);
        Assert.Equal(real.GetPlanet(Planet.Lilith)!.Longitude, mean.Alternates[1].Position.Longitude);
        Assert.Equal(mean.GetPlanet(Planet.Lilith)!.Longitude, real.Alternates[1].Position.Longitude);

        var rows = WorksheetService.Build(mean).Positions;
        Assert.Equal(WorksheetService.Dms(mean.Alternates[0].Position.Longitude), rows.Single(r => r.Name == "North Node (true)").Position);
        Assert.Equal(WorksheetService.Dms(mean.Alternates[1].Position.Longitude), rows.Single(r => r.Name == "Lilith (true)").Position);
        Assert.Equal(["North Node (true)", "Lilith (true)"], rows.TakeLast(2).Select(r => r.Name));
        Assert.Contains(WorksheetService.Build(real).Facts, f => f.Label == "Lilith" && f.Value.StartsWith("True (osculating)"));
    }

    [Fact]
    public void Quintiles_and_biquintiles_come_with_the_minor_aspects()
    {
        Assert.Equal(72, AspectType.Quintile.Angle());
        Assert.Equal(144, AspectType.Biquintile.Angle());
        Assert.False(AspectType.Quintile.IsMajor());
        Assert.Contains(AspectType.Biquintile, OrbSettings.Lore with { MinorAspects = true } is var on ? on.Types() : []);
        Assert.DoesNotContain(AspectType.Quintile, OrbSettings.Lore.Types());

        // Across the library they turn up, always within the 2° orb.
        int quintiles = 0, biquintiles = 0;
        NatalChart? withOne = null;
        foreach (var f in Repo.Figures.Take(40))
        {
            var c = Calculate(f, AsAstroSeek);
            foreach (var a in c.Aspects.Where(a => a.Type is AspectType.Quintile or AspectType.Biquintile))
            {
                double sep = Repo.ArcMinutesBetween(c.GetPlanet(a.PlanetA)!.Longitude, c.GetPlanet(a.PlanetB)!.Longitude) / 60;
                Assert.InRange(Math.Abs(sep - a.Type.Angle()), 0, 2);
                Assert.Equal(Math.Abs(sep - a.Type.Angle()), a.Orb, 6);
                Assert.False(a.OutOfSign); // a fifth of the circle has no signs to be out of
                if (a.Type == AspectType.Quintile) { quintiles++; withOne ??= c; } else biquintiles++;
            }
        }
        Assert.True(quintiles > 10 && biquintiles > 10, $"{quintiles} quintiles, {biquintiles} biquintiles");
        var chart = withOne!;

        // And the report has words for them.
        var report = new ChartInterpreter(Repo.Data("interpretations.json")).Interpret(chart);
        Assert.Contains(report.Single(s => s.Heading == "Major Aspects").Paragraphs,
            p => p.Contains("(Quintile Q") && p.Contains("A minor aspect: a fifth of the circle"));
    }

    [Fact]
    public void The_Worksheet_aspects_the_Vertex_and_the_Part_of_Fortune()
    {
        var chart = Calculate(Einstein, AsAstroSeek);
        var w = WorksheetService.Build(chart);

        Assert.Equal([NatalPoint.Ascendant, NatalPoint.Midheaven, NatalPoint.Vertex, NatalPoint.Fortune], w.Points.Skip(13));

        var toPoints = w.Aspects.Where(a => a.B == NatalPoint.Vertex || a.B == NatalPoint.Fortune).ToList();
        Assert.NotEmpty(toPoints);
        Assert.All(toPoints, a =>
        {
            double sep = Repo.ArcMinutesBetween(chart.LongitudeOf(a.A)!.Value, chart.LongitudeOf(a.B)!.Value) / 60;
            Assert.Equal(Math.Abs(sep - a.Type.Angle()), a.Orb, 6);
            Assert.True(a.Orb <= chart.Settings.Orbs.For(a.Type, a.A.Body));
            Assert.Same(a, w.AspectBetween(a.B, a.A));
        });
        Assert.Equal(chart.Vertex, chart.LongitudeOf(NatalPoint.Vertex));
        Assert.Equal(WorksheetService.PartOfFortune(chart), chart.LongitudeOf(NatalPoint.Fortune));

        // They stay on the Worksheet: the chart's own lists, which the report, the
        // patterns and the wheel read, hold the Ascendant and Midheaven only.
        Assert.All(chart.AngleAspects, a => Assert.True(a.Angle == NatalPoint.Ascendant || a.Angle == NatalPoint.Midheaven));

        // None without a birth time.
        var untimed = Calculate(new Celebrity
        {
            Id = "u", Name = "U", BirthDate = "1990-06-21", BirthTimeKnown = false,
            BirthPlace = "London", Latitude = 51.5, Longitude = -0.13, TimeZoneId = "Europe/London",
        }, AsAstroSeek);
        var bare = WorksheetService.Build(untimed);
        Assert.Equal(13, bare.Points.Count);
        Assert.DoesNotContain(bare.Aspects, a => a.B.IsAngle);
    }

    [Fact]
    public void A_body_standing_nearly_still_is_marked_stationary()
    {
        PlanetPosition At(Planet p, double speed) => new() { Planet = p, SpeedLongitude = speed };

        Assert.Equal("S", At(Planet.Mercury, 0.05).MotionMark);
        Assert.Equal("S℞", At(Planet.Mercury, -0.05).MotionMark);
        Assert.Equal("℞", At(Planet.Mercury, -0.8).MotionMark);
        Assert.Equal("", At(Planet.Mercury, 1.4).MotionMark);
        Assert.Equal("℞", At(Planet.Saturn, -0.0394).MotionMark);   // retrograde, but well under way
        Assert.False(At(Planet.Sun, 0).IsStationary);
        Assert.False(At(Planet.Moon, 0).IsStationary);

        // The mean node and mean Lilith keep a steady pace and are never marked.
        foreach (var f in Repo.Figures.Take(60))
        {
            var chart = Repo.Charts.Calculate(f);
            Assert.Equal("", chart.GetPlanet(Planet.NorthNode)!.MotionMark);
            Assert.Equal("", chart.GetPlanet(Planet.Lilith)!.MotionMark);
        }

        // The true node stops and turns every few weeks, so some charts catch it at it;
        // the Worksheet then says so in its last column, and in words in the text.
        var stopped = Repo.Figures.Select(f => Calculate(f, AsAstroSeek))
            .First(c => c.GetPlanet(Planet.NorthNode)!.IsStationary);
        var w = WorksheetService.Build(stopped);
        Assert.Contains("S", w.Positions.Single(r => r.Name == "North Node").Motion);
        string text = System.Text.Encoding.UTF8.GetString(WorksheetService.ToText(w));
        Assert.Matches(@"North Node +\d+°\d\d'\d\d"" \w+ .* stationary", text);
    }

    [Fact]
    public void The_Worksheet_orders_everything_by_degree_and_lays_the_bodies_out_by_element_and_mode()
    {
        var chart = Calculate(Einstein, AsAstroSeek);
        var w = WorksheetService.Build(chart);

        // Thirteen bodies and the two angles, lowest degree of its sign first.
        var byDegree = w.Sections.Single(s => s.Title == "By degree");
        Assert.Equal(15, byDegree.Rows.Count);
        Assert.Equal(byDegree.Rows.Select(r => Degrees(r[0])).Order(), byDegree.Rows.Select(r => Degrees(r[0])));
        var sun = chart.GetPlanet(Planet.Sun)!;
        Assert.Contains(byDegree.Rows, r => r[1] == "☉ Sun" && r[2] == sun.Sign.Name() &&
                                            r[0] == ZodiacSignExtensions.FormatDegreeInSign(sun.Longitude));
        Assert.Contains(byDegree.Rows, r => r[1] == "↑ Ascendant");

        // Four elements by three modes: each body in the one cell that is its sign.
        var grid = w.Sections.Single(s => s.Title == "Elements and modes");
        Assert.Equal(["", "Cardinal", "Fixed", "Mutable"], grid.Headers);
        Assert.Equal(["Fire", "Earth", "Air", "Water"], grid.Rows.Select(r => r[0]));
        Assert.Equal(13, grid.Rows.Sum(r => r.Skip(1).Sum(c => c == "·" ? 0 : c.Split(' ').Length)));
        foreach (var p in chart.Planets)
            Assert.Contains(p.PlanetSymbol, grid.Rows[(int)p.Sign.GetElement()][1 + (int)p.Sign.GetModality()].Split(' '));

        static double Degrees(string dm) => int.Parse(dm[..dm.IndexOf('°')]) + int.Parse(dm[(dm.IndexOf('°') + 1)..^1]) / 60.0;
    }

    [Fact]
    public void A_settings_file_from_before_the_Lilith_choice_still_loads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Houses": "Koch", "Node": "True" }""");
            Assert.Equal(new ChartSettings(HouseSystem.Koch, NodeType.True, LilithType.Mean), service.Load());

            service.Save(AsAstroSeek);
            Assert.Equal(AsAstroSeek, service.Load());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
