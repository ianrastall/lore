namespace Lore.Models;

public enum HouseSystem { Placidus, WholeSign, Equal, Koch }

// Which North Node: the mean node (a smoothed average that always moves backwards) or
// the true node (the Moon's actual orbit crossing, which wobbles up to 1.5° either side).
public enum NodeType { Mean, True }

// Which Black Moon Lilith: the mean lunar apogee (a smoothed point that moves steadily
// forwards) or the true, "osculating" apogee (the far point of the Moon's orbit as it
// stands at that instant, which swings up to 30° either side of the mean one).
public enum LilithType { Mean, True }

// The calculation choices astrologers disagree on. They apply to every chart Lore
// calculates, and each chart records the ones it was calculated with.
public sealed record ChartSettings(
    HouseSystem Houses = HouseSystem.Placidus, NodeType Node = NodeType.Mean, LilithType Lilith = LilithType.Mean)
{
    public static readonly ChartSettings Default = new();

    // Aspect orbs for the birth chart. A settings file saved before these existed has
    // none, and gets Lore's standard set.
    public OrbSettings Orbs { get; init; } = OrbSettings.Lore;
}

public static class ChartSettingsExtensions
{
    public static string Name(this HouseSystem h) => h switch
    {
        HouseSystem.WholeSign => "Whole Sign",
        HouseSystem.Equal     => "Equal",
        HouseSystem.Koch      => "Koch",
        _                     => "Placidus",
    };

    // The letter the Swiss Ephemeris knows the system by.
    public static char SweCode(this HouseSystem h) => h switch
    {
        HouseSystem.WholeSign => 'W',
        HouseSystem.Equal     => 'E',
        HouseSystem.Koch      => 'K',
        _                     => 'P',
    };

    public static string Name(this NodeType n) => n == NodeType.True ? "True node" : "Mean node";

    public static string Name(this LilithType l) => l == LilithType.True ? "True Lilith" : "Mean Lilith";
}
