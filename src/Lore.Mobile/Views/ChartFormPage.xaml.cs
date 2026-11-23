using System.Globalization;
using Lore.Models;
using Lore.Services;

namespace Lore.Mobile.Views;

[QueryProperty(nameof(Existing), "existing")]
public partial class ChartFormPage : ContentPage
{
    // The chart being changed; null for a new one.
    private Celebrity? _existing;

    // The IANA zone of the chosen city; null until one is chosen, when the resolver
    // finds it from the coordinates.
    private string? _timeZoneId;

    // Set while the form fills the coordinate boxes itself, so that is not taken as typing.
    private bool _filling;

    public ChartFormPage()
    {
        InitializeComponent();
        DateBox.MinimumDate = new DateTime(1200, 1, 1);
        DateBox.MaximumDate = new DateTime(2399, 12, 31);
        DateBox.Date = DateTime.Today;
        TimeBox.Time = new TimeSpan(12, 0, 0);
        _ = App.Main.EnsureHospitalsLoadedAsync();
    }

    public string Existing
    {
        set
        {
            if (value != "true" || App.Main.SelectedCelebrity is not { } c ||
                c.Category != UserChartService.MyChartsCategory) return;
            _existing = c;
            Load(c);
        }
    }

    private void Load(Celebrity c)
    {
        Title = "Edit chart";
        NameBox.Text = c.Name;
        var (year, month, day) = c.GetRecordedDate();
        DateBox.Date = new DateTime(year, month, day);
        JulianNote.IsVisible = c.JulianCalendar;
        TimeUnknown.IsChecked = !c.BirthTimeKnown;
        TimeBox.Time = c.GetBirthTime().ToTimeSpan();
        TimeBox.IsEnabled = c.BirthTimeKnown;
        PlaceBox.Text = c.BirthPlace;
        _timeZoneId = string.IsNullOrWhiteSpace(c.TimeZoneId) ? null : c.TimeZoneId;
        SetCoordinates(c.Latitude, c.Longitude);
        NoteBox.Text = c.Bio;
        RefreshOffset();
    }

    private void SetCoordinates(double lat, double lon)
    {
        _filling = true;
        LatBox.Text = lat.ToString("0.####", CultureInfo.InvariantCulture);
        LonBox.Text = lon.ToString("0.####", CultureInfo.InvariantCulture);
        _filling = false;
    }

