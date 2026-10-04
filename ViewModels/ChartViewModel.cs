using CommunityToolkit.Mvvm.ComponentModel;
using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lore.ViewModels;

public sealed partial class ChartViewModel : ObservableObject
{
    private readonly ChartInterpreter? _interpreter;
    private readonly ChartService? _charts;

    public ChartViewModel(ChartInterpreter? interpreter = null, ChartService? charts = null)
    {
        _interpreter = interpreter;
        _charts = charts;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial PlanetPosition? SelectedPlanet { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReportSection> ReportSections { get; set; } = [];

    public string Title => Chart is null ? "" : Chart.Celebrity.Name;

    // The Chart view's aspect list: planet to planet, then planet to angle.
    public IReadOnlyList<string> AspectLines => Chart is null ? [] :
        [.. Chart.Aspects.Select(a => a.Description), .. Chart.AngleAspects.Select(a => a.Description)];

    // The numbers behind the chart, for the Worksheet view.
    [ObservableProperty]
    public partial Worksheet? Worksheet { get; set; }

    public bool WorksheetHasCusps => Worksheet?.HasCusps == true;

    // "What if the birth time is off?" — the margin being tested, in minutes either way.
    // Starts at the chart's own stated margin; can be changed to try any other.
    [ObservableProperty]
    public partial double UncertaintyMinutes { get; set; }

    [ObservableProperty]
    public partial TimeSensitivity? Sensitivity { get; set; }

    public bool CanTestTime => Chart?.Timed == true;
    public bool HasChart => Chart is not null;
    public string SensitivityHeading => Chart?.Timed == false ? "Without a birth time" : "If the birth time is off";
    public bool HasSensitivity => Sensitivity is not null;
    public bool SensitivityHasChanges => Sensitivity?.HasChanges == true;

    partial void OnUncertaintyMinutesChanged(double value) => RebuildSensitivity();

    private void RebuildSensitivity()
    {
        int minutes = double.IsNaN(UncertaintyMinutes) ? 0 : (int)UncertaintyMinutes;
        Sensitivity = Chart is null || _charts is null ? null
            : Chart.Timed ? TimeSensitivityService.Analyse(_charts, Chart.Celebrity, minutes)
            : TimeSensitivityService.AnalyseDay(_charts, Chart.Celebrity);
        OnPropertyChanged(nameof(HasSensitivity));
        OnPropertyChanged(nameof(SensitivityHasChanges));
        OnPropertyChanged(nameof(BigThreeText));
    }

    public string WorksheetTitle => Chart is null ? "" : $"{Chart.Celebrity.Name} — Worksheet";

    public string SubTitle => Chart is null ? "" :
        $"{Chart.Celebrity.BirthDateLabel}  ·  {Chart.Celebrity.BirthPlace}" +
        (Chart.Celebrity.BirthTimeKnown ? $"  ·  {Chart.Celebrity.BirthTime}" : "  ·  time unknown") +
        (Chart.Timed && Chart.Celebrity.BirthTimeUncertaintyMinutes > 0
            ? $" (± {Chart.Celebrity.BirthTimeUncertaintyMinutes} min)" : "") +
        (string.IsNullOrWhiteSpace(Chart.Celebrity.RoddenRating) ? "" : $"  ·  Rodden {Chart.Celebrity.RoddenRating}");

    // The "Big Three" — what people actually mean by their sign. Sun first and foremost:
    // "I'm a Libra" is the Sun sign. Moon and Rising round it out.
    public string BigThreeText => Chart is null ? "" : BuildBigThree(Chart, Sensitivity);

    private static string BuildBigThree(NatalChart chart, TimeSensitivity? sensitivity)
    {
        var parts = new List<string>();
        if (chart.GetPlanet(Planet.Sun) is { } sun)   parts.Add($"☉ Sun {sun.Sign.Name()}");
        // With no birth time the Moon may have changed sign during the day: name both.
        if (!chart.Timed && sensitivity is { MoonSigns.Count: > 1 } s)
            parts.Add($"☽ Moon {string.Join(" or ", s.MoonSigns.Select(x => x.Name()))}");
        else if (chart.GetPlanet(Planet.Moon) is { } moon)  parts.Add($"☽ Moon {moon.Sign.Name()}");
        if (chart.Timed)
            parts.Add($"↑ Rising {ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name()}");
        return string.Join("     ·     ", parts);
    }

    // Technical chart angles — secondary, shown small. The Midheaven is NOT the "sign";
    // it lives here so it can't be mistaken for one.
    public string AnglesText => Chart is null ? "" : FormatAngles(Chart);

    // Shared with the PDF export so both print the same line.
    public static string FormatAngles(NatalChart chart) => !chart.Timed
        ? $"Birth time unknown — planets are placed for noon; no Ascendant, Midheaven or houses   ·   {chart.Settings.Node.Name()}"
        : $"Ascendant {ZodiacSignExtensions.FromLongitude(chart.Ascendant).Name()} {ZodiacSignExtensions.FormatDegreeInSign(chart.Ascendant)}" +
          $"   ·   Midheaven {ZodiacSignExtensions.FromLongitude(chart.Midheaven).Name()} {ZodiacSignExtensions.FormatDegreeInSign(chart.Midheaven)}" +
          $"   ·   {chart.HouseSystemLabel}   ·   {chart.Settings.Node.Name()}";

    // Traditional dignity score + verdict, shown as a coloured pill.
    private ChartScore? _score;

    public string ScoreText =>
        Chart is null ? "" :
        _score is null ? "Dignity score needs a birth time" :
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
        _score = value is null ? null : DignityService.ComputeIfTimed(value);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AspectLines));
        OnPropertyChanged(nameof(WorksheetTitle));
        Worksheet = value is null ? null : WorksheetService.Build(value);
        OnPropertyChanged(nameof(CanTestTime));
        OnPropertyChanged(nameof(HasChart));
        OnPropertyChanged(nameof(SensitivityHeading));
        // Setting the margin rebuilds the analysis only if the number changed, so
        // rebuild explicitly: the chart itself has.
        UncertaintyMinutes = value?.Celebrity.BirthTimeUncertaintyMinutes ?? 0;
        RebuildSensitivity();
        // After the analysis above: the report draws on it for an untimed Moon.
        ReportSections = (_interpreter is not null && value is not null)
            ? _interpreter.Interpret(value, Sensitivity?.MoonSigns)
            : [];
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(BigThreeText));
        OnPropertyChanged(nameof(AnglesText));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(ScoreBrush));
    }
}
