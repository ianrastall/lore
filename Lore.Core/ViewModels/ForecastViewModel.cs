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


    // One forecast at a time, the latest asked for: changing the settings quickly never
    // shows a stale list, and a forecast overtaken is stopped, not left to run on to a
    // result nobody will see.
    private readonly LatestOnly _latest = new();

    public ForecastViewModel(TransitService? transits = null, DailyInterpreter? interpreter = null)
    {
        _transits = transits;
        _interpreter = interpreter;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    // False while the Forecast view is not the one open. A forecast is a scan of months
    // of sky, so nothing is worked out then: what would have been is worked out when the
    // view is next opened. (True unless the main view model says otherwise.)
    [ObservableProperty]
    public partial bool IsActive { get; set; } = true;

    // An input changed while the view was closed; the reading on hand, if any, is not
    // for what is now set.
    private bool _stale;

    partial void OnIsActiveChanged(bool value)
    {
        if (value && _stale) Rebuild();
    }

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
        var chart = Chart;
        var start = Start;
        int days = Spans[Math.Clamp(SpanIndex, 0, Spans.Length - 1)];
        bool fast = IncludeFast, sky = IncludeSky;
        var answers = Answers;
        ErrorText = "";

        // Nothing to forecast, or not on screen. Off screen nothing is worked out, and
        // what was there (for other dates, or other answers) is not kept as if it were
        // current: it is made when the view is next opened.
        bool nothing = chart is null || _transits is null || _interpreter is null;
        if (nothing || !IsActive)
        {
            _stale = !nothing;
            _latest.Cancel();
            Reading = null;
            IsBusy = false; // nothing is running for this state
            return;
        }
        _stale = false;

        await _latest.Run(
            cancel => _interpreter!.ComposeForecast(chart!, _transits!.Forecast(chart!, start, days, _zone, fast, cancel), start, days, _zone,
                sky ? _transits.SkyCalendar(chart!, start, days, _zone, cancel) : null, answers),
            busy => IsBusy = busy,
            reading => Reading = reading,
            ex =>
            {
                Diagnostics.Log($"Forecast for chart {chart!.Celebrity.Id} from {start} failed: {ex}");
                Reading = null;
                ErrorText = $"Could not build the forecast: {ex.Message}";
            });
    }
}
