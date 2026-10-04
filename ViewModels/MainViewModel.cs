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

    // Bumped per chart load; a calculation that finishes after a newer selection has
    // started is dropped instead of overwriting the newer chart.
    private int _loadGeneration;

    // Exposed so the Add-Chart dialog (owned by the window) can offer city autocomplete.
    public CityService Cities { get; }

    // Exposed alongside Cities so the Add-Chart dialog can also offer hospital search
    // (a precise birthplace: hospitals are where people are born).
    public HospitalService Hospitals { get; }

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
        SettingsService? settings = null)
    {
        _settings = settings;
        _celebrities = celebrities;
        _charts = charts;
        _userCharts = userCharts;
        Cities = cities;
        Hospitals = hospitals;
        ChartVM = new ChartViewModel(interpreter, charts);
        DailyVM = new DailyViewModel(transits, dailyInterpreter);
        ForecastVM = new ForecastViewModel(transits, dailyInterpreter);
        TimingVM = new TimingViewModel(new TimingService(charts));
        SynastryVM = new SynastryViewModel(charts, synastryInterpreter);
    }

    public async Task InitializeAsync(string dataPath, string citiesPath, string hospitalsPath)
    {
        IsLoading = true;
        StatusMessage = "Loading data…";
        try
        {
            Diagnostics.Log($"Init: dataPath={dataPath} exists={File.Exists(dataPath)}; " +
                            $"citiesPath={citiesPath} exists={File.Exists(citiesPath)}; " +
                            $"hospitalsPath={hospitalsPath} exists={File.Exists(hospitalsPath)}");
            await _celebrities.LoadAsync(dataPath);
            await _userCharts.LoadAsync();
            await Cities.LoadAsync(citiesPath);
            await Hospitals.LoadAsync(hospitalsPath);
            await RebuildPoolAsync();
            StatusMessage = _userCharts.LoadProblem ?? $"{_all.Count} people loaded.";

            // Pick up where the last session left off.
            if (_settings is not null)
            {
                _ui = _settings.LoadUi();
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
            IsLoading = false;
        }
    }

    private async Task RebuildPoolAsync()
    {
        _all = [.. _celebrities.All, .. _userCharts.Charts];
        SynastryVM.SetPeople(_all);
        // Scoring runs the native Swiss Ephemeris calculation for every chart (150+),
        // so keep it off the UI thread. It only writes plain string fields on each
        // Celebrity, which the list bindings read afterwards at item realisation.
        await Task.Run(ComputeVerdicts);
        RefreshCategories();
        ApplyFilter();
    }

    // Score every chart so the browse list can highlight the notable ones. Only
    // Extraordinary/Alarming get a coloured bar; Ordinary stays clear so the outliers
    // stand out. Runs on a background thread (see RebuildPoolAsync) before the displayed
    // list is built, since the list bindings read these values at item realisation.
    private void ComputeVerdicts()
    {
        foreach (var c in _all)
        {
            try
            {
                // No birth time, no score (see DignityService.ComputeIfTimed).
                if (DignityService.ComputeIfTimed(_charts.Calculate(c)) is not { } score)
                {
                    c.VerdictColorHex = "#00000000";
                    c.VerdictLabel = "";
                    continue;
                }
                // Translucent wash for the whole row: green (Extraordinary) / red
                // (Alarming), transparent for Ordinary so the notable rows stand out.
                c.VerdictColorHex = score.Verdict switch
                {
                    Models.ChartVerdict.Extraordinary => "#553FB84F",
                    Models.ChartVerdict.Alarming      => "#55E5534B",
                    _                                 => "#00000000",
                };
                c.VerdictLabel = $"Dignity {score.Total:+#;-#;0} · {score.Verdict.Label()}";
            }
            catch
            {
                c.VerdictColorHex = "#00000000";
                c.VerdictLabel = "";
            }
        }
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

    // The house system and node type in force (see ChartSettings).
    public ChartSettings Settings => _charts.Settings;

    // Changes a calculation setting: saved, then everything on screen is recalculated
    // with it — the browse list's dignity verdicts and the chart being shown.
    public async Task ApplySettingsAsync(ChartSettings settings)
    {
        if (settings == _charts.Settings) return;
        _charts.Settings = settings;
        _settings?.Save(settings);

        string? selectedId = SelectedCelebrity?.Id;
        await RebuildPoolAsync();
        if (selectedId is not null &&
            DisplayedCelebrities.FirstOrDefault(c => c.Id == selectedId) is { } again)
        {
            SelectedCelebrity = again;
            await LoadChartAsync(again);
        }
        StatusMessage = $"Now using {settings.Houses.Name()} houses, the {settings.Node.Name().ToLowerInvariant()}, " +
                        $"and {settings.Orbs.PresetName.ToLowerInvariant()} orbs.";
    }

    public async Task AddCustomChartAsync(Celebrity chart)
    {
        if (!await TrySaveAsync(() => _userCharts.AddAsync(chart), $"add {chart.Name}")) return;
        await RebuildPoolAsync();
        SelectedCategory = UserChartService.MyChartsCategory;
        SelectedCelebrity = DisplayedCelebrities.FirstOrDefault(c => c.Id == chart.Id);
    }

    // Saves an edited chart (same Id) and shows it again, recalculated.
    public async Task UpdateCustomChartAsync(Celebrity chart)
    {
        if (!await TrySaveAsync(() => _userCharts.UpdateAsync(chart), $"save changes to {chart.Name}")) return;
        await RebuildPoolAsync();
        SelectedCategory = UserChartService.MyChartsCategory;
        SelectedCelebrity = DisplayedCelebrities.FirstOrDefault(c => c.Id == chart.Id);
        StatusMessage = $"Saved changes to {chart.Name}.";
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedCelebrity is not { } c || c.Category != UserChartService.MyChartsCategory)
            return;
        if (!await TrySaveAsync(() => _userCharts.RemoveAsync(c), $"delete {c.Name}")) return;
        SelectedCelebrity = null;
        ChartVM.Chart = null;
        DailyVM.Chart = null;
        ForecastVM.Chart = null;
        TimingVM.Chart = null;
        SynastryVM.Chart = null;
        await RebuildPoolAsync();
        StatusMessage = $"Deleted {c.Name}.";
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
        IsLoading = true;
        StatusMessage = $"Calculating chart for {celebrity.Name}…";
        try
        {
            var chart = await Task.Run(() => _charts.Calculate(celebrity));
            if (generation != _loadGeneration) return; // superseded by a newer selection
            ChartVM.Chart = chart;
            DailyVM.Chart = chart;
            ForecastVM.Chart = chart;
            TimingVM.Chart = chart;
            SynastryVM.Chart = chart;
            StatusMessage = chart.EphemerisNote; // empty unless the ephemeris data is missing
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
                StatusMessage = $"Chart error: {ex.Message}";
        }
        finally
        {
            if (generation == _loadGeneration)
                IsLoading = false;
        }
    }
}
