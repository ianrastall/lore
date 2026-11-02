using System.ComponentModel;
using Lore.Models;
using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

public sealed partial class WritersView : UserControl
{
    private WritersViewModel _vm = new();
    private City? _place;
    private bool _ready, _showDetail;
    public event Action<WriterBrief, bool>? ExportRequested;
    public event Action<NatalChart>? ChartRequested;

    public WritersViewModel ViewModel
    {
        get => _vm;
        set
        {
            _vm.PropertyChanged -= Model_Changed;
            _vm = value; _vm.PropertyChanged += Model_Changed;
            Bindings.Update();
        }
    }
    public WritersView()
    {
        InitializeComponent();
        StartPicker.MinDate = new DateTimeOffset(new DateTime(1200, 1, 1));
        StartPicker.MaxDate = new DateTimeOffset(new DateTime(2399, 12, 31));
        StartPicker.Date = new DateTimeOffset(new DateTime(1990, 6, 1));
        DistanceBox.Value = 100; FromYearBox.Value = 1200; ToYearBox.Value = DateTime.Today.AddYears(-50).Year;
        ShowLevels(); ShowHistoricalRule();
        _ready = true;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WritersViewModel.Brief)) return;
        PreviewBox.ItemsSource = ViewModel.BirthNames;
        PreviewBox.SelectedIndex = ViewModel.BirthNames.Count > 0 ? 0 : -1;
        if (ViewModel.HasBrief) { _showDetail = true; ApplyLayout(); }
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ViewModel.IsBusy) ViewModel.Cancel();
        int mode = ModeBox.SelectedIndex;
        CharacterInputs.Visibility = CharactersList.Visibility = When(mode == 0);
        GroupInputs.Visibility = GroupsList.Visibility = When(mode == 1);
        SavedInputs.Visibility = SavedList.Visibility = When(mode == 2);
        FindCharactersButton.Visibility = When(mode == 0); FindGroupsButton.Visibility = When(mode == 1);
        HistoricalNote.Visibility = When(mode == 1);
        SearchOptions.IsExpanded = mode != 2;
        if (mode == 2) ViewModel.Summary = $"{ViewModel.Saved.Count:N0} saved briefs. Choose one to reopen its original reading.";
        _showDetail = false; ViewModel.ClearSelection();
        ApplyLayout();
    }
    private void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ShowLevels();
    }
    private void ShowLevels()
    {
        ToneBox.ItemsSource = SizeBox.SelectedIndex == 0 ? new[] { "All levels", "Soulmates", "Harmonious", "Mixed", "Challenging", "Adversaries" }
            : new[] { "All tones", "Golden", "Harmonious", "Mixed", "Strained" };
        ToneBox.SelectedIndex = 0;
    }
    private void ShowHistoricalRule() => HistoricalNote.Text = $"Every member must be in the same field and must have died on or before {DateTime.Today.AddYears(-50):d MMMM yyyy} (at least fifty years ago).";
    private void Place_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _place = null; PlaceNote.Text = "Choose a city from the suggestions."; PlaceNote.Visibility = Visibility.Visible;
        sender.ItemsSource = ViewModel.Cities.Search(sender.Text);
    }
    private void Place_Chosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is City city) PickPlace(city);
    }
    private void Place_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        if (e.ChosenSuggestion is City city) PickPlace(city);
        else if (ViewModel.Cities.Search(e.QueryText).FirstOrDefault(c =>
            c.Name.Equals(e.QueryText, StringComparison.OrdinalIgnoreCase) || c.Display.Equals(e.QueryText, StringComparison.OrdinalIgnoreCase)) is { } exact) PickPlace(exact);
    }
    private void PickPlace(City city)
    {
        _place = city; PlaceBox.Text = city.Display;
        PlaceNote.Visibility = Visibility.Visible;
        PlaceNote.Text = $"{city.Latitude:0.####}, {city.Longitude:0.####} · {city.TimeZoneId}";
    }
    private async void FindCharacters_Click(object sender, RoutedEventArgs e)
    {
        if (_place is null || StartPicker.Date is not { } date) { ViewModel.Error = "Choose a birthplace and a starting date."; return; }
        _showDetail = false; SearchOptions.IsExpanded = false; ApplyLayout();
        int[] days = [7, 30, 90, 366];
        await ViewModel.FindCharactersAsync(new WriterCharacterQuery(new DateOnly(date.Year, date.Month, date.Day),
            days[Math.Clamp(SpanBox.SelectedIndex, 0, 3)], _place, (WriterAnchor)AnchorBox.SelectedIndex, (Planet)PlanetBox.SelectedIndex));
    }
    private async void FindGroups_Click(object sender, RoutedEventArgs e)
    {
        if (!double.IsFinite(DistanceBox.Value) || !double.IsFinite(FromYearBox.Value) || !double.IsFinite(ToYearBox.Value) ||
            FromYearBox.Value != Math.Truncate(FromYearBox.Value) || ToYearBox.Value != Math.Truncate(ToYearBox.Value))
        { ViewModel.Error = "Enter a valid distance and whole-number birth years."; return; }
        _showDetail = false; SearchOptions.IsExpanded = false; ApplyLayout();
        string category = CategoryBox.SelectedItem as string ?? "All categories";
        int[] prominence = [0, 10, 30, 100];
        ShowHistoricalRule();
        await ViewModel.FindGroupsAsync(new WriterGroupQuery(Size: SizeBox.SelectedIndex + 2, DistanceKm: DistanceBox.Value,
            Search: GroupSearchBox.Text, Category: category == "All categories" ? "" : category,
            FromYear: (int)FromYearBox.Value, ToYear: (int)ToYearBox.Value,
            MinimumSitelinks: prominence[Math.Clamp(ProminenceBox.SelectedIndex, 0, 3)],
            PairLevel: SizeBox.SelectedIndex == 0 && ToneBox.SelectedIndex > 0 ? 3 - ToneBox.SelectedIndex : null,
            TrioTone: SizeBox.SelectedIndex == 1 && ToneBox.SelectedIndex > 0 ? (TrioTone)(ToneBox.SelectedIndex - 1) : null,
            AsOf: DateOnly.FromDateTime(DateTime.Today)));
    }
    private async void Candidate_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WriterCandidate candidate) return;
        await ViewModel.SelectCandidateAsync(candidate);
    }
    private async void Group_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WriterGroup group) return;
        await ViewModel.SelectGroupAsync(group);
    }
    private void Saved_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WriterBrief brief) ViewModel.ShowBrief(brief);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel();
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ViewModel.ReloadSavedAsync();
    private async void Save_Click(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.BriefName)) { ViewModel.Error = "Give the brief a name before exporting it."; return; }
        if (ViewModel.Brief is { } brief) ExportRequested?.Invoke(brief with { Title = ViewModel.BriefName.Trim() }, (sender as Button)?.Tag as string == "pdf");
    }
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.PreviewAsync(Math.Max(0, PreviewBox.SelectedIndex)) is { } chart) ChartRequested?.Invoke(chart);
    }
    private void Back_Click(object sender, RoutedEventArgs e) { _showDetail = false; ApplyLayout(); }
    private void View_SizeChanged(object sender, SizeChangedEventArgs e) { if (_ready) ApplyLayout(); }
    private void ApplyLayout()
    {
        bool narrow = ActualWidth < 1000;
        SearchForm.Visibility = SearchStatus.Visibility = When(!narrow || !_showDetail);
        ResultsColumn.Width = new GridLength(narrow ? 0 : 320);
        Grid.SetColumnSpan(ResultsPane, narrow ? 2 : 1);
        Grid.SetColumn(DetailPane, narrow ? 0 : 1); Grid.SetColumnSpan(DetailPane, narrow ? 2 : 1);
        ResultsPane.Visibility = When(!narrow || !_showDetail);
        DetailPane.Visibility = When(!narrow || _showDetail);
        BackButton.Visibility = When(narrow);
    }
    public bool HasText(string text) => !string.IsNullOrWhiteSpace(text);
    public Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WhenEmpty(bool value) => When(!value);
}
