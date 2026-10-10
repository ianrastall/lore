using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using System.Collections.ObjectModel;

namespace Lore.ViewModels;

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
        ForecastVM = new ForecastViewModel(transits, dailyInterpreter);
        TimingVM = new TimingViewModel(new TimingService(charts, dailyInterpreter.HouseTopic), cities);
        SynastryVM = new SynastryViewModel(charts, synastryInterpreter, davisonInterpreter);
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
            StatusMessage = _userCharts.LoadProblem ?? $"{_all.Count} people loaded.";

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

    // The latest pass of RebuildCoreAsync to have been started.
    private Task? _rebuilding;

    // Rebuilds the list of people and their verdicts, keeping whoever is selected
    // selected. Of several passes under way at once only the latest fills the list in,
    // so each caller waits for that one: when this returns, the list holds whatever the
    // caller had just saved or changed.
    private async Task RebuildPoolAsync()
    {
        _rebuilding = RebuildCoreAsync();
        Task latest;
        do
        {
            latest = _rebuilding;
            await latest;
        }
        while (!ReferenceEquals(latest, _rebuilding));
    }

    private async Task RebuildCoreAsync()
    {
        int generation = ++_poolGeneration;
        var all = _all = [.. _celebrities.All, .. _userCharts.Charts];
        SynastryVM.SetPeople(all);
        // Scoring runs the native Swiss Ephemeris calculation for every chart in the
        // library (over 1,300), so it is kept off the UI thread, and it is done with
        // the settings in force now even if they are changed before it finishes.
        var charts = _charts.With(_charts.Settings);
        var verdicts = await Task.Run(() => ComputeVerdicts(charts, all));
        // A newer pass has started, with the newer list and settings, and it will fill
        // the list in. Replacing the list here as well would only clear the selection
        // from under it, leaving the chart on screen as it was before the change.
        if (generation != _poolGeneration) return;

        // Written here, on the UI thread: the list bindings read these values when the
        // rows are next built, just below.
        foreach (var (person, colorHex, label) in verdicts)
        {
            person.VerdictColorHex = colorHex;
            person.VerdictLabel = label;
        }
        // Replacing the list clears the selection, so it is put back in the same breath:
        // taken now rather than at the start, in case someone else was picked while the
        // verdicts were worked out.
        string? selectedId = SelectedCelebrity?.Id;
        RefreshCategories();
        ApplyFilter();
        Reselect(selectedId);
    }

    // Selects the person with this Id as the list now has them: the same person again
    // after the list was replaced, or the new record after an edit. Nobody, if the list
    // as filtered no longer shows them.
    private void Reselect(string? id)
    {
        if (id is null) return;
        var again = DisplayedCelebrities.FirstOrDefault(c => c.Id == id);
        if (!ReferenceEquals(SelectedCelebrity, again)) SelectedCelebrity = again;
    }

    // Score every chart so the browse list can highlight the notable ones. Only
    // Extraordinary/Alarming get a coloured bar; Ordinary stays clear so the outliers
    // stand out. Runs on a background thread (see RebuildPoolAsync).
    private static List<(Celebrity Person, string ColorHex, string Label)> ComputeVerdicts(
        ChartService charts, List<Celebrity> all)
    {
        var verdicts = new List<(Celebrity, string, string)>(all.Count);
        foreach (var c in all)
        {
            try
            {
                // No birth time, no score (see DignityService.ComputeIfTimed).
                if (DignityService.ComputeIfTimed(charts.Calculate(c)) is not { } score)
                {
                    verdicts.Add((c, "#00000000", ""));
                    continue;
                }
                // Translucent wash for the whole row: green (Extraordinary) / red
                // (Alarming), transparent for Ordinary so the notable rows stand out.
                string colorHex = score.Verdict switch
                {
                    Models.ChartVerdict.Extraordinary => "#553FB84F",
                    Models.ChartVerdict.Alarming      => "#55E5534B",
                    _                                 => "#00000000",
                };
                verdicts.Add((c, colorHex, $"Dignity {score.Total:+#;-#;0} · {score.Verdict.Label()}"));
            }
            catch
            {
                verdicts.Add((c, "#00000000", ""));
            }
        }
        return verdicts;
    }

    // First entry in the category dropdown: clears the filter to show everyone.
    public const string AllCategories = "All Categories";

    private void RefreshCategories()
    {
        var cats = _all.Select(c => c.Category).Distinct().ToList();
        // "All Categories" first, then My Charts (if any), then the rest alphabetically.
        var ordered = new List<string> { AllCategories };
        ordered.AddRange(cats.Where(c => c == UserChartService.MyChartsCategory));
        ordered.AddRange(cats.Where(c => c != UserChartService.MyChartsCategory).OrderBy(c => c));
        Categories = new ObservableCollection<string>(ordered);
        if (string.IsNullOrEmpty(SelectedCategory))
            SelectedCategory = AllCategories;
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

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Celebrity> q = _all;

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != AllCategories)
            q = q.Where(c => c.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim();
            q = q.Where(c =>
                c.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                c.BirthPlace.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        DisplayedCelebrities = new ObservableCollection<Celebrity>(q.OrderBy(c => c.Name));
    }

    [RelayCommand]
    private void ClearCategory() => SelectedCategory = AllCategories;

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

    public async Task AddCustomChartAsync(Celebrity chart)
    {
        if (!await TrySaveAsync(() => _userCharts.AddAsync(chart), $"add {chart.Name}")) return;
        await RebuildPoolAsync();
        ShowSaved(chart);
    }

    // Saves an edited chart (same Id) and shows it again, recalculated.
    public async Task UpdateCustomChartAsync(Celebrity chart)
    {
        if (!await TrySaveAsync(() => _userCharts.UpdateAsync(chart), $"save changes to {chart.Name}")) return;
        await RebuildPoolAsync();
        ShowSaved(chart);
        StatusMessage = $"Saved changes to {chart.Name}.";
    }

    // Selects a chart that has just been saved. A search still in the box may not match
    // it (it was typed to find someone else, or the name it found has just been changed),
    // and must not hide the chart: the search is cleared if it would.
    private void ShowSaved(Celebrity chart)
    {
        SelectedCategory = UserChartService.MyChartsCategory;
        if (!DisplayedCelebrities.Any(c => c.Id == chart.Id))
            SearchText = "";
        Reselect(chart.Id);
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

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedCelebrity is not { } c || c.Category != UserChartService.MyChartsCategory)
            return;
        if (!await TrySaveAsync(() => _userCharts.RemoveAsync(c), $"delete {c.Name}")) return;
        SelectedCelebrity = null;
        ShowNoChart();
        // The answers given for this person go with their chart.
        _inventoryStore?.Remove(c.Id);
        await RebuildPoolAsync();
        StatusMessage = $"Deleted {c.Name}.";
    }

    // ── My Charts as a file, for moving to another PC ─────────────────────────

    // The saved charts as they are in the file now, and how many (see UserChartService).
    public Task<(byte[] Bytes, int Count)> ExportMyChartsAsync() => Task.Run(_userCharts.ExportAsync);

    // Merges a saved copy of My Charts into this PC's, then shows the result.
    public async Task ImportMyChartsAsync(string path)
    {
        UserChartService.ImportResult result;
        try
        {
            // Off the UI thread: the file is read, checked and merged chart by chart.
            result = await Task.Run(() => _userCharts.ImportAsync(path));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't bring in {Path.GetFileName(path)}: {ex.Message}";
            Diagnostics.Log($"My Charts import failed ({path}): {ex}");
            return;
        }

        if (result.Added + result.Replaced == 0)
        {
            StatusMessage = $"{Path.GetFileName(path)} has no charts of your own in it; nothing was changed.";
            return;
        }

        bool keepCategory = SelectedCelebrity is not null && !SelectedIsCustom;
        await RebuildPoolAsync();
        string? selectedId = SelectedCelebrity?.Id;
        if (!keepCategory) SelectedCategory = UserChartService.MyChartsCategory;
        Reselect(selectedId);
        if (SelectedCelebrity is { } selected)
            await LoadChartAsync(selected);

        static string Charts(int n) => n == 1 ? "1 chart" : $"{n} charts";
        StatusMessage = $"Brought in {Charts(result.Added + result.Replaced)}: {result.Added} new" +
                        (result.Replaced > 0 ? $", {result.Replaced} replacing a chart already here" : "") +
                        (result.Skipped > 0 ? $"; {result.Skipped} other entries left out" : "") + ".";
    }

    // Runs a My Charts save, reporting a failure in the status bar instead of losing
    // it (the save itself never damages the existing file; see UserChartService).
    private async Task<bool> TrySaveAsync(Func<Task> save, string what)
    {
        try
        {
            await save();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't {what}: {ex.Message}";
            Diagnostics.Log($"My Charts save failed ({what}): {ex}");
            return false;
        }
    }

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
            InventoryVM.Chart = chart;
            ChartVM.Show(chart, sensitivity);
            DailyVM.Chart = chart;
            ForecastVM.Chart = chart;
            TimingVM.Chart = chart;
            SynastryVM.Chart = chart;
            InventoryVM.Chart = chart;
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
