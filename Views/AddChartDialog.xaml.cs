using Lore.Models;
using Lore.Services;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class AddChartDialog : ContentDialog
{
    private readonly CityService _cities;

    public Celebrity? Result { get; private set; }

    public AddChartDialog(CityService cities)
    {
        _cities = cities;
        InitializeComponent();

        DateField.MinYear = new DateTimeOffset(new DateTime(1000, 1, 1), TimeSpan.Zero);
        DateField.MaxYear = new DateTimeOffset(DateTime.Today, TimeSpan.Zero);
        DateField.SelectedDate = new DateTimeOffset(new DateTime(1990, 1, 1), TimeSpan.Zero);
        TimeField.SelectedTime = new TimeSpan(12, 0, 0);
        UtcBox.Value = 0;

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
            UtcBox.Value = city.UtcOffsetHours;
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