    private static double? Number(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.').Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v : null;

    // ── Place ────────────────────────────────────────────────────────────────

    private void OnCityTyped(object? sender, TextChangedEventArgs e)
    {
        CityResults.Children.Clear();
        foreach (var city in App.Main.Cities.Search(e.NewTextValue, 6))
            CityResults.Children.Add(Choice(city.Display, () =>
            {
                PlaceBox.Text = city.Display;
                _timeZoneId = string.IsNullOrWhiteSpace(city.TimeZoneId) ? null : city.TimeZoneId;
                SetCoordinates(city.Latitude, city.Longitude);
                CitySearch.Text = "";
                CityResults.Children.Clear();
                CitySearch.Unfocus();
                RefreshOffset();
            }));
    }

    private async void OnHospitalTyped(object? sender, TextChangedEventArgs e)
    {
        string? query = e.NewTextValue;
        await App.Main.EnsureHospitalsLoadedAsync();
        if (HospitalSearch.Text != query) return; // typed on while the list loaded
        HospitalResults.Children.Clear();
        foreach (var hospital in App.Main.Hospitals.Search(query, 6))
            HospitalResults.Children.Add(Choice(hospital.Display, () =>
            {
                PlaceBox.Text = string.IsNullOrWhiteSpace(hospital.City) ? hospital.Display : $"{hospital.Name}, {hospital.City}";
                _timeZoneId = null; // found again from the hospital's own coordinates
                SetCoordinates(hospital.Latitude, hospital.Longitude);
                HospitalSearch.Text = "";
                HospitalResults.Children.Clear();
                HospitalSearch.Unfocus();
                RefreshOffset();
            }));
    }

    private static View Choice(string text, Action chosen)
    {
        var label = new Label { Text = text, Padding = new Thickness(8, 10) };
        label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(chosen) });
        return label;
    }

    private void OnCoordinatesTyped(object? sender, TextChangedEventArgs e)
    {
        if (_filling) return;
        _timeZoneId = null; // the old place's zone may not be the new one's
        RefreshOffset();
    }

    // ── Time and time zone ───────────────────────────────────────────────────

    private void OnWhenChanged(object? sender, DateChangedEventArgs e) => RefreshOffset();

    private void OnTimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimePicker.Time)) RefreshOffset();
    }

    private void OnTimeUnknownChanged(object? sender, CheckedChangedEventArgs e)
    {
        TimeBox.IsEnabled = !e.Value;
        RefreshOffset();
    }

    private (int Year, int Month, int Day) RecordedDate()
    {
        var d = DateBox.Date ?? DateTime.Today;
        return (d.Year, d.Month, d.Day);
    }

    private bool IsJulian => _existing?.JulianCalendar == true;

    // The offset the time-zone database gives for the place and moment: shown, and saved.
    private double? RefreshOffset()
    {
        if (Number(LatBox.Text) is not { } lat || Number(LonBox.Text) is not { } lon)
        {
            OffsetText.Text = "Pick a birthplace to resolve its time zone.";
            return null;
        }
        if (_existing is { UtcOffsetFixed: true } fixedOffset)
        {
            OffsetText.Text = $"UTC{fixedOffset.UtcOffsetHours:+0.##;-0.##;+0} (set by hand on Windows, and kept)";
            return fixedOffset.UtcOffsetHours;
        }
        try
        {
            var (y, m, d) = RecordedDate();
            var date = BirthTimeResolver.GregorianDate(y, m, d, IsJulian);
            var time = TimeUnknown.IsChecked ? new TimeSpan(12, 0, 0) : TimeBox.Time ?? new TimeSpan(12, 0, 0);
            var (offset, label, zoneId) = BirthTimeResolver.Describe(_timeZoneId, lat, lon,
                date.Year, date.Month, date.Day, time.Hours, time.Minutes);
            OffsetText.Text = zoneId is null ? label : $"{label} · {zoneId}";
            if (zoneId is not null) _timeZoneId = zoneId;
            return offset;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            OffsetText.Text = "This date does not exist in that calendar.";
            return null;
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return "Please enter a name.";
        if (Number(LatBox.Text) is not { } lat || lat < -90 || lat > 90)
            return "Latitude must be between -90 and 90 (pick a city or type it in).";
        if (Number(LonBox.Text) is not { } lon || lon < -180 || lon > 180)
            return "Longitude must be between -180 and 180 (pick a city or type it in).";
        return null;
    }

    private bool _saving;

    private async void OnSave(object? sender, EventArgs e)
    {
        if (_saving) return;
        string? error = Validate();
        double? offset = error is null ? RefreshOffset() : null;
        if (error is null && offset is null) error = "The time zone could not be worked out for this place and date.";
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        var (year, month, day) = RecordedDate();
        bool timeKnown = !TimeUnknown.IsChecked;
        var time = TimeBox.Time ?? new TimeSpan(12, 0, 0);
        var chart = new Celebrity
        {
            Id = _existing?.Id ?? "user-" + Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            Category = UserChartService.MyChartsCategory,
            BirthDate = $"{year:D4}-{month:D2}-{day:D2}",
            JulianCalendar = IsJulian,
            DeathDate = _existing?.DeathDate,
            BirthTime = timeKnown ? $"{time.Hours:D2}:{time.Minutes:D2}" : null,
            BirthTimeKnown = timeKnown,
            BirthTimeUncertaintyMinutes = timeKnown ? _existing?.BirthTimeUncertaintyMinutes ?? 0 : 0,
            BirthPlace = string.IsNullOrWhiteSpace(PlaceBox.Text) ? "Unknown" : PlaceBox.Text.Trim(),
            Latitude = Number(LatBox.Text)!.Value,
            Longitude = Number(LonBox.Text)!.Value,
            UtcOffsetHours = offset!.Value,
            UtcOffsetFixed = _existing?.UtcOffsetFixed == true,
            TimeZoneId = _timeZoneId,
            RoddenRating = _existing?.RoddenRating,
            Source = _existing?.Source,
            Bio = NoteBox.Text?.Trim() ?? "",
        };

        // Saving rebuilds and scores the whole list, which takes some seconds on a phone.
        _saving = true;
        ErrorText.IsVisible = false;
        SavingRow.IsVisible = true;
        SaveButton.IsEnabled = SaveItem.IsEnabled = false;
        try
        {
            if (_existing is null) await App.Main.AddCustomChartAsync(chart);
            else await App.Main.UpdateCustomChartAsync(chart);
        }
        finally
        {
            _saving = false;
            SavingRow.IsVisible = false;
            SaveButton.IsEnabled = SaveItem.IsEnabled = true;
        }
        await Shell.Current.GoToAsync("..");
        await AppShell.ShowChartAsync();
    }
}
