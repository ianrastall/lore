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
        _settingsReady = true;

        AppWindow.Title = "Lore — Natal Charts";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();

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

    private async void AddChart_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && dialog.Result is { } chart)
            await ViewModel.AddCustomChartAsync(chart);
    }

    private async void EditChart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCelebrity is not { } existing || !ViewModel.SelectedIsCustom) return;
        var dialog = new AddChartDialog(ViewModel.Cities, ViewModel.Hospitals, existing) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && dialog.Result is { } chart)
            await ViewModel.UpdateCustomChartAsync(chart);
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
    private void ShowSynastry_Click(object sender, RoutedEventArgs e) => ShowBody(synastry: true);

    private void ShowBody(bool report = false, bool worksheet = false, bool daily = false, bool synastry = false)
    {
        ViewModel.ShowLegend = false;
        ViewModel.ShowReport = report;
        ViewModel.ShowWorksheet = worksheet;
        ViewModel.ShowDaily = daily;
        ViewModel.ShowSynastry = synastry;
        ChartToggle.IsChecked = !report && !worksheet && !daily && !synastry;
        ReportToggle.IsChecked = report;
        WorksheetToggle.IsChecked = worksheet;
        DailyToggle.IsChecked = daily;
        SynastryToggle.IsChecked = synastry;
    }

    // False while the constructor is filling the two settings boxes in.
    private readonly bool _settingsReady;

    private async void Settings_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (!_settingsReady || HouseSystemCombo.SelectedIndex < 0 || NodeTypeCombo.SelectedIndex < 0) return;
        await ViewModel.ApplySettingsAsync(new Models.ChartSettings(
            (Models.HouseSystem)HouseSystemCombo.SelectedIndex,
            (Models.NodeType)NodeTypeCombo.SelectedIndex));
    }

    private void ShowLegend_Click(object sender, RoutedEventArgs e) => ViewModel.ShowLegend = true;
    private void Legend_CloseRequested(object sender, EventArgs e) => ViewModel.ShowLegend = false;

    // ── Export ────────────────────────────────────────────────────────────────
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("pdf");
    private async void ExportPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("png");
    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ExportAsync("json");
    private async void ExportXml_Click(object sender, RoutedEventArgs e) => await ExportAsync("xml");
    private async void ExportWorksheet_Click(object sender, RoutedEventArgs e) => await ExportAsync("worksheettxt");
    private async void ExportDailyPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("dailypdf");
    private async void ExportDailyText_Click(object sender, RoutedEventArgs e) => await ExportAsync("dailytxt");
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

        // The daily exports write the reading already on screen (for whatever date the
        // Daily view is set to) rather than recalculating it.
        var reading = ViewModel.DailyVM.Reading;
        bool daily = kind.StartsWith("daily", StringComparison.Ordinal);
        if (daily && reading is null)
        {
            ViewModel.StatusMessage = "The daily horoscope is not ready yet — try again in a moment.";
            return;
        }

        // Likewise the synastry exports write the comparison already on screen.
        var comparison = ViewModel.SynastryVM.Comparison;
        var synastryReading = ViewModel.SynastryVM.Reading;
        bool synastry = kind.StartsWith("synastry", StringComparison.Ordinal);
        if (synastry && (comparison is null || synastryReading is null))
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
                "json" => ("JSON data", ".json"),
                "xml"  => ("XML data", ".xml"),
                "worksheettxt" => ("Text file", ".txt"),
                "dailypdf" => ("PDF document", ".pdf"),
                "dailytxt" => ("Text file", ".txt"),
                "synastrypdf" => ("PDF document", ".pdf"),
                "synastrypng" => ("PNG image", ".png"),
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add(label, [ext]);
            picker.SuggestedFileName =
                daily ? SafeFileName($"{chart.Celebrity.Name} - daily {reading!.Date:yyyy-MM-dd}") :
                synastry ? SafeFileName($"{synastryReading!.FirstName} and {synastryReading.SecondName} - synastry") :
                kind == "worksheettxt" ? SafeFileName($"{chart.Celebrity.Name} - worksheet") :
                SafeFileName(chart.Celebrity.Name);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return; // user cancelled

            ViewModel.StatusMessage = $"Exporting {file.Name}…";
            byte[] bytes = kind switch
            {
                "pdf"  => ExportService.ToPdf(chart, ViewModel.ChartVM.ReportSections,
                                              await ExportService.RenderChartPngAsync(chart)),
                "png"  => await ExportService.RenderChartPngAsync(chart),
                "json" => ExportService.ToJson(chart),
                "xml"  => ExportService.ToXml(chart),
                "worksheettxt" => WorksheetService.ToText(WorksheetService.Build(chart)),
                "dailypdf" => DailyExportService.ToPdf(reading!),
                "dailytxt" => DailyExportService.ToText(reading!),
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
    public Visibility VisChartBody(bool showReport, bool showWorksheet, bool showDaily, bool showSynastry, bool showLegend) =>
        !showReport && !showWorksheet && !showDaily && !showSynastry && !showLegend ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisReportBody(bool show, bool showLegend) =>
        show && !showLegend ? Visibility.Visible : Visibility.Collapsed;
}
