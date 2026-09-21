using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class AddChartDialog : ContentDialog
{
    private readonly CityService _cities;

    // IANA zone id of the chosen city; null until a city is picked (then the resolver
    // backfills from lat/lon). Latitude/longitude drive the geographic fallback.
    private string? _timeZoneId;

    public Celebrity? Result { get; private set; }

    public AddChartDialog(CityService cities)
    {
        _cities = cities;
        InitializeComponent();

        // DateTimeOffset(dateTime, offset) throws if dateTime.Kind == Local and the
        // offset doesn't match the machine's local offset. DateTime.Today is Local, so
        // force Unspecified (as the literal dates below already are) before pairing with
        // a zero offset. This crash was aborting the whole Add-Chart dialog.
        DateField.MinYear = new DateTimeOffset(new DateTime(1000, 1, 1), TimeSpan.Zero);
        DateField.MaxYear = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified), TimeSpan.Zero);
        DateField.SelectedDate = new DateTimeOffset(new DateTime(1990, 1, 1), TimeSpan.Zero);
        TimeField.SelectedTime = new TimeSpan(12, 0, 0);
        UtcBox.Value = 0;

        // Re-resolve the historical offset whenever the date or time changes.
        DateField.SelectedDateChanged += (_, _) => RefreshResolvedOffset();
        TimeField.SelectedTimeChanged += (_, _) => RefreshResolvedOffset();

        PrimaryButtonClick += OnCreate;
    }

    private void TimeUnknown_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TimeField.IsEnabled = TimeUnknownCheck.IsChecked != true;
    }

    private void CityBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = _cities.Search(sender.Text);
    }

    private void CityBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is City city)
        {
            sender.Text = city.Display;
            PlaceBox.Text = city.Display;
            LatBox.Value = city.Latitude;
            LonBox.Value = city.Longitude;
            UtcBox.Value = city.UtcOffsetHours; // fallback only
            _timeZoneId = string.IsNullOrWhiteSpace(city.TimeZoneId) ? null : city.TimeZoneId;
            RefreshResolvedOffset();
        }
    }

    // Show the offset that will actually be applied for the selected place + date, and
    // mirror it into the fallback box so it stays meaningful if no zone is found.
    private void RefreshResolvedOffset()
    {
        if (double.IsNaN(LatBox.Value) || double.IsNaN(LonBox.Value))
        {
            ResolvedOffsetText.Text = "Pick a birthplace to resolve its time zone.";
            return;
        }

        var date = DateField.SelectedDate?.DateTime ?? new DateTime(1990, 1, 1);
        var time = TimeField.SelectedTime ?? new TimeSpan(12, 0, 0);
        var (offset, label, zoneId) = BirthTimeResolver.Describe(
            _timeZoneId, LatBox.Value, LonBox.Value,
            date.Year, date.Month, date.Day, time.Hours, time.Minutes);

        ResolvedOffsetText.Text = zoneId is null ? label : $"{label} · {zoneId}";
        if (zoneId is not null)
        {
            _timeZoneId = zoneId;   // record the geographically-resolved zone too
            UtcBox.Value = offset;  // keep the fallback in sync with what will be used
        }
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

        var date = DateField.SelectedDate!.Value;
        bool timeKnown = TimeUnknownCheck.IsChecked != true;
        var time = TimeField.SelectedTime ?? new TimeSpan(12, 0, 0);

        Result = new Celebrity
        {
            Id = "user-" + Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            Category = UserChartService.MyChartsCategory,
            BirthDate = date.ToString("yyyy-MM-dd"),
            BirthTime = timeKnown ? $"{time.Hours:D2}:{time.Minutes:D2}" : null,
            BirthTimeKnown = timeKnown,
            BirthPlace = string.IsNullOrWhiteSpace(PlaceBox.Text) ? "Unknown" : PlaceBox.Text.Trim(),
            Latitude = LatBox.Value,
            Longitude = LonBox.Value,
            UtcOffsetHours = UtcBox.Value,
            TimeZoneId = _timeZoneId,
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
