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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string baseDir       = AppContext.BaseDirectory;
        string ephemerisPath = Path.Combine(baseDir, "Assets", "Ephemeris");
        string interpPath    = Path.Combine(baseDir, "Data", "interpretations.json");

        var celebSvc    = new CelebrityService();
        var chartSvc    = new ChartService(ephemerisPath);
        var interpreter = new ChartInterpreter(interpPath);
        var userCharts  = new UserChartService();
        var cities      = new CityService();
        var mainVm      = new MainViewModel(celebSvc, chartSvc, interpreter, userCharts, cities);

        _mainWindow = new MainWindow(mainVm);
        _mainWindow.Activate();
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
