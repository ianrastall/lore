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

    private List<Celebrity> _all = [];

    public ChartViewModel ChartVM { get; }

    // Exposed so the Add-Chart dialog (owned by the window) can offer city autocomplete.
    public CityService Cities { get; }

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

    // Full-page Legend / reference view over the chart+report body.
    [ObservableProperty]
    public partial bool ShowLegend { get; set; }

    // True when the selected entry is a user-created chart (so it can be deleted).
    public bool SelectedIsCustom => SelectedCelebrity?.Category == UserChartService.MyChartsCategory;

    public MainViewModel(
        CelebrityService celebrities,
        ChartService charts,
        ChartInterpreter interpreter,
        UserChartService userCharts,
        CityService cities)
    {
        _celebrities = celebrities;
        _charts = charts;
        _userCharts = userCharts;
        Cities = cities;
        ChartVM = new ChartViewModel(interpreter);
    }

    public async Task InitializeAsync(string dataPath, string citiesPath)
    {
        IsLoading = true;
        StatusMessage = "Loading data…";
        try
        {
            Diagnostics.Log($"Init: dataPath={dataPath} exists={File.Exists(dataPath)}; " +
                            $"citiesPath={citiesPath} exists={File.Exists(citiesPath)}");
            await _celebrities.LoadAsync(dataPath);
            await _userCharts.LoadAsync();
            await Cities.LoadAsync(citiesPath);
            RebuildPool();
            StatusMessage = $"{_all.Count} people loaded.";
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

    private void RebuildPool()
    {
        _all = [.. _celebrities.All, .. _userCharts.Charts];
        ComputeVerdicts();
        RefreshCategories();
        ApplyFilter();
    }

    // Score every chart so the browse list can highlight the notable ones. Only
    // Extraordinary/Alarming get a coloured bar; Ordinary stays clear so the outliers
    // stand out. Runs once at load (fast) before the displayed list is built, since the
    // list bindings read these values at item realisation.
    private void ComputeVerdicts()
    {
        foreach (var c in _all)
        {
            try
            {
                var score = DignityService.Compute(_charts.Calculate(c));
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

    public async Task AddCustomChartAsync(Celebrity chart)
    {
        await _userCharts.AddAsync(chart);
        RebuildPool();
        SelectedCategory = UserChartService.MyChartsCategory;
        SelectedCelebrity = DisplayedCelebrities.FirstOrDefault(c => c.Id == chart.Id);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedCelebrity is not { } c || c.Category != UserChartService.MyChartsCategory)
            return;
        await _userCharts.RemoveAsync(c);
        SelectedCelebrity = null;
        ChartVM.Chart = null;
        RebuildPool();
    }

    private async Task LoadChartAsync(Celebrity celebrity)
    {
        IsLoading = true;
        StatusMessage = $"Calculating chart for {celebrity.Name}…";
        try
        {
            var chart = await Task.Run(() => _charts.Calculate(celebrity));
            ChartVM.Chart = chart;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chart error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
