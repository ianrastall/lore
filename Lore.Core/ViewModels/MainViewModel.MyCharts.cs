using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lore.Models;
using Lore.Services;
using System.Collections.ObjectModel;

namespace Lore.ViewModels;

// The reader's own charts, and the inventory answers that go with them: adding, editing
// and deleting, saving a copy and bringing one in, and what has not reached the file yet.
public sealed partial class MainViewModel
{
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

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedCelebrity is not { } c || c.Category != UserChartService.MyChartsCategory)
            return;
        if (!await TrySaveAsync(() => _userCharts.RemoveAsync(c), $"delete {c.Name}")) return;
        SelectedCelebrity = null;
        ShowNoChart();
        // The answers given for this person go with their chart. If the answers file
        // cannot be changed just now they are still in it, and that is said: the store
        // keeps the deletion and tries it again with its next save.
        bool answersGone = _inventoryStore is null || await _inventoryStore.RemoveAsync(c.Id);
        await RebuildPoolAsync();
        StatusMessage = answersGone
            ? $"Deleted {c.Name}."
            : $"Deleted {c.Name}, but their inventory answers could not be removed from the answers file yet: " +
              $"{_inventoryStore?.Problem ?? "the file could not be changed."}";
    }

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

    // True while something answered (or deleted) in this window has not reached the file.
    public bool HasUnsavedAnswers => _inventoryStore is { State: not InventorySaveState.Saved };

    // Why the last save of them failed, for the window to say before it closes.
    public string? UnsavedAnswersProblem => _inventoryStore?.Problem;

    // Waits for the answers on their way to the file and, if that save failed, tries it
    // once more. True when everything is in the file.
    public async Task<bool> SaveOutstandingAnswersAsync()
    {
        if (_inventoryStore is not { } store) return true;
        if (await Task.Run(() => store.Flush())) return true;
        return await store.RetryAsync();
    }
}
