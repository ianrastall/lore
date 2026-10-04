using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// Aspect orbs: the standard set must reproduce what Lore has always found, and the
// other sets must widen or narrow it in the way they say.
public class OrbSettingsTests
{
    private static NatalChart Calculate(string id, OrbSettings orbs) =>
        new ChartService(Repo.Ephemeris) { Settings = new ChartSettings { Orbs = orbs } }.Calculate(Repo.Figure(id));

    private static IEnumerable<string> Keys(NatalChart c) =>
        c.Aspects.Select(a => $"{a.PlanetA}-{a.Type}-{a.PlanetB}").OrderBy(x => x);

    [Fact]
    public void The_standard_set_is_what_Lore_has_always_used()
    {
        Assert.Equal(new OrbSettings(8, 6, 8, 8, 8, 0), OrbSettings.Lore);
        Assert.Equal(OrbSettings.Lore, ChartSettings.Default.Orbs);
        foreach (var type in Enum.GetValues<AspectType>())
            Assert.Equal(type.Orb(), OrbSettings.Lore.For(type, Planet.Sun, Planet.Moon));
    }

    [Fact]
    public void Einsteins_aspects_under_the_standard_set_are_pinned()
    {
        // The 22 planet-to-planet aspects Lore 1.6 found, closest first. If this list
        // changes, the default orbs (or the aspect arithmetic) have changed with it.
        var chart = Repo.Charts.Calculate(Repo.Figure("albert-einstein"));
        Assert.Equal(22, chart.Aspects.Count);
        var closest = chart.Aspects.OrderBy(a => a.Orb).First();
        Assert.Equal((Planet.Jupiter, AspectType.Sextile, Planet.Lilith), (closest.PlanetA, closest.Type, closest.PlanetB));
        Assert.All(chart.Aspects, a => Assert.Equal(a.Type.Orb(), a.Allowed));
        Assert.Equal(Keys(chart), Keys(Calculate("albert-einstein", OrbSettings.Lore)));
    }

    [Fact]
    public void Tighter_orbs_keep_a_subset_and_wider_orbs_a_superset()
    {
        foreach (var id in new[] { "albert-einstein", "elvis-presley", "roger-federer" })
        {
            var standard = Keys(Calculate(id, OrbSettings.Lore)).ToHashSet();
            var none = new OrbSettings(0, 0, 0, 0, 0);
            var narrow = Keys(Calculate(id, new OrbSettings(4, 3, 4, 4, 4))).ToHashSet();
            var wide = Calculate(id, OrbSettings.Wide);

            Assert.Empty(Calculate(id, none).Aspects);
            Assert.True(narrow.IsProperSubsetOf(standard), id);
            Assert.True(standard.IsSubsetOf(Keys(wide).ToHashSet()), id);
            Assert.All(wide.Aspects, a => Assert.True(a.Orb <= a.Allowed));
            Assert.All(Calculate(id, new OrbSettings(4, 3, 4, 4, 4)).Aspects, a => Assert.True(a.Orb <= 4));
        }
    }

    [Fact]
    public void The_Sun_and_Moon_allowance_applies_only_to_them()
    {
        var orbs = OrbSettings.Tight; // 6°, sextile 4°, luminaries +2°
        Assert.Equal(8, orbs.For(AspectType.Square, Planet.Sun, Planet.Mars));
        Assert.Equal(8, orbs.For(AspectType.Square, Planet.Mars, Planet.Moon));
        Assert.Equal(6, orbs.For(AspectType.Square, Planet.Mars, Planet.Saturn));
        Assert.Equal(6, orbs.For(AspectType.Sextile, Planet.Moon));
        Assert.Equal(4, orbs.For(AspectType.Sextile, Planet.Venus));

        // In a real chart: nothing between two non-luminaries is wider than 6°.
        var chart = Calculate("albert-einstein", orbs);
        Assert.All(chart.Aspects, a =>
        {
            bool luminary = a.PlanetA is Planet.Sun or Planet.Moon || a.PlanetB is Planet.Sun or Planet.Moon;
            Assert.True(a.Orb <= (a.Type == AspectType.Sextile ? 4 : 6) + (luminary ? 2 : 0));
        });
    }

    [Fact]
    public void The_worksheet_names_the_orbs_and_uses_them_for_the_angles()
    {
        var standard = WorksheetService.Build(Calculate("albert-einstein", OrbSettings.Lore));
        Assert.Equal("8° conjunction, 6° sextile, 8° square, 8° trine, 8° opposition (Standard); minor aspects off",
            standard.Facts.Single(f => f.Label == "Aspect orbs").Value);

        var tight = WorksheetService.Build(Calculate("albert-einstein", OrbSettings.Tight));
        Assert.EndsWith("2° more with the Sun or Moon (Tight); minor aspects off", tight.Facts.Single(f => f.Label == "Aspect orbs").Value);
        Assert.True(tight.Aspects.Count < standard.Aspects.Count);
        Assert.All(tight.Aspects.Where(a => a.B.IsAngle), a => Assert.True(a.Orb <= 8));

        Assert.Contains("(Custom)", new OrbSettings(7, 5, 7, 7, 7).Describe());
    }

    [Fact]
    public void Orbs_are_saved_read_back_and_defaulted_for_an_older_settings_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chosen = new ChartSettings(HouseSystem.WholeSign, NodeType.True) { Orbs = new OrbSettings(7, 5, 7.5, 7, 9, 1) };
            new SettingsService(dir).Save(chosen);
            Assert.Equal(chosen, new SettingsService(dir).Load());

            // A file written by Lore 1.5 or 1.6 has no orbs in it.
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Houses": "Koch", "Node": "True" }""");
            var older = new SettingsService(dir).Load();
            Assert.Equal(HouseSystem.Koch, older.Houses);
            Assert.Equal(OrbSettings.Lore, older.Orbs);

            // Out-of-range numbers are pulled back in.
            File.WriteAllText(Path.Combine(dir, "settings.json"),
                """{ "Houses": "Placidus", "Node": "Mean", "Orbs": { "Conjunction": 99, "Sextile": -3, "Square": 8, "Trine": 8, "Opposition": 8, "LuminaryBonus": 0 } }""");
            var wild = new SettingsService(dir).Load().Orbs;
            Assert.Equal(OrbSettings.Max, wild.Conjunction);
            Assert.Equal(0, wild.Sextile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
