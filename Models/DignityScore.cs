namespace Lore.Models;

// A chart's overall traditional condition, from summing the essential + accidental
// dignities of the seven classical planets (Sun–Saturn). Outer planets, the nodes,
// Chiron and Lilith have no traditional dignities and are not scored.
public enum ChartVerdict { Alarming, Ordinary, Extraordinary }

public static class ChartVerdictExtensions
{
    public static string Label(this ChartVerdict v) => v switch
    {
        ChartVerdict.Extraordinary => "Extraordinary",
        ChartVerdict.Alarming      => "Alarming",
        _                          => "Ordinary",
    };

    // Hex both WinUI (via ColorHelper) and QuestPDF understand.
    public static string ColorHex(this ChartVerdict v) => v switch
    {
        ChartVerdict.Extraordinary => "#3FB84F", // green
        ChartVerdict.Alarming      => "#E5534B", // red
        _                          => "#9AA0A6", // neutral grey
    };
}

// One planet's dignity tally. Notes is a short human-readable trail ("Domicile +5,
// 10th house +5, combust -5").
public sealed record PlanetDignity(Planet Planet, int Essential, int Accidental, string Notes)
{
    public int Total => Essential + Accidental;
}

public sealed record ChartScore(
    int Total,
    ChartVerdict Verdict,
    bool IsDayChart,
    IReadOnlyList<PlanetDignity> Planets);
