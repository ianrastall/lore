using Lore.Services;
using Lore.ViewModels;
using Microsoft.UI.Xaml;

namespace Lore;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Splash goes up first and is painted before anything else is built; it then
        // stays over the main window until the one-time data load has finished.
        SplashWindow? splash = await SplashWindow.ShowAsync();

        string baseDir       = AppContext.BaseDirectory;
        string ephemerisPath = Path.Combine(baseDir, "Assets", "Ephemeris");
        string interpPath    = Path.Combine(baseDir, "Data", "interpretations.json");
        string dailyPath     = Path.Combine(baseDir, "Data", "daily.json");
        string synastryPath  = Path.Combine(baseDir, "Data", "synastry.json");

        var celebSvc    = new CelebrityService();
        var settings    = new SettingsService();
        var chartSvc    = new ChartService(ephemerisPath) { Settings = settings.Load() };
        var interpreter = new ChartInterpreter(interpPath);
        var userCharts  = new UserChartService();
        var cities      = new CityService();
        var hospitals   = new HospitalService();
        var transits    = new TransitService(chartSvc);
        var daily       = new DailyInterpreter(dailyPath);
        var synastry    = new SynastryInterpreter(synastryPath);
        var mainVm      = new MainViewModel(celebSvc, chartSvc, interpreter, userCharts, cities, hospitals,
                                            transits, daily, synastry, settings);

        _mainWindow = new MainWindow(mainVm);
        _mainWindow.Activate();

        if (splash is not null)
            await splash.CloseWhenAsync(_mainWindow.Initialized);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            string log = Path.Combine(Path.GetTempPath(), "lore-crash.txt");
            File.AppendAllText(log, $"[{DateTime.Now:s}] {e.Exception}\n\n");
        }
        catch { }
    }
}
