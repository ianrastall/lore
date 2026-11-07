using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.ViewModels;

// State for the Sky view: the date being read, the reading of that day's sky for
// everyone, and the tone of each day of its week. No chart is involved. Worked out off
// the UI thread, and only while the view is open.
public sealed partial class SkyViewModel : ObservableObject
{
    private readonly TransitService? _transits;
    private readonly SkyInterpreter? _interpreter;
    private readonly DateTimeZone _zone;
    private readonly LatestOnly _latest = new(), _latestWeek = new();

    public SkyViewModel(TransitService? transits = null, SkyInterpreter? interpreter = null, DateTimeZone? zone = null)
    {
        _transits = transits;
        _interpreter = interpreter;
        _zone = zone ?? TransitService.LocalZone();
    }

    // False while the view is closed: nothing is calculated, and the reading is made
    // when the view is next opened.
    [ObservableProperty]
    public partial bool IsActive { get; set; } = true;

    private bool _stale = true, _weekStale = true;

    partial void OnIsActiveChanged(bool value)
    {
        if (!value) return;
        if (_stale) Rebuild();
        if (_weekStale) RebuildWeek();
    }

    [ObservableProperty]
    public partial DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    // Where the reader is, for sunrise, sunset and the planetary hours.
    [ObservableProperty]
    public partial HomePlace? Home { get; set; }

    [ObservableProperty]
    public partial SkyReading? Reading { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SkyDayTone> Week { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public DateTimeOffset? PickerDate
    {
        get => new DateTimeOffset(Date.ToDateTime(new TimeOnly(12, 0)));
        set { if (value is { } v) Date = DateOnly.FromDateTime(v.Date); }
    }

    public string DateText => Date.ToString("dddd, d MMMM yyyy");
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Now);

    public string ToneText => Reading is null ? "" : $"The day reads as {Reading.Tone.Label()}";
    public string ToneColorHex => Reading?.Tone.ColorHex() ?? "#9AA0A6";

    public string WheelCaption => Reading is null ? "" :
        $"The sky at the middle of {Reading.Date:d MMMM yyyy}, 0° Aries at the left   ·   " +
        "the lines are the day's aspects, the ones the reading uses drawn heavier";

    public IReadOnlyList<DailySection> Sections => Reading?.Sections ?? [];
    public IReadOnlyList<string> Trace => Reading?.Trace ?? [];
    public string TraceHeader => Reading is null ? "" :
        $"Why this reading? — all {Reading.Aspects.Count} aspects in effect " +
        $"(the Moon's within {TransitService.Orb:0}°, the others within {TransitService.SkyOrb(Planet.Sun):0}°)";

    // The Monday that starts the week strip.
    private DateOnly WeekStart => Date.AddDays(-(((int)Date.DayOfWeek + 6) % 7));

    [RelayCommand]
    private void PreviousDay() => Date = Date.AddDays(-1);

    [RelayCommand]
    private void NextDay() => Date = Date.AddDays(1);

    [RelayCommand]
    private void Today() => Date = DateOnly.FromDateTime(DateTime.Now);

    public void ShowDay(DateOnly date) => Date = date;

    partial void OnHomeChanged(HomePlace? value) => Rebuild();

    partial void OnDateChanged(DateOnly oldValue, DateOnly newValue)
    {
        OnPropertyChanged(nameof(PickerDate));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(DateText));
        Rebuild();
        // Moving within the strip keeps it; a date in another week brings that week.
        DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        if (Monday(oldValue) != Monday(newValue) || Week.Count == 0) RebuildWeek();
    }

    partial void OnReadingChanged(SkyReading? value)
    {
        OnPropertyChanged(nameof(ToneText));
        OnPropertyChanged(nameof(ToneColorHex));
        OnPropertyChanged(nameof(WheelCaption));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(Trace));
        OnPropertyChanged(nameof(TraceHeader));
    }

    private async void Rebuild()
    {
        var date = Date;
        var home = Home;
        ErrorText = "";
        if (_transits is null || _interpreter is null || !IsActive)
        {
            _stale = true;
            _latest.Cancel();
            Reading = null;
            IsBusy = false;
            return;
        }
        _stale = false;

        if (date.Year < 1200 || date.Year >= 2400)
        {
            _latest.Cancel();
            Reading = null;
            IsBusy = false;
            ErrorText = "Choose a date between 1200 and 2399.";
            return;
        }

        await _latest.Run(
            cancel =>
            {
                var sky = _transits.ScanSky(date, _zone, home, cancel: cancel);
                cancel.ThrowIfCancellationRequested();
                return _interpreter.Compose(sky, _zone);
            },
            busy => IsBusy = busy,
            reading => Reading = reading,
            ex =>
            {
                Diagnostics.Log($"Sky reading for {date} failed: {ex}");
                Reading = null;
                ErrorText = $"Could not build the reading of the sky: {ex.Message}";
            });
    }

    private async void RebuildWeek()
    {
        var start = WeekStart;
        if (_transits is null || !IsActive || start.Year < 1200 || start.AddDays(6).Year >= 2400)
        {
            _weekStale = _transits is not null && !IsActive;
            _latestWeek.Cancel();
            Week = [];
            return;
        }
        _weekStale = false;

        await _latestWeek.Run(
            cancel => Enumerable.Range(0, 7)
                .Select(i => start.AddDays(i))
                .Select(d => new SkyDayTone(d, SkyInterpreter.ToneOf(_transits.ScanSky(d, _zone, aspectsOnly: true, cancel: cancel))))
                .ToList(),
            _ => { },
            week => Week = week,
            ex =>
            {
                Diagnostics.Log($"Sky week from {start} failed: {ex}");
                Week = [];
            });
    }
}
