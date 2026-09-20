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

        bool initialized = false;
        Activated += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            string dataPath   = Path.Combine(AppContext.BaseDirectory, "Data", "celebrities.json");
            string citiesPath = Path.Combine(AppContext.BaseDirectory, "Data", "cities.json");
            await ViewModel.InitializeAsync(dataPath, citiesPath);
        };
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
