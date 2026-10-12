using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using System.Collections.ObjectModel;

namespace Lore.ViewModels;

// The window's one view model: who is selected, what is on screen, and the view models
// of the eight views, which all follow the chart shown here.
//
// The class is in three files: this one (selection, the chart on screen, settings),
// MainViewModel.Library.cs (the list of people: building it, scoring it, filtering it)
// and MainViewModel.MyCharts.cs (the reader's own charts and answers: saving, deleting,
// moving to another PC).
public sealed partial class MainViewModel : ObservableObject
{
    private readonly CelebrityService _celebrities;
    private readonly ChartService _charts;
    private readonly UserChartService _userCharts;
    private readonly SettingsService? _settings;
    private readonly InventoryStore? _inventoryStore;

    private List<Celebrity> _all = [];

    // Which chart and view were open, kept between sessions (see SettingsService.UiState).
    private SettingsService.UiState _ui = new();
    public string LastView => _ui.LastView;

    public void RememberView(string view)
    {
        if (view == _ui.LastView) return;
        _ui = _ui with { LastView = view };
        _settings?.SaveUi(_ui);
    }

    public ChartViewModel ChartVM { get; }

    // The Daily view's state; follows whichever chart ChartVM is showing.
    public DailyViewModel DailyVM { get; }

    // The Forecast view's state: the transits coming up for ChartVM's chart.
    public ForecastViewModel ForecastVM { get; }

    // The Timing view's state: the solar return and progressions for ChartVM's chart.
    public TimingViewModel TimingVM { get; }

    // The Synastry view's state: ChartVM's chart compared with a second person.
    public SynastryViewModel SynastryVM { get; }

    // The Inventory view's state: the personality questionnaire for whoever ChartVM's
    // chart belongs to. It reads nothing from the chart but whose it is.
    public InventoryViewModel InventoryVM { get; }

    // Bumped per chart load; a calculation that finishes after a newer selection has
    // started is dropped instead of overwriting the newer chart.
    private int _loadGeneration;

    // Stops the calculation a newer selection has overtaken (see LoadChartAsync).
    private CancellationTokenSource? _loadCancel;

    // Bumped each time the list is rebuilt and each time a setting is changed: of two
    // that overlap, only the later one's results are used.
    private int _poolGeneration;
    private int _settingsGeneration;

    // Stops the scoring of the library that a newer rebuild of the list has overtaken.
    private CancellationTokenSource? _poolCancel;

    // Files Lore is installed with that are not there (see InstalledFiles), in words
    // for the status bar; null when all are. Set by the app when it starts.
    public string? InstallProblem { get; init; }

    // The two things the progress ring stands for. They are kept apart because they
    // overlap: the first chart starts calculating before the data load has returned,
    // and either may finish first.
    private bool _initializing;
    private bool _calculating;

    private void SetBusy(bool? initializing = null, bool? calculating = null)
    {
        _initializing = initializing ?? _initializing;
        _calculating = calculating ?? _calculating;
        IsLoading = _initializing || _calculating;
    }

    // Exposed so the Add-Chart dialog (owned by the window) can offer city autocomplete.
    public CityService Cities { get; }

    // Exposed alongside Cities so the Add-Chart dialog can also offer hospital search
    // (a precise birthplace: hospitals are where people are born).
    public HospitalService Hospitals { get; }

    // The hospital list is 25 MB and only the Add-Chart dialog uses it, so it is read
    // when that dialog first opens rather than at every start. Called on each open;
    // the list is read once, off the UI thread, and the task never faults.
    private string _hospitalsPath = "";
    private Task? _hospitalsLoading;

    public Task EnsureHospitalsLoadedAsync() => _hospitalsLoading ??= Task.Run(async () =>
    {
        try
        {
            await Hospitals.LoadAsync(_hospitalsPath);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Hospital list could not be loaded: {ex}");
        }
    });

    [ObservableProperty]
    public partial ObservableCollection<Celebrity> DisplayedCelebrities { get; set; } = [];

