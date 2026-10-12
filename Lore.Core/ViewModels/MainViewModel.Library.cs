using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using System.Collections.ObjectModel;

namespace Lore.ViewModels;

// The list of people down the side of the window: the bundled figures and the reader's
// own charts together, scored for the coloured rows, and filtered by category and search.
public sealed partial class MainViewModel
{
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
        _poolCancel?.Cancel();
        var cancel = _poolCancel = new CancellationTokenSource();
        var all = _all = [.. _celebrities.All, .. _userCharts.Charts];
        SynastryVM.SetPeople(all);
        // Scoring runs the native Swiss Ephemeris calculation for every chart in the
        // library (over 1,300), so it is kept off the UI thread, and it is done with
        // the settings in force now even if they are changed before it finishes.
        var charts = _charts.With(_charts.Settings);
        List<(Celebrity Person, string ColorHex, string Label)> verdicts;
        try
        {
            verdicts = await Task.Run(() => ComputeVerdicts(charts, all, cancel.Token));
        }
        catch (OperationCanceledException)
        {
            return; // overtaken: the newer pass scores the list
        }
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
        ChartService charts, List<Celebrity> all, CancellationToken cancel = default)
    {
        var verdicts = new List<(Celebrity, string, string)>(all.Count);
        foreach (var c in all)
        {
            cancel.ThrowIfCancellationRequested();
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
}
