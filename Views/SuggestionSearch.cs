using Microsoft.UI.Xaml.Controls;

namespace Lore.Views;

// Fills an AutoSuggestBox as its text is typed, without making the typing wait: the
// search starts after a short pause, runs off the UI thread, and is dropped if another
// keystroke has come in meanwhile. One of these per box (or per dialog, where only one
// box can be typed in at a time).
internal sealed class SuggestionSearch
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    private CancellationTokenSource? _searching;

    public async Task RunAsync<T>(AutoSuggestBox box, Func<string, IReadOnlyList<T>> search)
    {
        _searching?.Cancel();
        var mine = _searching = new CancellationTokenSource();
        string query = box.Text;
        try
        {
            await Task.Delay(Pause, mine.Token);
            var results = await Task.Run(() => search(query), mine.Token);
            if (!mine.IsCancellationRequested)
                box.ItemsSource = results;
        }
        catch (OperationCanceledException)
        {
            // superseded by a later keystroke
        }
    }

    // The box has been answered (a suggestion taken, Enter pressed): a search still on
    // its way must not reopen the list.
    public void Cancel() => _searching?.Cancel();
}
