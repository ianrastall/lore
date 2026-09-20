using Lore.ViewModels;
using Lore.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

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

    // x:Bind helpers for the Chart/Report toggle (instance methods so x:Bind can call them).
    public Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VisIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public bool Not(bool b) => !b;
}
