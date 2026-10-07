using Lore.Services;
using Lore.ViewModels;
using Lore.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lore;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // Completes when the one-time data load below has finished (the splash waits on it).
    private readonly TaskCompletionSource _initialized = new();
    public Task Initialized => _initialized.Task;

    public MainWindow(MainViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();

        // Show the saved calculation settings (index order matches the enums).
        HouseSystemCombo.SelectedIndex = (int)vm.Settings.Houses;
        NodeTypeCombo.SelectedIndex = (int)vm.Settings.Node;
        LilithTypeCombo.SelectedIndex = (int)vm.Settings.Lilith;
        ShowOrbs(vm.Settings.Orbs);
        ShowHome();
        _settingsReady = true;

        AppWindow.Title = "Lore — Natal Charts";
        // The window (and so the taskbar) names whoever's chart is open.
        vm.ChartVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartViewModel.Chart))
                AppWindow.Title = vm.ChartVM.Chart is { } c ? $"{c.Celebrity.Name} — Lore" : "Lore — Natal Charts";
        };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();

#if DEBUG
        // For checking the "chart with tables" image without clicking through a save
        // dialog: with LORE_SHEET_OUT set to a file path, a debug build writes the image
        // for the first chart it opens there.
        if (Environment.GetEnvironmentVariable("LORE_SHEET_OUT") is { Length: > 0 } sheetOut)
        {
            bool written = false;
            vm.ChartVM.PropertyChanged += async (_, e) =>
            {
                if (written || e.PropertyName != nameof(ChartViewModel.Chart) || vm.ChartVM.Chart is not { } first) return;
                written = true;
                try { await File.WriteAllBytesAsync(sheetOut, await ExportService.RenderChartSheetPngAsync(first)); }
                catch (Exception ex) { Diagnostics.Log($"LORE_SHEET_OUT failed: {ex}"); }
            };
        }
