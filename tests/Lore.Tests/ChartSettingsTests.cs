using Lore.Models;
using Lore.Services;

namespace Lore.Tests;

// The house system and node type: what each changes, and what it must leave alone.
public class ChartSettingsTests
{
    private static NatalChart Calculate(string id, ChartSettings settings) =>
        new ChartService(Repo.Ephemeris) { Settings = settings }.Calculate(Repo.Figure(id));

    [Fact]
    public void Whole_sign_houses_start_at_the_Ascendants_sign()
    {
        var chart = Calculate("elvis-presley", new ChartSettings(HouseSystem.WholeSign));
        int rising = (int)ZodiacSignExtensions.FromLongitude(chart.Ascendant);

        for (int house = 1; house <= 12; house++)
            Assert.Equal(((rising + house - 1) % 12) * 30.0, chart.GetHouse(house).Longitude, 6);
        Assert.Equal("Whole Sign houses", chart.HouseSystemLabel);
    }

    [Fact]
    public void Equal_houses_start_at_the_Ascendant()
    {
        var chart = Calculate("elvis-presley", new ChartSettings(HouseSystem.Equal));
        Assert.Equal(chart.Ascendant, chart.GetHouse(1).Longitude, 6);
        Assert.Equal((chart.Ascendant + 90) % 360, chart.GetHouse(4).Longitude, 6);
    }

    [Fact]
    public void The_house_system_moves_no_planet_and_no_angle()
    {
        var placidus = Calculate("elvis-presley", ChartSettings.Default);
        foreach (var system in Enum.GetValues<HouseSystem>())
        {
            var other = Calculate("elvis-presley", new ChartSettings(system));
            Assert.Equal(placidus.Ascendant, other.Ascendant, 9);
            Assert.Equal(placidus.Midheaven, other.Midheaven, 9);
            Assert.Equal(placidus.Planets.Select(p => p.Longitude), other.Planets.Select(p => p.Longitude));
        }
    }

    [Fact]
    public void Day_or_night_does_not_depend_on_the_house_system()
    {
        // Sect is the Sun above or below the horizon, so every figure must come out the
        // same whichever way the houses are cut.
        var placidus = new ChartService(Repo.Ephemeris);
        var whole = new ChartService(Repo.Ephemeris) { Settings = new ChartSettings(HouseSystem.WholeSign) };
        foreach (var f in Repo.Figures)
        {
            var p = placidus.Calculate(f);
            // In Placidus, houses 7-12 are the half of the sky above the horizon.
            bool above = p.GetHouseForLongitude(p.GetPlanet(Planet.Sun)!.Longitude) >= 7;
            Assert.True(above == p.IsDayChart, f.Name);
            Assert.True(above == whole.Calculate(f).IsDayChart, f.Name);
        }
    }

    [Fact]
    public void The_true_node_differs_from_the_mean_node_by_under_two_degrees()
    {
        var mean = Calculate("elvis-presley", ChartSettings.Default);
        var real = Calculate("elvis-presley", new ChartSettings(Node: NodeType.True));

        double apart = Repo.ArcMinutesBetween(
            mean.GetPlanet(Planet.NorthNode)!.Longitude, real.GetPlanet(Planet.NorthNode)!.Longitude);
        Assert.InRange(apart, 0.01, 120);

        // Nothing else moves.
        Assert.Equal(mean.GetPlanet(Planet.Moon)!.Longitude, real.GetPlanet(Planet.Moon)!.Longitude);
        Assert.Equal(NodeType.True, real.Settings.Node);
    }

    [Fact]
    public void Settings_are_saved_and_read_back()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(ChartSettings.Default, new SettingsService(dir).Load());

            var chosen = new ChartSettings(HouseSystem.Koch, NodeType.True);
            new SettingsService(dir).Save(chosen);
            Assert.Equal(chosen, new SettingsService(dir).Load());

            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
            Assert.Equal(ChartSettings.Default, new SettingsService(dir).Load());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_last_chart_and_view_are_remembered()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lore-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fresh = new SettingsService(dir).LoadUi();
            Assert.Null(fresh.LastChartId);
            Assert.Equal("Chart", fresh.LastView);

            // Written behind the scenes; Flush waits for it, as closing the window does.
            var saving = new SettingsService(dir);
            saving.SaveUi(new SettingsService.UiState("albert-einstein", "Worksheet"));
            saving.Flush();
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Equal(new SettingsService.UiState("albert-einstein", "Worksheet"), new SettingsService(dir).LoadUi());

            File.WriteAllText(Path.Combine(dir, "ui.json"), "not json at all");
            Assert.Equal(new SettingsService.UiState(), new SettingsService(dir).LoadUi());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
