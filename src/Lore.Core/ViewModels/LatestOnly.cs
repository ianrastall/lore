namespace Lore.ViewModels;

// One slot for a view model's background calculation, of which only the latest asked
// for counts. Every reading is worked out off the window's thread and may be overtaken
// before it is done (another chart picked, another date, a setting changed); each view
// model once kept its own counter and its own cancellation for that, and the copies had
// drifted apart. This is the one way of doing it:
//   • asking again cancels the calculation before, which stops at its next check;
//   • a result, a failure or the end of "busy" is delivered only for the latest request,
//     so nothing stale is ever shown and the progress ring is not switched off by a
//     calculation that has been replaced;
//   • everything is delivered on the thread that asked (the window's).
public sealed class LatestOnly
{
    private int _generation;
    private CancellationTokenSource? _cancel;

    // Nothing is to be calculated for the state the view model is now in: whatever is
    // running is stopped and its result dropped. The caller clears its own busy flag.
    public void Cancel()
    {
        _generation++;
        _cancel?.Cancel();
        _cancel = null;
    }

    // Runs `work` on a background thread. `busy(true)` is called at once; then, unless a
    // newer Run or Cancel has overtaken this one, `done` with the result or `failed`
    // with what went wrong, and `busy(false)`.
    public async Task Run<T>(Func<CancellationToken, T> work, Action<bool> busy, Action<T> done, Action<Exception> failed)
    {
        int generation = ++_generation;
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        busy(true);
        try
        {
            var result = await Task.Run(() => work(cancel.Token));
            if (generation == _generation) done(result);
        }
        catch (OperationCanceledException)
        {
            // overtaken by a newer request
        }
        catch (Exception ex)
        {
            if (generation == _generation) failed(ex);
        }
        finally
        {
            if (generation == _generation) busy(false);
        }
    }
}
