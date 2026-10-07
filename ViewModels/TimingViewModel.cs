using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using NodaTime;

namespace Lore.ViewModels;

// State for the Timing view: the selected chart, the date being looked at, and the solar
// return, profection, progressions and solar arc for it. Rebuilt (off the UI thread) whenever an input changes.
public sealed partial class TimingViewModel : ObservableObject
{
    private readonly TimingService? _timing;
    private readonly CityService? _cities;
    private readonly DateTimeZone _zone = TransitService.LocalZone();

    // Bumped on every rebuild; a calculation that finishes after a newer one has started
    // is discarded, so changing the date quickly never shows a stale result.
    private int _generation;

    public TimingViewModel(TimingService? timing = null, CityService? cities = null)
    {
        _timing = timing;
        _cities = cities;
    }

    [ObservableProperty]
    public partial NatalChart? Chart { get; set; }

    [ObservableProperty]
    public partial DateOnly AsOf { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    // Where the solar return is cast for, and the birth chart relocated to; null for the
    // birthplace. It belongs to the person (where they were living), so it is dropped
    // when another chart is picked.
    [ObservableProperty]
    public partial ReturnPlace? Place { get; set; }

    [ObservableProperty]
    public partial TimingReading? Reading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    // CalendarDatePicker speaks DateTimeOffset; noon keeps a daylight-saving edge from
    // tipping the date either way.
    public DateTimeOffset? PickerDate
    {
        get => new DateTimeOffset(AsOf.ToDateTime(new TimeOnly(12, 0)));
        set { if (value is { } v) AsOf = DateOnly.FromDateTime(v.Date); }
    }

    public string Title => Chart is null ? "" : $"{Chart.Celebrity.Name} — Timing";
    public IReadOnlyList<DailySection> Sections => Reading?.Sections ?? [];

    // The return chart, for the wheel beside the text; null when there is none.
    public NatalChart? ReturnChart => Reading?.Return?.Chart;
    public bool HasReturnChart => ReturnChart is not null;
    public string WheelCaption => Reading?.Return is { } r
        ? $"Solar return chart, {r.Utc.Year}–{r.Utc.Year + 1}, cast for {r.Place?.Name ?? "the birthplace"}"
        : "";

    // A return can only be cast (for anywhere) when the birth time is known.
    public bool CanChoosePlace => Chart?.Timed == true;
    public bool HasPlace => Place is not null;
    public string PlaceText => Chart is null ? "" :
        Place is { } p ? $"Return cast for, and birth chart relocated to, {p.Name}" : $"Return cast for the birthplace, {Chart.Celebrity.BirthPlace}";

    // Cities matching what has been typed in the "cast for" box.
    public IReadOnlyList<City> Suggest(string? query) => _cities?.Search(query) ?? [];

    public void CastFor(City city) => Place = new ReturnPlace(city.Display, city.Latitude, city.Longitude);

    [RelayCommand]
    private void UseBirthplace() => Place = null;

    [RelayCommand]
    private void Today() => AsOf = DateOnly.FromDateTime(DateTime.Now);

    [RelayCommand]
    private void PreviousYear() => AsOf = AsOf.AddYears(-1);

    [RelayCommand]
    private void NextYear() => AsOf = AsOf.AddYears(1);

    partial void OnChartChanged(NatalChart? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanChoosePlace));
        OnPropertyChanged(nameof(PlaceText));
        if (Place is not null) Place = null; // which rebuilds
        else Rebuild();
    }

    partial void OnPlaceChanged(ReturnPlace? value)
    {
        OnPropertyChanged(nameof(HasPlace));
        OnPropertyChanged(nameof(PlaceText));
        Rebuild();
    }

    partial void OnAsOfChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(PickerDate));
        Rebuild();
    }

    partial void OnReadingChanged(TimingReading? value)
    {
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(ReturnChart));
        OnPropertyChanged(nameof(HasReturnChart));
        OnPropertyChanged(nameof(WheelCaption));
    }

    private async void Rebuild()
    {
        int generation = ++_generation;
        var chart = Chart;
        var asOf = AsOf;
        var place = Place;
        ErrorText = "";

        if (chart is null || _timing is null)
        {
            Reading = null;
            IsBusy = false; // nothing is running for this (empty) state
            return;
        }

        IsBusy = true;
        try
        {
            var reading = await Task.Run(() => _timing.Compose(chart, asOf, _zone, place));
            if (generation == _generation)
                Reading = reading;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Timing for {chart.Celebrity.Name} on {asOf} failed: {ex}");
            if (generation == _generation)
            {
                Reading = null;
                ErrorText = $"Could not build the timing reading: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _generation)
                IsBusy = false;
        }
    }
}
