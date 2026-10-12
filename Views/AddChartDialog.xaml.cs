using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class AddChartDialog : ContentDialog
{
    private readonly CityService _cities;
    private readonly HospitalService _hospitals;

    // Completes once the hospital list has been read (it is loaded when the dialog is
    // first opened, so the first search may have to wait a moment for it).
    private readonly Task _hospitalsReady;

    // IANA zone id of the chosen city; null until a city is picked (then the resolver
    // backfills from lat/lon). Latitude/longitude drive the geographic fallback.
    private string? _timeZoneId;

    public Celebrity? Result { get; private set; }

    // Set when editing an existing chart: its Id is kept so the save replaces it.
    private readonly Celebrity? _existing;

    public AddChartDialog(CityService cities, HospitalService hospitals, Task hospitalsReady, Celebrity? existing = null)
    {
        _cities = cities;
        _hospitals = hospitals;
        _hospitalsReady = hospitalsReady;
        _existing = existing;
        InitializeComponent();

        // DateTimeOffset(dateTime, offset) throws if dateTime.Kind == Local and the
        // offset doesn't match the machine's local offset. DateTime.Today is Local, so
        // force Unspecified (as the literal dates below already are) before pairing with
        // a zero offset. This crash was aborting the whole Add-Chart dialog.
        // The bundled ephemeris files begin in 1200; earlier dates would be calculated
        // from a rougher built-in model, and without Chiron.
        DateField.MinYear = new DateTimeOffset(new DateTime(1200, 1, 1), TimeSpan.Zero);
        DateField.MaxYear = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified), TimeSpan.Zero);
        DateField.SelectedDate = PickerDate(new DateOnly(1990, 1, 1));
        TimeField.SelectedTime = new TimeSpan(12, 0, 0);
        UtcBox.Value = 0;

        // Re-resolve the historical offset whenever the date or time changes.
        DateField.SelectedDateChanged += (_, _) => { RefreshCalendar(); RefreshResolvedOffset(); };
        TimeField.SelectedTimeChanged += (_, _) => RefreshResolvedOffset();

        // First entry is "not rated"; the rest are Rodden's codes, most reliable first.
        RatingCombo.Items.Add("Not rated");
        foreach (var (code, _) in RoddenRating.All)
            RatingCombo.Items.Add(RoddenRating.Describe(code));
        RatingCombo.SelectedIndex = 0;

        PrimaryButtonClick += OnCreate;

        if (existing is not null)
            LoadExisting(existing);
    }

    // Fill the form from a saved chart. Coordinates go in before the date so the
    // date-changed handler can already resolve the time zone.
    private void LoadExisting(Celebrity c)
    {
        Title = "Edit chart";
        PrimaryButtonText = "Save";

        NameBox.Text = c.Name;
        PlaceBox.Text = c.BirthPlace;
        SetCoordinates(c.Latitude, c.Longitude);
        UtcBox.Value = c.UtcOffsetHours;
        OffsetFixedCheck.IsChecked = c.UtcOffsetFixed;
        OffsetExpander.IsExpanded = c.UtcOffsetFixed;
        _timeZoneId = string.IsNullOrWhiteSpace(c.TimeZoneId) ? null : c.TimeZoneId;
        NoteBox.Text = c.Bio;
        SourceBox.Text = c.Source ?? "";
        int rating = RoddenRating.All.ToList().FindIndex(r => r.Code == c.RoddenRating);
        RatingCombo.SelectedIndex = rating + 1; // -1 (none, or not one of Rodden's) -> "Not rated"

        // An Old Style 29 February of a year the picker's calendar gives none is shown
        // as the 28th with the box under it ticked.
        var (year, month, day) = c.GetRecordedDate();
        bool julianOnly = c.JulianCalendar && Celebrity.IsJulianOnly(year, month, day);
        DateField.SelectedDate = PickerDate(new DateOnly(year, month, julianOnly ? 28 : day));
        var t = c.GetBirthTime();
        TimeField.SelectedTime = new TimeSpan(t.Hour, t.Minute, 0);
        JulianCheck.IsChecked = c.JulianCalendar;
        RefreshCalendar();
        JulianLeapCheck.IsChecked = julianOnly;
        RefreshCalendar();
        TimeUnknownCheck.IsChecked = !c.BirthTimeKnown;
        TimeField.IsEnabled = c.BirthTimeKnown;
        UncertaintyBox.Value = c.BirthTimeUncertaintyMinutes;
        UncertaintyBox.IsEnabled = c.BirthTimeKnown;

        RefreshResolvedOffset();
    }

    // The DatePicker shows its value in the machine's local time zone, so a calendar
    // date must be given as local noon: midnight UTC would display (and save) as the
    // previous day anywhere west of Greenwich.
    private static DateTimeOffset PickerDate(DateOnly d) =>
        new(new DateTime(d.Year, d.Month, d.Day, 12, 0, 0, DateTimeKind.Unspecified));

    private void TimeUnknown_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TimeField.IsEnabled = TimeUnknownCheck.IsChecked != true;
        UncertaintyBox.IsEnabled = TimeUnknownCheck.IsChecked != true;
    }

    // The last country to leave the Julian calendar (Greece) did so in 1923; after that
    // there is nothing to ask.
    private const int LastJulianYear = 1923;

    private bool IsJulian => JulianCheck.IsChecked == true &&
                             (DateField.SelectedDate?.Year ?? int.MaxValue) <= LastJulianYear;

    private void Julian_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        RefreshCalendar();
        RefreshResolvedOffset();
    }

    // The picker shows the Gregorian calendar, which has no 29 February in 1700, 1800 or
    // 1900 (nor 1300, 1400, 1500); the Julian calendar had. With Old Style ticked and
    // 28 February of such a year picked, a second box says it was the day after.
    private bool CanBeJulianLeapDay =>
        IsJulian && DateField.SelectedDate is { } picked &&
        picked.Month == 2 && picked.Day == 28 && Celebrity.DaysInMonth(picked.Year, 2, julian: true) == 29 &&
        !DateTime.IsLeapYear(picked.Year);

    // The date as it is to be recorded: what the picker shows, or the 29 February the
    // picker cannot show.
    private (int Year, int Month, int Day) RecordedDate()
    {
        var picked = DateField.SelectedDate?.DateTime ?? new DateTime(1990, 1, 1);
        bool leapDay = CanBeJulianLeapDay && JulianLeapCheck.IsChecked == true;
        return (picked.Year, picked.Month, leapDay ? 29 : picked.Day);
    }

    // Offer the Old Style tick-box for early dates, and when it is ticked show the
    // Gregorian date the chart will actually be calculated for.
    private void RefreshCalendar()
    {
        bool early = (DateField.SelectedDate?.Year ?? int.MaxValue) <= LastJulianYear;
        JulianCheck.Visibility = early ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        bool leapDay = CanBeJulianLeapDay;
        JulianLeapCheck.Visibility = leapDay ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (!leapDay && JulianLeapCheck.IsChecked == true) JulianLeapCheck.IsChecked = false;

        if (IsJulian && DateField.SelectedDate is not null)
        {
            var (year, month, day) = RecordedDate();
            var gregorian = BirthTimeResolver.GregorianDate(year, month, day, julian: true);
            JulianText.Text = $"Calculated for {gregorian:d MMMM yyyy} in today's (Gregorian) calendar.";
            JulianText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            JulianText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    // Ticked: the offset box is the user's own and is used as given. Unticked: it goes
    // back to showing (and following) what the time-zone database says.
    private void OffsetFixed_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => RefreshResolvedOffset();

    // True while code, not the user, is filling in the coordinates.
    private bool _settingCoordinates;

    private void SetCoordinates(double latitude, double longitude)
    {
        _settingCoordinates = true;
        LatBox.Value = latitude;
        LonBox.Value = longitude;
        _settingCoordinates = false;
    }

    // The user typed a latitude or longitude. Whatever time zone came with the city or
    // the saved chart no longer applies — the place has moved — so drop it and look the
    // zone up afresh from the new coordinates.
    private void Place_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_settingCoordinates) return;
        _timeZoneId = null;
        RefreshResolvedOffset();
    }

    // Searching a quarter of a million hospitals takes long enough to feel in the typing,
    // so both searches wait for a short pause and then run off the UI thread. A newer
    // keystroke cancels the search before it.
    private readonly SuggestionSearch _search = new();

    private Task SuggestAsync<T>(AutoSuggestBox box, Func<string, IReadOnlyList<T>> search) =>
        _search.RunAsync(box, search);

    private async void CityBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        await SuggestAsync(sender, q => _cities.Search(q));
    }

    private void CityBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is City city)
        {
            sender.Text = city.Display;
            PlaceBox.Text = city.Display;
            SetCoordinates(city.Latitude, city.Longitude);
            UtcBox.Value = city.UtcOffsetHours; // fallback only
            _timeZoneId = string.IsNullOrWhiteSpace(city.TimeZoneId) ? null : city.TimeZoneId;
            RefreshResolvedOffset();
        }
    }

    private async void HospitalBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        await SuggestAsync(sender, q =>
        {
            _hospitalsReady.Wait(); // already off the UI thread here
            return _hospitals.Search(q);
        });
    }

    private void HospitalBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Hospital hospital)
        {
            sender.Text = hospital.Display;
            PlaceBox.Text = hospital.Display;
            SetCoordinates(hospital.Latitude, hospital.Longitude);
            // Hospitals carry no time-zone data, so clear any zone from a prior city
            // pick and let RefreshResolvedOffset resolve it from these coordinates.
            _timeZoneId = null;
            RefreshResolvedOffset();
        }
    }

    // Show the offset the time-zone database gives for the selected place + date, and
    // mirror it into the offset box unless the user has taken that box over.
    private void RefreshResolvedOffset()
    {
        bool own = OffsetFixedCheck.IsChecked == true;
        UtcBox.IsEnabled = own;

        if (double.IsNaN(LatBox.Value) || double.IsNaN(LonBox.Value))
        {
            ResolvedOffsetText.Text = "Pick a birthplace to resolve its time zone.";
            return;
        }

        var (recordedYear, recordedMonth, recordedDay) = RecordedDate();
        var date = BirthTimeResolver.GregorianDate(recordedYear, recordedMonth, recordedDay, IsJulian);
        var time = TimeField.SelectedTime ?? new TimeSpan(12, 0, 0);
        var (offset, label, zoneId) = BirthTimeResolver.Describe(
            _timeZoneId, LatBox.Value, LonBox.Value,
            date.Year, date.Month, date.Day, time.Hours, time.Minutes);

        ResolvedOffsetText.Text = zoneId is null ? label : $"{label} · {zoneId}";
        if (own)
            ResolvedOffsetText.Text += " — not used: the offset below is.";
        if (zoneId is null)
        {
            UtcBox.IsEnabled = true; // nothing to follow, so the box is all there is
            OffsetExpander.IsExpanded = true; // ... and it needs to be seen
            return;
        }
        _timeZoneId = zoneId;        // record the geographically-resolved zone too
        if (!own) UtcBox.Value = offset; // show what will be used
    }

    private void OnCreate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? error = Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            args.Cancel = true; // keep the dialog open
            return;
        }

        var (year, month, day) = RecordedDate();
        bool timeKnown = TimeUnknownCheck.IsChecked != true;
        var time = TimeField.SelectedTime ?? new TimeSpan(12, 0, 0);

        Result = new Celebrity
        {
            Id = _existing?.Id ?? "user-" + Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            Category = UserChartService.MyChartsCategory,
            // Built from the numbers, not formatted: a format would follow the Windows
            // regional calendar and could store a Buddhist or Persian year.
            BirthDate = $"{year:D4}-{month:D2}-{day:D2}",
            JulianCalendar = IsJulian,
            BirthTime = timeKnown ? $"{time.Hours:D2}:{time.Minutes:D2}" : null,
            BirthTimeKnown = timeKnown,
            BirthTimeUncertaintyMinutes = timeKnown && !double.IsNaN(UncertaintyBox.Value) ? (int)UncertaintyBox.Value : 0,
            BirthPlace = string.IsNullOrWhiteSpace(PlaceBox.Text) ? "Unknown" : PlaceBox.Text.Trim(),
            Latitude = LatBox.Value,
            Longitude = LonBox.Value,
            UtcOffsetHours = UtcBox.Value,
            UtcOffsetFixed = OffsetFixedCheck.IsChecked == true,
            TimeZoneId = _timeZoneId,
            RoddenRating = RatingCombo.SelectedIndex > 0 ? RoddenRating.All[RatingCombo.SelectedIndex - 1].Code : null,
            Source = string.IsNullOrWhiteSpace(SourceBox.Text) ? null : SourceBox.Text.Trim(),
            Bio = NoteBox.Text.Trim()
        };
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return "Please enter a name.";
        if (DateField.SelectedDate is null)
            return "Please choose a birth date.";
        if (double.IsNaN(LatBox.Value) || LatBox.Value < -90 || LatBox.Value > 90)
            return "Latitude must be between -90 and 90 (pick a city or type it in).";
        if (double.IsNaN(LonBox.Value) || LonBox.Value < -180 || LonBox.Value > 180)
            return "Longitude must be between -180 and 180 (pick a city or type it in).";
        if (double.IsNaN(UtcBox.Value))
            return "Please set the UTC offset.";
        return null;
    }
}
