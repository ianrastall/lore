using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Media;
using NodaTime;
using Windows.UI;

namespace Lore.ViewModels;

// State for the Daily view: the selected chart, the date being read, and the generated
// reading. The reading is rebuilt (off the UI thread) whenever either input changes.
public sealed partial class DailyViewModel : ObservableObject
{
    private readonly TransitService? _transits;
    private readonly DailyInterpreter? _interpreter;
    private readonly DateTimeZone _zone = TransitService.LocalZone();

    // Bumped on every rebuild; a calculation that finishes after a newer one has started
    // is discarded, so flicking quickly through dates or people never shows a stale day.
    private int _generation;

    public DailyViewModel(TransitService? transits = null, DailyInterpreter? interpreter = null)
    {
        _transits = transits;
        _interpreter = interpreter;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    [ObservableProperty]
    public partial DailyReading? Reading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    // CalendarDatePicker speaks DateTimeOffset; noon keeps a daylight-saving edge from
    // tipping the date either way.
    public DateTimeOffset? PickerDate
    {
        get => new DateTimeOffset(Date.ToDateTime(new TimeOnly(12, 0)));
        set { if (value is { } v) Date = DateOnly.FromDateTime(v.Date); }
    }

    public string Title => Chart is null ? "" : $"{Chart.Celebrity.Name} — Daily horoscope";
    public string DateText => Reading?.DateText ?? Date.ToString("dddd, d MMMM yyyy");
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Now);

    public string ToneText => Reading is null ? "" : $"The day reads as {Reading.Tone.Label()}";
    public SolidColorBrush ToneBrush => new(ParseHex(Reading?.Tone.ColorHex() ?? "#9AA0A6"));

    // Under the wheel beside the reading.
    public string WheelCaption => Reading is null ? "" :
        $"Inner wheel: the birth chart   ·   Outer wheel: the sky at the middle of {Reading.Date:d MMMM}   ·   " +
        "the lines are the day's transits, the ones the reading uses drawn heavier";

    public IReadOnlyList<DailySection> Sections => Reading?.Sections ?? [];
    public IReadOnlyList<string> Trace => Reading?.Trace ?? [];
    public string TraceHeader => Reading is null ? "" :
        $"Why this reading? — all {Reading.Events.Count} transits in effect today (within {TransitService.Orb:0}° of exact)";

    [RelayCommand]
    private void PreviousDay() => Date = Date.AddDays(-1);

    [RelayCommand]
    private void NextDay() => Date = Date.AddDays(1);

    [RelayCommand]
    private void Today() => Date = DateOnly.FromDateTime(DateTime.Now);

    partial void OnChartChanged(NatalChart? value)
    {
        OnPropertyChanged(nameof(Title));
        Rebuild();
    }

    partial void OnDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(PickerDate));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(DateText));
        Rebuild();
    }

    partial void OnReadingChanged(DailyReading? value)
    {
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(ToneText));
        OnPropertyChanged(nameof(ToneBrush));
        OnPropertyChanged(nameof(WheelCaption));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(Trace));
        OnPropertyChanged(nameof(TraceHeader));
    }

    private async void Rebuild()
    {
        int generation = ++_generation;
        var chart = Chart;
        var date = Date;
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
                _interpreter.Compose(chart, _transits.Scan(chart, date, _zone), _zone));
            if (generation == _generation)
                Reading = reading;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Daily reading for {chart.Celebrity.Name} on {date} failed: {ex}");
            if (generation == _generation)
            {
                Reading = null;
                ErrorText = $"Could not build the daily reading: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
                IsBusy = false;
        }
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
