using Jupiter.UI;
using Lore.Services;
using Lore.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lore;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public static new App Current => (App)Application.Current;

    // What the shell remembers, and the accent every window takes from it.
    public ShellPreferences Preferences { get; private set; } = null!;
    public AccentService Accent { get; private set; } = null!;

    public App()
    {
        UseGregorianCalendar();
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        // Errors on background threads and in forgotten tasks never reach the handler
        // above; log those too.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Log($"Unhandled (background): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diagnostics.Log($"Unobserved task error: {e.Exception}");
            e.SetObserved();
        };
    }

    // Dates are shown in the Windows regional format, but always in the Gregorian
    // calendar: a birth year must not appear as a Buddhist or Persian one. (Stored dates
    // are separately read and written with the invariant culture.)
    private static void UseGregorianCalendar()
    {
        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.CurrentCulture.Clone();
        if (culture.DateTimeFormat.Calendar is not System.Globalization.GregorianCalendar)
        {
            var gregorian = culture.OptionalCalendars.OfType<System.Globalization.GregorianCalendar>().FirstOrDefault();
            if (gregorian is not null) culture.DateTimeFormat.Calendar = gregorian;
            else culture = System.Globalization.CultureInfo.InvariantCulture;
        }
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Preferences = ShellPreferences.Load();
        Accent = new AccentService(Preferences);

        // Everything below can fail before there is a main window to report it in: the
        // Swiss Ephemeris DLL missing, a corpus file damaged, the data folder out of
        // reach. A failure is shown in a window of its own.
        try
        {
            _mainWindow = BuildMainWindow();
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Start-up failed: {ex}");
            ShowStartupFailure(ex);
        }
    }

    private static MainWindow BuildMainWindow()
    {
        string baseDir       = AppContext.BaseDirectory;
        string ephemerisPath = Path.Combine(baseDir, "Assets", "Ephemeris");
        string interpPath    = Path.Combine(baseDir, "Data", "interpretations.json");
        string dailyPath     = Path.Combine(baseDir, "Data", "daily.json");
        string synastryPath  = Path.Combine(baseDir, "Data", "synastry.json");
        string davisonPath   = Path.Combine(baseDir, "Data", "davison.json");
        string inventoryPath = Path.Combine(baseDir, "Data", "inventory.json");
        string mirrorPath    = Path.Combine(baseDir, "Data", "mirror.json");

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
        var inventoryStore = new InventoryStore();
        var mainVm      = new MainViewModel(celebSvc, chartSvc, interpreter, userCharts, cities, hospitals,
                                            transits, daily, synastry, settings,
                                            new DavisonInterpreter(davisonPath),
                                            new InventoryService(inventoryPath), inventoryStore,
                                            new MirrorInterpreter(mirrorPath, interpreter))
        {
            // Anything Lore is installed with that is not there is said, not left to
            // show as a reading that is empty. (coreclr.dll is beside a published build
            // and not beside one run from the development tools.)
            InstallProblem = InstalledFiles.Problem(baseDir, published: File.Exists(Path.Combine(baseDir, "coreclr.dll"))),
        };

        var window = new MainWindow(mainVm);
        // What is still waiting to be written (the chart and view to come back to). The
        // window has already waited for the inventory answers, and asked what to do if
        // they could not be saved (MainWindow.OnClosing); this is the last chance.
        window.Closed += (_, _) => { settings.Flush(); inventoryStore.Flush(TimeSpan.FromSeconds(3)); };
        return window;
    }

    // Kept in a field so the window is not collected while it is on screen.
    private Window? _failureWindow;

    // Lore could not start. Says so, with what went wrong and where the details are;
    // closing the window ends the app.
    private void ShowStartupFailure(Exception ex)
    {
        try
        {
            string missing = ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                ? "The Swiss Ephemeris library (sweph.dll) is missing or damaged. "
                : ex is FileNotFoundException or DirectoryNotFoundException or System.Text.Json.JsonException
                    ? "One of the files Lore is installed with is missing or damaged. "
                    : "";
            var text = new TextBlock
            {
                Text = "Lore could not start.\n\n" + missing +
                       "Installing Lore again usually puts this right; your own saved charts are kept " +
                       "elsewhere and are not affected.\n\n" +
                       $"What went wrong: {ex.Message}\n\n" +
                       $"The details are in {Diagnostics.LogFile}",
                Style = (Style)Resources["JupiterBodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(24),
            };
            var root = new Grid
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Resources["JupiterWindowBrush"],
                RequestedTheme = ElementTheme.Dark,
            };
            root.Children.Add(text);
            _failureWindow = new Window { Title = "Lore", Content = root };
            _failureWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 420));
            _failureWindow.Closed += (_, _) => Exit();
            _failureWindow.Activate();
        }
        catch (Exception second)
        {
            Diagnostics.Log($"Start-up failure could not be shown: {second}");
            Exit();
        }
    }

    // An error in the interface is logged and the app carries on, but not silently: the
    // status bar says something went wrong and where the details are.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Diagnostics.Log($"Unhandled: {e.Exception}");
        try
        {
            if (_mainWindow is not null)
                _mainWindow.ViewModel.StatusMessage =
                    $"Something went wrong ({e.Message}). Lore is still running; the details are in {Diagnostics.LogFile}.";
        }
        catch { /* reporting must never raise a second error */ }
    }
}
