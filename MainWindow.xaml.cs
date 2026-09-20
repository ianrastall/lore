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

    public MainWindow(MainViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();

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
            string dataPath   = Path.Combine(AppContext.BaseDirectory, "Data", "celebrities.json");
            string citiesPath = Path.Combine(AppContext.BaseDirectory, "Data", "cities.json");
            await ViewModel.InitializeAsync(dataPath, citiesPath);
        }

        if (Content is FrameworkElement root)
            root.Loaded += (_, _) => InitOnce("Loaded");
        Activated += (_, _) => InitOnce("Activated");
    }

    private async void AddChart_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddChartDialog(ViewModel.Cities) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && dialog.Result is { } chart)
            await ViewModel.AddCustomChartAsync(chart);
    }

    private void ShowChart_Click(object sender, RoutedEventArgs e) => ViewModel.ShowReport = false;
    private void ShowReport_Click(object sender, RoutedEventArgs e) => ViewModel.ShowReport = true;

    // ── Export ────────────────────────────────────────────────────────────────
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("pdf");
    private async void ExportPng_Click(object sender, RoutedEventArgs e) => await ExportAsync("png");
    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ExportAsync("json");
    private async void ExportXml_Click(object sender, RoutedEventArgs e) => await ExportAsync("xml");

    private async Task ExportAsync(string kind)
    {
        var chart = ViewModel.ChartVM.Chart;
        if (chart is null)
        {
            ViewModel.StatusMessage = "Select a chart first, then export.";
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
                _      => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add(label, [ext]);
            picker.SuggestedFileName = SafeFileName(chart.Celebrity.Name);

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

    // x:Bind helpers for the Chart/Report toggle (instance methods so x:Bind can call them).
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public bool Not(bool b) => !b;
}