#endif

        // One-time data load. Window.Activated proved unreliable here (it never fired,
        // so the app started with empty lists), so drive it from the root element's
        // Loaded event, with Activated kept as a fallback. The guard runs it once.
        bool initialized = false;
        async void InitOnce(string via)
        {
            if (initialized) return;
            initialized = true;
            Services.Diagnostics.Log($"InitOnce via {via}");
            string dataPath      = Path.Combine(AppContext.BaseDirectory, "Data", "celebrities.json");
            string citiesPath    = Path.Combine(AppContext.BaseDirectory, "Data", "cities.json");
            string hospitalsPath = Path.Combine(AppContext.BaseDirectory, "Data", "hospitals.json");
            try
            {
                await ViewModel.InitializeAsync(dataPath, citiesPath, hospitalsPath);
                ShowView(ViewModel.LastView); // back to the view the last session ended in
            }
            finally
            {
                _initialized.TrySetResult();
            }
        }

        if (Content is FrameworkElement root)
            root.Loaded += (_, _) => InitOnce("Loaded");
        Activated += (_, _) => InitOnce("Activated");
    }

    // Only one dialog can be open at a time; a second Ctrl+N while one is up does nothing.
    private bool _dialogOpen;

    private async void AddChart_Click(object sender, RoutedEventArgs e) => await AddChartAsync();

    private async Task AddChartAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals, ViewModel.EnsureHospitalsLoadedAsync()) { XamlRoot = Content.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && dialog.Result is { } chart)
                await ViewModel.AddCustomChartAsync(chart);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    private async void AddShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await AddChartAsync();
    }

    private void SearchShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
    }

    private void LegendShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.ShowLegend = !ViewModel.ShowLegend;
    }

    // Ctrl+1 … Ctrl+7, in the order the buttons run across the toolbar.
    private static readonly string[] Views = ["Chart", "Report", "Worksheet", "Daily", "Forecast", "Timing", "Synastry"];

    private void ViewShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        int index = sender.Key - Windows.System.VirtualKey.Number1;
        if (index >= 0 && index < Views.Length) ShowView(Views[index]);
    }

    private void ShowView(string view) => ShowBody(
        report: view == "Report", worksheet: view == "Worksheet", daily: view == "Daily",
        forecast: view == "Forecast", timing: view == "Timing", synastry: view == "Synastry");

    private async void EditChart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCelebrity is not { } existing || !ViewModel.SelectedIsCustom || _dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals, ViewModel.EnsureHospitalsLoadedAsync(), existing) { XamlRoot = Content.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && dialog.Result is { } chart)
                await ViewModel.UpdateCustomChartAsync(chart);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // Deleting a chart can't be undone from inside the app, so ask first.
    private async void DeleteChart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCelebrity is not { } chart || !ViewModel.SelectedIsCustom) return;
        var confirm = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = $"Delete {chart.Name}?",
            Content = "This removes the chart from My Charts. It can't be undone from inside Lore.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
            XamlRoot = Content.XamlRoot,
        };
        if (await confirm.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
            await ViewModel.DeleteSelectedCommand.ExecuteAsync(null);
    }

    private void ShowChart_Click(object sender, RoutedEventArgs e) => ShowBody();
    private void ShowReport_Click(object sender, RoutedEventArgs e) => ShowBody(report: true);
    private void ShowWorksheet_Click(object sender, RoutedEventArgs e) => ShowBody(worksheet: true);
    private void ShowDaily_Click(object sender, RoutedEventArgs e) => ShowBody(daily: true);
    private void ShowForecast_Click(object sender, RoutedEventArgs e) => ShowBody(forecast: true);
    private void ShowTiming_Click(object sender, RoutedEventArgs e) => ShowBody(timing: true);
    private void ShowSynastry_Click(object sender, RoutedEventArgs e) => ShowBody(synastry: true);

    private void ShowBody(bool report = false, bool worksheet = false, bool daily = false, bool forecast = false, bool timing = false, bool synastry = false)
    {
        ViewModel.ShowTiming = timing;
        TimingToggle.IsChecked = timing;
        ViewModel.ShowForecast = forecast;
        ForecastToggle.IsChecked = forecast;
        ViewModel.ShowLegend = false;
        ViewModel.ShowReport = report;
        ViewModel.ShowWorksheet = worksheet;
        ViewModel.ShowDaily = daily;
        ViewModel.ShowSynastry = synastry;
        ChartToggle.IsChecked = !report && !worksheet && !daily && !forecast && !timing && !synastry;
        ReportToggle.IsChecked = report;
        WorksheetToggle.IsChecked = worksheet;
        DailyToggle.IsChecked = daily;
        SynastryToggle.IsChecked = synastry;

        ViewModel.RememberView(
            report ? "Report" : worksheet ? "Worksheet" : daily ? "Daily" :
            forecast ? "Forecast" : timing ? "Timing" : synastry ? "Synastry" : "Chart");
    }

    // False while the constructor is filling the settings boxes in.
    private readonly bool _settingsReady;

    // True while code (not the user) is writing to the orb boxes, so that doing so is
    // not taken for six separate edits.
    private bool _showingOrbs;

    private async void Settings_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e) =>
        await ApplySettingsFromMenuAsync();

    // A named set was picked: put its numbers in the boxes, then apply once.
    private async void OrbPreset_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (!_settingsReady || _showingOrbs) return;
        int i = OrbPresetCombo.SelectedIndex;
        if (i < 0 || i >= Models.OrbSettings.Presets.Count) return; // "Custom": the boxes stand as they are
        // A named set changes the major orbs; the minor-aspect switch stays as it is.
        var current = ReadOrbs();
        ShowOrbs(Models.OrbSettings.Presets[i].Orbs with { MinorAspects = current.MinorAspects, Minor = current.Minor });
        await ApplySettingsFromMenuAsync();
    }

    private async void Minor_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady || _showingOrbs) return;
        OrbMinorBox.IsEnabled = MinorAspectsCheck.IsChecked == true;
        await ApplySettingsFromMenuAsync();
    }

    // A number was edited by hand.
    private async void Orb_Changed(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs e)
    {
        if (!_settingsReady || _showingOrbs) return;
        if (double.IsNaN(sender.Value)) return; // box cleared mid-edit: wait for a number
        var orbs = ReadOrbs();
        ShowOrbs(orbs); // moves the drop-down to the matching set, or to "Custom"
        await ApplySettingsFromMenuAsync();
    }

    private void ShowOrbs(Models.OrbSettings orbs)
    {
        _showingOrbs = true;
        OrbConjunctionBox.Value = orbs.Conjunction;
        OrbSextileBox.Value = orbs.Sextile;
        OrbSquareBox.Value = orbs.Square;
        OrbTrineBox.Value = orbs.Trine;
        OrbOppositionBox.Value = orbs.Opposition;
        OrbLuminaryBox.Value = orbs.LuminaryBonus;
        MinorAspectsCheck.IsChecked = orbs.MinorAspects;
        OrbMinorBox.Value = orbs.Minor;
        OrbMinorBox.IsEnabled = orbs.MinorAspects;
        int preset = Models.OrbSettings.Presets.ToList().FindIndex(p => p.Orbs.PresetName == orbs.PresetName);
        OrbPresetCombo.SelectedIndex = preset >= 0 ? preset : Models.OrbSettings.Presets.Count; // last = "Custom"
        _showingOrbs = false;
    }

    private Models.OrbSettings ReadOrbs() => new Models.OrbSettings(
        OrbConjunctionBox.Value, OrbSextileBox.Value, OrbSquareBox.Value, OrbTrineBox.Value,
        OrbOppositionBox.Value, OrbLuminaryBox.Value)
    {
        MinorAspects = MinorAspectsCheck.IsChecked == true,
        Minor = OrbMinorBox.Value,
    }.Clamped();

    private async Task ApplySettingsFromMenuAsync()
    {
        if (!_settingsReady || HouseSystemCombo.SelectedIndex < 0 || NodeTypeCombo.SelectedIndex < 0 ||
            LilithTypeCombo.SelectedIndex < 0) return;
        await ViewModel.ApplySettingsAsync(new Models.ChartSettings(
            (Models.HouseSystem)HouseSystemCombo.SelectedIndex,
            (Models.NodeType)NodeTypeCombo.SelectedIndex,
            (Models.LilithType)LilithTypeCombo.SelectedIndex) { Orbs = ReadOrbs() });
    }

    // ── Where the reader is ───────────────────────────────────────────────────

    private void ShowHome()
    {
        HomeText.Text = ViewModel.Home is { } home
            ? $"{home.Name}: the Daily view gives sunrise, sunset and the planetary hours there."
            : "Not set. Choose a city and the Daily view gives sunrise, sunset and the planetary hours there.";
        HomeClearButton.Visibility = ViewModel.Home is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HomeBox_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.Cities.Search(sender.Text);
    }

    // A suggestion was picked, or Enter pressed: take the pick, else the best match.
    private void HomeBox_QuerySubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var city = args.ChosenSuggestion as Models.City ?? ViewModel.Cities.Search(args.QueryText).FirstOrDefault();
        if (city is null) return;

        ViewModel.Home = new Models.HomePlace(city.Display, city.Latitude, city.Longitude);
        sender.Text = "";
        sender.ItemsSource = null;
        ShowHome();
    }

    private void HomeClear_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Home = null;
        ShowHome();
    }

    private void ShowLegend_Click(object sender, RoutedEventArgs e) => ViewModel.ShowLegend = true;
    private void Legend_CloseRequested(object sender, EventArgs e) => ViewModel.ShowLegend = false;

    // ── Export ────────────────────────────────────────────────────────────────
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("pdf");
    private async void ExportPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("png");
    private async void ExportSheetPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("sheetpng");
    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ExportAsync("json");
    private async void ExportXml_Click(object sender, RoutedEventArgs e) => await ExportAsync("xml");
    private async void ExportWorksheet_Click(object sender, RoutedEventArgs e) => await ExportAsync("worksheettxt");
    private async void ExportDailyPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("dailypdf");
    private async void ExportDailyText_Click(object sender, RoutedEventArgs e) => await ExportAsync("dailytxt");
    private async void ExportDailyPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("dailypng");
    private async void ExportForecastPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("forecastpdf");
    private async void ExportForecastText_Click(object sender, RoutedEventArgs e) => await ExportAsync("forecasttxt");
    private async void ExportTimingPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("timingpdf");
    private async void ExportTimingText_Click(object sender, RoutedEventArgs e) => await ExportAsync("timingtxt");
    private async void ExportSynastryPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("synastrypdf");
    private async void ExportSynastryPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("synastrypng");

    private async Task ExportAsync(string kind)
    {
        var chart = ViewModel.ChartVM.Chart;
        if (chart is null)
        {
            ViewModel.StatusMessage = "Select a chart first, then export.";
            return;
        }

        // Everything the export will use is taken now, before the save dialog is shown:
        // while that dialog is open another chart may be selected or a calculation may
        // finish, and the file must not end up with one person's wheel and another's text.
        var report = ViewModel.ChartVM.ReportSections;
        var worksheet = ViewModel.ChartVM.Worksheet;
        var sensitivity = ViewModel.ChartVM.Sensitivity;
        string name = chart.Celebrity.Name;

        // The daily exports write the reading already on screen (for whatever date the
        // Daily view is set to) rather than recalculating it — but only once it is the
        // reading for this chart, not the previous one still showing while this one loads.
        var reading = ViewModel.DailyVM.Reading;
        bool daily = kind.StartsWith("daily", StringComparison.Ordinal);
        if (daily && (reading is null || ViewModel.DailyVM.IsBusy || reading.Name != name))
        {
            ViewModel.StatusMessage = "The daily horoscope is not ready yet — try again in a moment.";
            return;
        }

        var forecast = ViewModel.ForecastVM.Reading;
        bool forecasting = kind.StartsWith("forecast", StringComparison.Ordinal);
        if (forecasting && (forecast is null || ViewModel.ForecastVM.IsBusy || forecast.Name != name))
        {
            ViewModel.StatusMessage = "The forecast is not ready yet — open the Forecast view, then try again in a moment.";
            return;
        }

        var timing = ViewModel.TimingVM.Reading;
        bool timed = kind.StartsWith("timing", StringComparison.Ordinal);
        if (timed && (timing is null || ViewModel.TimingVM.IsBusy || timing.Name != name))
        {
            ViewModel.StatusMessage = "The solar return and progressions are not ready yet — open the Timing view, then try again in a moment.";
            return;
        }

        // Likewise the synastry exports write the comparison already on screen.
        var comparison = ViewModel.SynastryVM.Comparison;
        var synastryReading = ViewModel.SynastryVM.Reading;
        bool synastry = kind.StartsWith("synastry", StringComparison.Ordinal);
        if (synastry && (comparison is null || synastryReading is null || ViewModel.SynastryVM.IsBusy ||
                         !ReferenceEquals(comparison.First, chart)))
        {
            ViewModel.StatusMessage = "Open the Synastry view and choose someone to compare with first, then export.";
            return;
        }

        try
        {
            var (label, ext) = kind switch
            {
                "pdf"  => ("PDF document", ".pdf"),
                "png"  => ("PNG image", ".png"),
                "sheetpng" => ("PNG image", ".png"),
                "json" => ("JSON data", ".json"),
                "xml"  => ("XML data", ".xml"),
                "worksheettxt" => ("Text file", ".txt"),
                "dailypdf" => ("PDF document", ".pdf"),
                "dailytxt" => ("Text file", ".txt"),
                "dailypng" => ("PNG image", ".png"),
                "forecastpdf" => ("PDF document", ".pdf"),
                "forecasttxt" => ("Text file", ".txt"),
                "timingpdf" => ("PDF document", ".pdf"),
                "timingtxt" => ("Text file", ".txt"),
                "synastrypdf" => ("PDF document", ".pdf"),
                "synastrypng" => ("PNG image", ".png"),
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add(label, [ext]);
            picker.SuggestedFileName =
                daily ? SafeFileName($"{chart.Celebrity.Name} - daily {reading!.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}") :
                synastry ? SafeFileName($"{synastryReading!.FirstName} and {synastryReading.SecondName} - synastry") :
                kind == "worksheettxt" ? SafeFileName($"{chart.Celebrity.Name} - worksheet") :
                kind == "sheetpng" ? SafeFileName($"{chart.Celebrity.Name} - chart with tables") :
                timed ? SafeFileName($"{chart.Celebrity.Name} - return and progressions {timing!.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}") :
                forecasting ? SafeFileName($"{chart.Celebrity.Name} - forecast from {forecast!.Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}") :
                SafeFileName(chart.Celebrity.Name);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            ViewModel.StatusMessage = $"Exporting {file.Name}…";
            byte[] bytes = kind switch
            {
                "pdf"  => ExportService.ToPdf(chart, report,
                                              await ExportService.RenderChartPngAsync(chart),
                                              worksheet, sensitivity),
                "png"  => await ExportService.RenderChartPngAsync(chart),
                "sheetpng" => await ExportService.RenderChartSheetPngAsync(chart),
                "json" => ExportService.ToJson(chart),
                "xml"  => ExportService.ToXml(chart),
                "worksheettxt" => WorksheetService.ToText(WorksheetService.Build(chart), sensitivity),
                "dailypdf" => DailyExportService.ToPdf(reading!,
                                              await ExportService.RenderTransitWheelPngAsync(chart, reading!)),
                "dailypng" => await ExportService.RenderTransitWheelPngAsync(chart, reading!),
                "dailytxt" => DailyExportService.ToText(reading!),
                "forecastpdf" => DailyExportService.ForecastToPdf(forecast!),
                "forecasttxt" => DailyInterpreter.ForecastToText(forecast!),
                "timingpdf" => DailyExportService.TimingToPdf(timing!,
                                              timing!.Return is { } solar ? await ExportService.RenderChartPngAsync(solar.Chart) : null),
                "timingtxt" => TimingService.ToText(timing!),
                "synastrypdf" => SynastryExportService.ToPdf(comparison!, synastryReading!,
                                              await ExportService.RenderBiWheelPngAsync(comparison!)),
                "synastrypng" => await ExportService.RenderBiWheelPngAsync(comparison!),
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            await FileIO.WriteBytesAsync(file, bytes);
            ViewModel.StatusMessage = $"Exported {file.Name}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Export failed: {ex.Message}";
            Diagnostics.Log($"Export {kind} failed: {ex}");
        }
    }

    // ── My Charts: save a copy, bring a copy in ───────────────────────────────

    private async void ExportMyCharts_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.MyChartsCount == 0)
        {
            ViewModel.StatusMessage = "There are no charts of your own to save yet.";
            return;
        }
        try
        {
            // Taken before the dialog opens, like the other exports.
            byte[] bytes = ViewModel.ExportMyCharts();
            int count = ViewModel.MyChartsCount;

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("Lore charts", [".json"]);
            picker.SuggestedFileName =
                $"Lore - My Charts {DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            await FileIO.WriteBytesAsync(file, bytes);
            ViewModel.StatusMessage = $"Saved {count} of your charts to {file.Name}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Couldn't save a copy of My Charts: {ex.Message}";
            Diagnostics.Log($"My Charts export failed: {ex}");
        }
    }

    private async void ImportMyCharts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return; // user cancelled

            await ViewModel.ImportMyChartsAsync(file.Path);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Couldn't bring in charts: {ex.Message}";
            Diagnostics.Log($"My Charts import failed: {ex}");
        }
    }

    private static string SafeFileName(string name)
    {
        var cleaned = string.Join("_", name.Split(Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(cleaned) ? "chart" : cleaned;
    }

    // x:Bind helpers for the Chart/Report/Legend view switch (instance methods so x:Bind can call them).
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public bool Not(bool b) => !b;

    // Body panes: the legend, when shown, hides the chart, report, daily and synastry views.
    public Visibility VisChartBody(bool showReport, bool showWorksheet, bool showDaily, bool showForecast, bool showTiming, bool showSynastry, bool showLegend) =>
        !showReport && !showWorksheet && !showDaily && !showForecast && !showTiming && !showSynastry && !showLegend ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisReportBody(bool show, bool showLegend) =>
        show && !showLegend ? Visibility.Visible : Visibility.Collapsed;
}