    [ObservableProperty]
    public partial Celebrity? SelectedCelebrity { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Categories { get; set; } = [];

    [ObservableProperty]
    public partial string? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowReport { get; set; }

    // The Daily horoscope view (mutually exclusive with ShowReport; neither = the chart).
    [ObservableProperty]
    public partial bool ShowDaily { get; set; }

    // The Forecast view (likewise).
    [ObservableProperty]
    public partial bool ShowForecast { get; set; }

    // The Timing view (likewise).
    [ObservableProperty]
    public partial bool ShowTiming { get; set; }

    // The Synastry view (likewise mutually exclusive with the other two).
    [ObservableProperty]
    public partial bool ShowSynastry { get; set; }

    // The Inventory view (likewise).
    [ObservableProperty]
    public partial bool ShowInventory { get; set; }

    // The Worksheet view (likewise).
    [ObservableProperty]
    public partial bool ShowWorksheet { get; set; }

    // Full-page Legend / reference view over the chart+report body.
    [ObservableProperty]
    public partial bool ShowLegend { get; set; }

    // True when the selected entry is a user-created chart (so it can be edited or deleted).
    public bool SelectedIsCustom => SelectedCelebrity?.Category == UserChartService.MyChartsCategory;

    public MainViewModel(
        CelebrityService celebrities,
        ChartService charts,
        ChartInterpreter interpreter,
        UserChartService userCharts,
        CityService cities,
        HospitalService hospitals,
        TransitService transits,
        DailyInterpreter dailyInterpreter,
        SynastryInterpreter synastryInterpreter,
        SettingsService? settings = null,
        DavisonInterpreter? davisonInterpreter = null,
        InventoryService? inventory = null,
        InventoryStore? inventoryStore = null,
        MirrorInterpreter? mirror = null)
    {
        _settings = settings;
        _celebrities = celebrities;
        _charts = charts;
        _userCharts = userCharts;
        Cities = cities;
        Hospitals = hospitals;
        ChartVM = new ChartViewModel(interpreter, charts);
        DailyVM = new DailyViewModel(transits, dailyInterpreter) { Home = settings?.LoadHome() };
        // The forecast and the search for matches are the two costly readings, so each
        // is worked out when its view is first opened for a chart, not for every chart
        // passed in the list (see OnShowForecastChanged, OnShowSynastryChanged).
        ForecastVM = new ForecastViewModel(transits, dailyInterpreter) { IsActive = false };
        TimingVM = new TimingViewModel(new TimingService(charts, dailyInterpreter.HouseTopic), cities);
        SynastryVM = new SynastryViewModel(charts, synastryInterpreter, davisonInterpreter) { IsActive = false };
        _inventoryStore = inventoryStore;
        InventoryVM = new InventoryViewModel(inventory, inventoryStore, mirror);
        InventoryVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InventoryViewModel.ShapeReadings) &&
                InventoryVM.ShapeReadings != _ui.AnswersShapeReadings)
            {
                _ui = _ui with { AnswersShapeReadings = InventoryVM.ShapeReadings };
                _settings?.SaveUi(_ui);
            }
            if (e.PropertyName is nameof(InventoryViewModel.Reading) or nameof(InventoryViewModel.ShapeReadings))
                PassAnswersOn();
        };
    }

    public async Task InitializeAsync(string dataPath, string citiesPath, string hospitalsPath)
    {
        SetBusy(initializing: true);
        StatusMessage = "Loading data…";
        try
        {
            Diagnostics.Log($"Init: dataPath={dataPath} exists={File.Exists(dataPath)}; " +
                            $"citiesPath={citiesPath} exists={File.Exists(citiesPath)}; " +
                            $"hospitalsPath={hospitalsPath} exists={File.Exists(hospitalsPath)}");
            await _celebrities.LoadAsync(dataPath);
            await _userCharts.LoadAsync();
            await Cities.LoadAsync(citiesPath);
            _hospitalsPath = hospitalsPath; // read later: see EnsureHospitalsLoadedAsync
            await RebuildPoolAsync();
            StatusMessage = _userCharts.LoadProblem ?? _inventoryStore?.RecoveryNote ?? _inventoryStore?.Problem ??
                            InstallProblem ?? $"{_all.Count} people loaded.";

            // Pick up where the last session left off.
            if (_settings is not null)
            {
                _ui = _settings.LoadUi();
                InventoryVM.ShapeReadings = _ui.AnswersShapeReadings;
                if (_ui.LastChartId is { } id && DisplayedCelebrities.FirstOrDefault(c => c.Id == id) is { } last)
                    SelectedCelebrity = last;
            }
            Diagnostics.Log($"Init OK: celebrities={_celebrities.All.Count}, " +
                            $"userCharts={_userCharts.Charts.Count}, categories={Categories.Count}, " +
                            $"displayed={DisplayedCelebrities.Count}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading data: {ex.Message}";
            Diagnostics.Log($"Init FAILED: {ex}");
        }
        finally
        {
            SetBusy(initializing: false);
        }
    }

    partial void OnSelectedCelebrityChanged(Celebrity? value)
    {
        OnPropertyChanged(nameof(SelectedIsCustom));
        // Whatever is selected now (even nothing), a chart still being calculated for
        // the previous selection must not arrive and show itself.
        _loadGeneration++;
        if (value is not null && value.Id != _ui.LastChartId)
        {
            _ui = _ui with { LastChartId = value.Id };
            _settings?.SaveUi(_ui);
        }
        if (value is not null)
        {
            ShowLegend = false; // picking a person returns from the legend to their chart
            _ = LoadChartAsync(value);
        }
        else
        {
            // Nothing is selected, so nothing is being calculated for the screen: the
            // calculation just dropped must not leave the progress ring turning.
            _loadCancel?.Cancel();
            SetBusy(calculating: false);
        }
    }

    // The Forecast and the Synastry view's search work only while their view is open.
    partial void OnShowForecastChanged(bool value) => ForecastVM.IsActive = value;
    partial void OnShowSynastryChanged(bool value) => SynastryVM.IsActive = value;

    // Where the reader is, for the Daily view's sunrise, sunset and planetary hours; null
    // if no city has been chosen. Saved as soon as it is set or cleared.
    public HomePlace? Home
    {
        get => DailyVM.Home;
        set
        {
            if (value == DailyVM.Home) return;
            DailyVM.Home = value;
            _settings?.SaveHome(value);
            StatusMessage = value is null
                ? "Your place is cleared; the Daily view no longer shows the planetary hours."
                : $"Sunrise, sunset and the planetary hours in the Daily view are now for {value.Name}.";
        }
    }

    // The house system and node type in force (see ChartSettings).
    public ChartSettings Settings => _charts.Settings;

    // Changes a calculation setting: saved, then everything on screen is recalculated
    // with it — the browse list's dignity verdicts and the chart being shown.
    public async Task ApplySettingsAsync(ChartSettings settings)
    {
        if (settings == _charts.Settings) return;
        int request = ++_settingsGeneration;
        _charts.Settings = settings;
        _settings?.Save(settings);

        await RebuildPoolAsync();
        // A newer change overtook this one while the list was rescored; it will redraw
        // the chart and say what is in use.
        if (request != _settingsGeneration) return;
        if (SelectedCelebrity is { } selected)
            await LoadChartAsync(selected);
        StatusMessage = $"Now using {settings.Houses.Name()} houses, the {settings.Node.Name().ToLowerInvariant()}, " +
                        $"{(settings.Lilith == LilithType.True ? "true" : "mean")} Lilith " +
                        $"and {settings.Orbs.PresetName.ToLowerInvariant()} orbs.";
    }

    // Hands the chart-and-answers reading to the readings whose wording it shapes: the
    // Report, the Daily and the Forecast. Each uses it only for the chart it was made
    // for, so it does not matter that it arrives a moment before or after that chart.
    private void PassAnswersOn()
    {
        var answers = InventoryVM.ShapeReadings ? InventoryVM.Reading : null;
        ChartVM.Answers = answers;
        DailyVM.Answers = answers;
        ForecastVM.Answers = answers;
    }

    // Empties every view: there is no chart to show.
    private void ShowNoChart()
    {
        ChartVM.Chart = null;
        DailyVM.Chart = null;
        ForecastVM.Chart = null;
        TimingVM.Chart = null;
        SynastryVM.Chart = null;
        InventoryVM.Chart = null;
    }

    // False while the chart on screen is not (or not yet) the selected person's: theirs
    // is still being calculated and the one before is still showing. Nothing is exported
    // then. (With nobody selected — the search box has been typed in, say — the chart
    // still on screen is the current one.)
    public bool ChartIsCurrent =>
        !_calculating && (SelectedCelebrity is null || ReferenceEquals(ChartVM.Chart?.Celebrity, SelectedCelebrity));

    // What an export of `kind` would write, taken now (see ExportRequest), or null and
    // why not.
    public ExportRequest? PrepareExport(string kind, out string refusal) => ExportRequest.Prepare(this, kind, out refusal);

    private async Task LoadChartAsync(Celebrity celebrity)
    {
        int generation = ++_loadGeneration;
        _loadCancel?.Cancel();
        var cancel = _loadCancel = new CancellationTokenSource();
        SetBusy(calculating: true);
        StatusMessage = $"Calculating chart for {celebrity.Name}…";
        try
        {
            // The chart and its birth-time check together, both with the settings in
            // force now. The check alone recalculates the chart some hundreds of times.
            var charts = _charts.With(_charts.Settings);
            var (chart, sensitivity) = await Task.Run(() =>
            {
                var calculated = charts.Calculate(celebrity);
                cancel.Token.ThrowIfCancellationRequested();
                try
                {
                    // The lunations and stations around the birth, for the Worksheet.
                    calculated.Events = NatalEventsService.Compute(charts, calculated);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"Events around the birth of chart {celebrity.Id} could not be found: {ex}");
                }
                try
                {
                    return (calculated, ChartViewModel.Analyse(charts, calculated, cancel.Token));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Diagnostics.Log($"Birth-time check for chart {celebrity.Id} failed: {ex}");
                    return (calculated, (TimeSensitivity?)null);
                }
            });
            if (generation != _loadGeneration) return; // superseded by a newer selection
            // The inventory first: its reading is then in hand when the Report is written.
            // It takes the scan of the birth day too, which says (for a chart with no
            // birth time) which signs are in doubt, as the Report is told below.
            InventoryVM.ShowChart(chart, sensitivity);
            ChartVM.Show(chart, sensitivity);
            DailyVM.Chart = chart;
            ForecastVM.Chart = chart;
            TimingVM.Chart = chart;
            SynastryVM.Chart = chart;
            StatusMessage = chart.EphemerisNote; // empty unless the ephemeris data is missing
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer selection
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
            {
                // The chart of whoever was selected before must not stay on screen as
                // if it were this person's.
                ShowNoChart();
                StatusMessage = $"Chart error: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _loadGeneration)
                SetBusy(calculating: false);
        }
    }
}
