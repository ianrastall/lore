using Lore.Models;
using Lore.ViewModels;
using Lore.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Lore;

// Settings - Lore: one per app, non-modal, changes apply at once.
public sealed partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    // False while the constructor is filling the settings boxes in.
    private readonly bool _ready;

    // True while code (not the user) is writing to the orb boxes, so that doing so is
    // not taken for six separate edits.
    private bool _showingOrbs;

    public SettingsWindow(MainWindow main)
    {
        _vm = main.ViewModel;
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark;
        App.Current.Accent.Attach(Root);
        AccentControl.Service = App.Current.Accent;

        // Show the saved calculation settings (index order matches the enums).
        HouseSystemCombo.SelectedIndex = (int)_vm.Settings.Houses;
        NodeTypeCombo.SelectedIndex = (int)_vm.Settings.Node;
        LilithTypeCombo.SelectedIndex = (int)_vm.Settings.Lilith;
        ShowOrbs(_vm.Settings.Orbs);
        ShowHome();
        _ready = true;

        // Standard 6.1: 720 × 720, minimum 560 × 480, centered on the main window's display.
        bool sized = false;
        Root.Loaded += (_, _) =>
        {
            if (sized) return;
            sized = true;
            double scale = Root.XamlRoot.RasterizationScale;
            var area = DisplayArea.GetFromWindowId(main.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int width = Math.Min((int)(720 * scale), area.Width);
            int height = Math.Min((int)(720 * scale), area.Height);
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)(560 * scale);
                presenter.PreferredMinimumHeight = (int)(480 * scale);
            }
        };
    }

    private async void Settings_Changed(object sender, SelectionChangedEventArgs e) => await ApplyAsync();

    // A named set was picked: put its numbers in the boxes, then apply once.
    private async void OrbPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _showingOrbs) return;
        int i = OrbPresetCombo.SelectedIndex;
        if (i < 0 || i >= OrbSettings.Presets.Count) return; // "Custom": the boxes stand as they are
        // A named set changes the major orbs; the minor-aspect switch stays as it is.
        var current = ReadOrbs();
        ShowOrbs(OrbSettings.Presets[i].Orbs with { MinorAspects = current.MinorAspects, Minor = current.Minor });
        await ApplyAsync();
    }

    private async void Minor_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _showingOrbs) return;
        OrbMinorBox.IsEnabled = MinorAspectsToggle.IsOn;
        await ApplyAsync();
    }

    // A number was edited by hand.
    private async void Orb_Changed(object? sender, EventArgs e)
    {
        if (!_ready || _showingOrbs) return;
        ShowOrbs(ReadOrbs()); // moves the drop-down to the matching set, or to "Custom"
        await ApplyAsync();
    }

    private void ShowOrbs(OrbSettings orbs)
    {
        _showingOrbs = true;
        OrbConjunctionBox.Value = orbs.Conjunction;
        OrbSextileBox.Value = orbs.Sextile;
        OrbSquareBox.Value = orbs.Square;
        OrbTrineBox.Value = orbs.Trine;
        OrbOppositionBox.Value = orbs.Opposition;
        OrbLuminaryBox.Value = orbs.LuminaryBonus;
        MinorAspectsToggle.IsOn = orbs.MinorAspects;
        OrbMinorBox.Value = orbs.Minor;
        OrbMinorBox.IsEnabled = orbs.MinorAspects;
        int preset = OrbSettings.Presets.ToList().FindIndex(p => p.Orbs.PresetName == orbs.PresetName);
        OrbPresetCombo.SelectedIndex = preset >= 0 ? preset : OrbSettings.Presets.Count; // last = "Custom"
        _showingOrbs = false;
    }

    // A box left with something unreadable in it keeps the value in force.
    private OrbSettings ReadOrbs()
    {
        var saved = _vm.Settings.Orbs;
        static double Or(double typed, double kept) => double.IsNaN(typed) ? kept : typed;
        return new OrbSettings(
            Or(OrbConjunctionBox.Value, saved.Conjunction), Or(OrbSextileBox.Value, saved.Sextile),
            Or(OrbSquareBox.Value, saved.Square), Or(OrbTrineBox.Value, saved.Trine),
            Or(OrbOppositionBox.Value, saved.Opposition), Or(OrbLuminaryBox.Value, saved.LuminaryBonus))
        {
            MinorAspects = MinorAspectsToggle.IsOn,
            Minor = Or(OrbMinorBox.Value, saved.Minor),
        }.Clamped();
    }

    private async Task ApplyAsync()
    {
        if (!_ready || HouseSystemCombo.SelectedIndex < 0 || NodeTypeCombo.SelectedIndex < 0 ||
            LilithTypeCombo.SelectedIndex < 0) return;
        await _vm.ApplySettingsAsync(new ChartSettings(
            (HouseSystem)HouseSystemCombo.SelectedIndex,
            (NodeType)NodeTypeCombo.SelectedIndex,
            (LilithType)LilithTypeCombo.SelectedIndex) { Orbs = ReadOrbs() });
    }

    // ── Where the reader is ───────────────────────────────────────────────────

    private void ShowHome()
    {
        HomeText.Text = _vm.Home is { } home
            ? $"{home.Name}: the Daily view gives sunrise, sunset and the planetary hours there."
            : "Not set.";
        HomeClearButton.Visibility = _vm.Home is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly SuggestionSearch _homeSearch = new();

    private async void HomeBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            await _homeSearch.RunAsync(sender, q => _vm.Cities.Search(q));
    }

    // A suggestion was picked, or Enter pressed: take the pick, else the best match.
    private void HomeBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var city = args.ChosenSuggestion as City ?? _vm.Cities.Search(args.QueryText).FirstOrDefault();
        if (city is null) return;

        _homeSearch.Cancel();
        _vm.Home = new HomePlace(city.Display, city.Latitude, city.Longitude);
        sender.Text = "";
        sender.ItemsSource = null;
        ShowHome();
    }

    private void HomeClear_Click(object sender, RoutedEventArgs e)
    {
        _vm.Home = null;
        ShowHome();
    }
}
