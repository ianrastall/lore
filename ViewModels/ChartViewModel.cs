using CommunityToolkit.Mvvm.ComponentModel;
using Lore.Models;
using Lore.Services;

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

    // Set while a new chart is being taken in, when the margin is reset as part of that
    // and the analysis arrives with the chart rather than being rebuilt for the change.
    private bool _loadingChart;

    // The analysis that belongs to a chart being shown (see Show).
    private TimeSensitivity? _incoming;
    private bool _hasIncoming;

    // Bumped whenever the analysis is asked for again; one that finishes after a newer
    // request (or after another chart has been shown) is dropped.
    private int _sensitivityGeneration;
    private CancellationTokenSource? _sensitivityCancel;

    // The analysis recalculates the chart for every minute of the margin (361 charts at
    // its widest), so it is never run on the UI thread: MainViewModel works it out
    // beside the chart itself and hands both over together.
    public static TimeSensitivity? Analyse(ChartService charts, NatalChart chart, CancellationToken cancel = default) =>
        TimeSensitivityService.For(charts, chart, chart.Celebrity.BirthTimeUncertaintyMinutes, cancel);

    public void Show(NatalChart? chart, TimeSensitivity? sensitivity)
    {
        _incoming = sensitivity;
        _hasIncoming = true;
        try { Chart = chart; }
        finally { _hasIncoming = false; _incoming = null; }
    }

    partial void OnUncertaintyMinutesChanged(double value)
    {
        if (!_loadingChart) RebuildSensitivity();
    }

    partial void OnSensitivityChanged(TimeSensitivity? value)
    {
        OnPropertyChanged(nameof(HasSensitivity));
        OnPropertyChanged(nameof(SensitivityHasChanges));
        OnPropertyChanged(nameof(BigThreeText));
    }

    // The margin was changed by hand: work the analysis out again, off the UI thread,
    // after a short pause so that holding the box's arrow down asks only once.
    private async void RebuildSensitivity()
    {
        int generation = ++_sensitivityGeneration;
        _sensitivityCancel?.Cancel();
        var cancel = _sensitivityCancel = new CancellationTokenSource();
        var chart = Chart;
        var charts = _charts;
        int minutes = double.IsNaN(UncertaintyMinutes) ? 0 : (int)UncertaintyMinutes;

        if (chart is null || charts is null)
        {
            Sensitivity = null;
            return;
        }

        try
        {
            await Task.Delay(150, cancel.Token);
            var result = await Task.Run(() => TimeSensitivityService.For(charts, chart, minutes, cancel.Token), cancel.Token);
            if (generation == _sensitivityGeneration)
                Sensitivity = result;
        }
        catch (OperationCanceledException)
        {
            // overtaken by a newer margin or another chart
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Birth-time check for chart {chart.Celebrity.Id} failed: {ex}");
            if (generation == _sensitivityGeneration)
                Sensitivity = null;
        }
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

    // Technical chart angles — secondary, shown small (see NatalChart.AnglesLine, which
    // the PDF export prints too).
    public string AnglesText => Chart?.AnglesLine ?? "";

    // Traditional dignity score + verdict, shown as a coloured pill.
    private ChartScore? _score;

    public string ScoreText =>
        Chart is null ? "" :
        _score is null ? "Dignity score needs a birth time" :
        $"Dignity score {_score.Total:+#;-#;0}  ·  {_score.Verdict.Label()}";

    // The pill's colour as "#RRGGBB"; the view makes the brush.
    public string ScoreColorHex => _score?.Verdict.ColorHex() ?? "#9AA0A6";

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
        // Whatever analysis was on its way belonged to the chart before this one.
        _sensitivityGeneration++;
        _sensitivityCancel?.Cancel();
        _loadingChart = true;
        UncertaintyMinutes = value?.Celebrity.BirthTimeUncertaintyMinutes ?? 0;
        _loadingChart = false;
        // Normally handed over with the chart (see Show); worked out here only when a
        // chart is set on its own.
        Sensitivity = _hasIncoming ? _incoming
            : value is null || _charts is null ? null
            : Analyse(_charts, value);
        // After the analysis above: the report draws on it for an untimed Moon.
        ReportSections = (_interpreter is not null && value is not null)
            ? _interpreter.Interpret(value, Sensitivity?.MoonSigns)
            : [];
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(BigThreeText));
        OnPropertyChanged(nameof(AnglesText));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(ScoreColorHex));
    }
}
