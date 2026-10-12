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
        AppWindow.Closing += OnClosing;

#if DEBUG
        // For checking the "chart with tables" image without clicking through a save
        // dialog: with LORE_SHEET_OUT set to a file path, a debug build writes the image
        // for the first chart it opens there, with the JSON, XML and PDF exports beside it.
        if (Environment.GetEnvironmentVariable("LORE_SHEET_OUT") is { Length: > 0 } sheetOut)
        {
            bool written = false;
            vm.ChartVM.PropertyChanged += async (_, e) =>
            {
                if (written || e.PropertyName != nameof(ChartViewModel.Chart) || vm.ChartVM.Chart is not { } first) return;
                written = true;
                try
                {
                    await File.WriteAllBytesAsync(sheetOut, await ExportService.RenderChartSheetPngAsync(first));
                    // And the data exports and the PDF beside it, under the same name.
                    await File.WriteAllBytesAsync(Path.ChangeExtension(sheetOut, ".json"), ExportService.ToJson(first));
                    await File.WriteAllBytesAsync(Path.ChangeExtension(sheetOut, ".xml"), ExportService.ToXml(first));
                    await File.WriteAllBytesAsync(Path.ChangeExtension(sheetOut, ".pdf"), ExportService.ToPdf(
                        first, vm.ChartVM.ReportSections, await ExportService.RenderChartPngAsync(first),
                        vm.ChartVM.Worksheet, vm.ChartVM.Sensitivity));
                }
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

    // Set once it is settled that the window may go: everything is saved, or the reader
    // has said to close without what could not be.
    private bool _closeNow;

    // Inventory answers are saved in the background as they are given. Closing waits for
    // the ones still on their way, and if they cannot be saved it says so and asks,
    // rather than letting them go without a word.
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeNow || !ViewModel.HasUnsavedAnswers) return;
        args.Cancel = true;
        try
        {
            while (!await ViewModel.SaveOutstandingAnswersAsync())
            {
                if (_dialogOpen) return; // another dialog is up: stay open, and ask on the next close
                _dialogOpen = true;
                Microsoft.UI.Xaml.Controls.ContentDialogResult answer;
                try
                {
                    answer = await new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "Some inventory answers are not saved",
                        Content = (ViewModel.UnsavedAnswersProblem ?? "The inventory answers file could not be written.") +
                                  "\n\nIf Lore closes now, what was answered or deleted in the Inventory view since the last " +
                                  "successful save is lost. Answers saved earlier are not affected.",
                        PrimaryButtonText = "Try again",
                        SecondaryButtonText = "Close without saving them",
                        CloseButtonText = "Keep Lore open",
                        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                        RequestedTheme = ElementTheme.Dark,
                        XamlRoot = Content.XamlRoot,
                    }.ShowAsync();
                }
                finally
                {
                    _dialogOpen = false;
                }
                if (answer == Microsoft.UI.Xaml.Controls.ContentDialogResult.None) return; // stay open
                if (answer == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary) break;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Saving the inventory answers before closing failed: {ex}");
        }
        _closeNow = true;
        Close();
    }

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

    // Ctrl+1 … Ctrl+8, in the order the buttons run across the toolbar.
    private static readonly string[] Views = ["Chart", "Report", "Worksheet", "Daily", "Forecast", "Timing", "Synastry", "Inventory"];

    private void ViewShortcut_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        int index = sender.Key - Windows.System.VirtualKey.Number1;
        if (index >= 0 && index < Views.Length) ShowView(Views[index]);
    }

    private void ShowView(string view) => ShowBody(
        report: view == "Report", worksheet: view == "Worksheet", daily: view == "Daily",
        forecast: view == "Forecast", timing: view == "Timing", synastry: view == "Synastry",
        inventory: view == "Inventory");

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
    private void ShowInventory_Click(object sender, RoutedEventArgs e) => ShowBody(inventory: true);

    private void ShowBody(bool report = false, bool worksheet = false, bool daily = false, bool forecast = false, bool timing = false, bool synastry = false, bool inventory = false)
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
        ViewModel.ShowInventory = inventory;
        InventoryToggle.IsChecked = inventory;
        ChartToggle.IsChecked = !report && !worksheet && !daily && !forecast && !timing && !synastry && !inventory;
        ReportToggle.IsChecked = report;
        WorksheetToggle.IsChecked = worksheet;
        DailyToggle.IsChecked = daily;
        SynastryToggle.IsChecked = synastry;

        ViewModel.RememberView(
            report ? "Report" : worksheet ? "Worksheet" : daily ? "Daily" :
            forecast ? "Forecast" : timing ? "Timing" : synastry ? "Synastry" : inventory ? "Inventory" : "Chart");
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

    private readonly SuggestionSearch _homeSearch = new();

    private async void HomeBox_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == Microsoft.UI.Xaml.Controls.AutoSuggestionBoxTextChangeReason.UserInput)
            await _homeSearch.RunAsync(sender, q => ViewModel.Cities.Search(q));
    }

    // A suggestion was picked, or Enter pressed: take the pick, else the best match.
    private void HomeBox_QuerySubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var city = args.ChosenSuggestion as Models.City ?? ViewModel.Cities.Search(args.QueryText).FirstOrDefault();
        if (city is null) return;

        _homeSearch.Cancel();
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
    private async void ExportDavisonPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("davisonpdf");
    private async void ExportDavisonPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("davisonpng");
    private async void ExportDavisonSheetPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("davisonsheetpng");
    private async void ExportDavisonText_Click(object sender, RoutedEventArgs e) => await ExportAsync("davisontxt");
    private async void ExportInventoryText_Click(object sender, RoutedEventArgs e) => await ExportAsync("inventorytxt");
    private async void ExportMirrorText_Click(object sender, RoutedEventArgs e) => await ExportAsync("mirrortxt");

    // One export at a time: a second, started while the first is still being written,
    // would be competing with it for the same drawing surface and status line.
    private bool _exporting;

    private async Task ExportAsync(string kind)
    {
        if (_exporting)
        {
            ViewModel.StatusMessage = "An export is still being written — try again when it has finished.";
            return;
        }
        _exporting = true;
        try
        {
            await ExportCoreAsync(kind);
        }
        finally
        {
            _exporting = false;
        }
    }

    private async Task ExportCoreAsync(string kind)
    {
        try
        {
            // Everything the export will use is taken now, before the save dialog is shown:
            // while that dialog is open another chart may be selected, a calculation may
            // finish or answers be deleted, and the file must not end up with one person's
            // name and another's contents. From here on only `request` is read, never the
            // view models (see ExportRequest).
            if (ExportRequest.Prepare(ViewModel, kind, out string refusal) is not { } request)
            {
                ViewModel.StatusMessage = refusal;
                return;
            }
            var chart = request.Chart;

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add(request.Label, [request.Extension]);
            picker.SuggestedFileName = request.SuggestedName;

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            ViewModel.StatusMessage = $"Exporting {file.Name}…";
            // The wheel first, for the kinds that have one. (Drawn here: the drawing
            // code shares its fonts and device with the wheels on screen.)
            byte[]? wheel = kind switch
            {
                "pdf" or "png" => await ExportService.RenderChartPngAsync(chart),
                "sheetpng" => await ExportService.RenderChartSheetPngAsync(chart),
                "dailypdf" or "dailypng" => await ExportService.RenderTransitWheelPngAsync(chart, request.Daily!),
                "timingpdf" when request.Timing!.Return is { } solar => await ExportService.RenderChartPngAsync(solar.Chart, showVerdict: false),
                "synastrypdf" or "synastrypng" => await ExportService.RenderBiWheelPngAsync(request.Comparison!),
                "davisonpdf" or "davisonpng" => await ExportService.RenderChartPngAsync(request.Davison!.Chart, showVerdict: false),
                "davisonsheetpng" => await ExportService.RenderChartSheetPngAsync(request.Davison!.Chart, showVerdict: false),
                _ => null,
            };

            // Then the document itself, off the UI thread: laying out a PDF of a dozen
            // pages takes long enough to freeze the window if it is done on it.
            byte[] bytes = await Task.Run(() => kind switch
            {
                "pdf"  => ExportService.ToPdf(chart, request.Report, wheel!, request.Worksheet, request.Sensitivity),
                "png" or "sheetpng" or "dailypng" or "synastrypng" or "davisonpng" or "davisonsheetpng" => wheel!,
                "json" => ExportService.ToJson(chart),
                "xml"  => ExportService.ToXml(chart),
                "worksheettxt" => WorksheetService.ToText(request.Worksheet ?? WorksheetService.Build(chart), request.Sensitivity),
                "dailypdf" => DailyExportService.ToPdf(request.Daily!, wheel!),
                "dailytxt" => DailyExportService.ToText(request.Daily!),
                "forecastpdf" => DailyExportService.ForecastToPdf(request.Forecast!),
                "forecasttxt" => DailyInterpreter.ForecastToText(request.Forecast!),
                "timingpdf" => DailyExportService.TimingToPdf(request.Timing!, wheel),
                "timingtxt" => TimingService.ToText(request.Timing!),
                "synastrypdf" => SynastryExportService.ToPdf(request.Comparison!, request.Synastry!, wheel!),
                "davisonpdf" => SynastryExportService.DavisonToPdf(request.Comparison!, request.Davison!, wheel!,
                    WorksheetService.Build(request.Davison!.Chart)),
                "davisontxt" => WorksheetService.ToText(WorksheetService.Build(request.Davison!.Chart)),
                "inventorytxt" or "mirrortxt" => request.Text!,
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            });

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
        try
        {
            // Taken before the dialog opens, like the other exports — and from the file,
            // which may hold charts saved in another Lore window since this one opened.
            var (bytes, count) = await ViewModel.ExportMyChartsAsync();
            if (count == 0)
            {
                ViewModel.StatusMessage = "There are no charts of your own to save yet.";
                return;
            }

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

    // x:Bind helpers for the Chart/Report/Legend view switch (instance methods so x:Bind can call them).
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public bool Not(bool b) => !b;

    // Body panes: the legend, when shown, hides the chart, report, daily and synastry views.
    public Visibility VisChartBody(bool showReport, bool showWorksheet, bool showDaily, bool showForecast, bool showTiming, bool showSynastry, bool showInventory, bool showLegend) =>
        !showReport && !showWorksheet && !showDaily && !showForecast && !showTiming && !showSynastry && !showInventory && !showLegend ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisReportBody(bool show, bool showLegend) =>
        show && !showLegend ? Visibility.Visible : Visibility.Collapsed;
}
