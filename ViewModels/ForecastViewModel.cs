using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.ViewModels;

// State for the Forecast view: the selected chart, the stretch of days being looked at,
// and the transits found in it. Rebuilt (off the UI thread) whenever an input changes.
public sealed partial class ForecastViewModel : ObservableObject
{
    // The stretches offered, in days, in the order the drop-down lists them.
    public static readonly int[] Spans = [30, 91, 182, 365];

    private readonly TransitService? _transits;
    private readonly DailyInterpreter? _interpreter;
    private readonly DateTimeZone _zone = TransitService.LocalZone();

    // Bumped on every rebuild; a calculation that finishes after a newer one has started
    // is discarded, so changing the settings quickly never shows a stale list.
    private int _generation;

    // Stops the calculation a newer one has overtaken, rather than letting it run on
    // to a result nobody will see: holding an arrow key down in the list would
    // otherwise queue up a year's forecast for every person passed.
    private CancellationTokenSource? _cancel;

    public ForecastViewModel(TransitService? transits = null, DailyInterpreter? interpreter = null)
    {
        _transits = transits;
        _interpreter = interpreter;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial DateOnly Start { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    // Index into Spans; three months to begin with.
    [ObservableProperty]
    public partial int SpanIndex { get; set; } = 1;

    // Whether the Sun, Mercury, Venus and Mars are listed along with the slow movers.
    [ObservableProperty]
    public partial bool IncludeFast { get; set; } = true;

    // Whether the sky's own calendar (New and Full Moons, eclipses, stations, the slow
    // planets' changes of sign) is set among the transits.
    [ObservableProperty]
    public partial bool IncludeSky { get; set; } = true;

    [ObservableProperty]
    public partial ForecastReading? Reading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    // CalendarDatePicker speaks DateTimeOffset; noon keeps a daylight-saving edge from
    // tipping the date either way.
    public DateTimeOffset? PickerDate
    {
        get => new DateTimeOffset(Start.ToDateTime(new TimeOnly(12, 0)));
        set { if (value is { } v) Start = DateOnly.FromDateTime(v.Date); }
    }

    public string Title => Chart is null ? "" : $"{Chart.Celebrity.Name} — Transits ahead";

    public string Summary => Reading is null ? "" :
        $"{Reading.Passes.Count} {(Reading.Passes.Count == 1 ? "transit" : "transits")}" +
        (Reading.Sky.Count > 0 ? $" and {Reading.Sky.Count} {(Reading.Sky.Count == 1 ? "event" : "events")} in the sky" : "") +
        $" from {Reading.RangeText}" +
        (Chart?.Timed == false ? ". With no birth time, the Ascendant, Midheaven and natal Moon are left out." : ".");

    public IReadOnlyList<DailySection> Sections => Reading?.Sections ?? [];

    [RelayCommand]
    private void FromToday() => Start = DateOnly.FromDateTime(DateTime.Now);

    // The reading that sets this person's inventory answers beside their chart, if the
    // answers are to shape the wording here; null if there are none or they are not to.
    [ObservableProperty]
    public partial MirrorReading? Answers { get; set; }

    partial void OnAnswersChanged(MirrorReading? oldValue, MirrorReading? newValue)
    {
        // Only the chart on screen's own answers change anything it says.
        if (Chart is { } chart && (oldValue?.IsFor(chart) == true || newValue?.IsFor(chart) == true)) Rebuild();
    }

    partial void OnChartChanged(NatalChart? value)
    {
        OnPropertyChanged(nameof(Title));
        Reading = null; // the last person's list must not stay under the new heading
        Rebuild();
    }

    partial void OnStartChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(PickerDate));
        Rebuild();
    }

    partial void OnSpanIndexChanged(int value) => Rebuild();
    partial void OnIncludeFastChanged(bool value) => Rebuild();
    partial void OnIncludeSkyChanged(bool value) => Rebuild();

    partial void OnReadingChanged(ForecastReading? value)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Sections));
    }

    private async void Rebuild()
    {
        int generation = ++_generation;
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        var chart = Chart;
        var start = Start;
        int days = Spans[Math.Clamp(SpanIndex, 0, Spans.Length - 1)];
        bool fast = IncludeFast, sky = IncludeSky;
        var answers = Answers;
        ErrorText = "";

        if (chart is null || _transits is null || _interpreter is null)
        {
            Reading = null;
            IsBusy = false; // nothing is running for this (empty) state
            return;
        }

        IsBusy = true;
        try
        {
            var reading = await Task.Run(() =>
                _interpreter.ComposeForecast(chart, _transits.Forecast(chart, start, days, _zone, fast, cancel.Token), start, days, _zone,
                    sky ? _transits.SkyCalendar(chart, start, days, _zone, cancel.Token) : null, answers));
            if (generation == _generation)
                Reading = reading;
        }
        catch (OperationCanceledException)
        {
            // overtaken by a newer forecast
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Forecast for chart {chart.Celebrity.Id} from {start} failed: {ex}");
            if (generation == _generation)
            {
                Reading = null;
                ErrorText = $"Could not build the forecast: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
                IsBusy = false;
        }
    }
}
