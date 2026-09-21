using CommunityToolkit.Mvvm.ComponentModel;
using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.ViewModels;

public sealed partial class ChartViewModel : ObservableObject
{
    private readonly ChartInterpreter? _interpreter;

    public ChartViewModel(ChartInterpreter? interpreter = null)
    {
        _interpreter = interpreter;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial PlanetPosition? SelectedPlanet { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportSection> ReportSections { get; set; } = [];

    public string Title => Chart is null ? "" : Chart.Celebrity.Name;

    public string SubTitle => Chart is null ? "" :
        $"{Chart.Celebrity.BirthDate}  ·  {Chart.Celebrity.BirthPlace}" +
        (Chart.Celebrity.BirthTimeKnown ? $"  ·  {Chart.Celebrity.BirthTime}" : "  ·  time unknown");

    // The "Big Three" — what people actually mean by their sign. Sun first and foremost:
    // "I'm a Libra" is the Sun sign. Moon and Rising round it out.
    public string BigThreeText => Chart is null ? "" : BuildBigThree(Chart);

    private static string BuildBigThree(NatalChart chart)
    {
        var parts = new List<string>();
        if (chart.GetPlanet(Planet.Sun) is { } sun)   parts.Add($"☉ Sun {sun.Sign.Name()}");
        if (chart.GetPlanet(Planet.Moon) is { } moon)  parts.Add($"☽ Moon {moon.Sign.Name()}");
        parts.Add($"↑ Rising {ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name()}");
        return string.Join("     ·     ", parts);
    }

    // Technical chart angles — secondary, shown small. The Midheaven is NOT the "sign";
    // it lives here so it can't be mistaken for one.
    public string AnglesText => Chart is null ? "" :
        $"Ascendant {ZodiacSignExtensions.FromLongitude(Chart.Ascendant).Name()}" +
        $"   ·   Midheaven {ZodiacSignExtensions.FromLongitude(Chart.Midheaven).Name()}";

    // Traditional dignity score + verdict, shown as a coloured pill.
    private ChartScore? _score;

    public string ScoreText => _score is null ? "" :
        $"Dignity score {_score.Total:+#;-#;0}  ·  {_score.Verdict.Label()}";

    public SolidColorBrush ScoreBrush => new(ParseHex(_score?.Verdict.ColorHex() ?? "#9AA0A6"));

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    partial void OnChartChanged(NatalChart? value)
    {
        SelectedPlanet = null;
        _score = value is null ? null : DignityService.Compute(value);
        ReportSections = (_interpreter is not null && value is not null)
            ? _interpreter.Interpret(value)
            : [];
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(BigThreeText));
        OnPropertyChanged(nameof(AnglesText));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(ScoreBrush));
    }
}
