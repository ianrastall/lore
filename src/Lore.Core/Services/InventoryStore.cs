using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Whether the answers given in this window have reached the file: all of them; some are
// on their way; or the last attempt failed and they are kept here to be tried again.
public enum InventorySaveState { Saved, Pending, Failed }

// Keeps the answers people have given to the personality inventory, by the Id of the
// saved chart they belong to, in %LOCALAPPDATA%\Lore\inventories.json. A file of its own
// on purpose: the answers are more private than a birth date, and kept apart from
// mycharts.json they are never part of a My Charts export. Nothing here leaves the PC.
//
// Answers are saved as they are given, so a sitting can be left and taken up again. The
// file is treated as the saved charts are (see UserChartService):
//   • a save reads the file, puts this window's changes into it and writes it back while
//     holding inventories.json.lock, so two Lore windows saving at the same moment take
//     turns and neither undoes the other;
//   • each write is of the people changed here only, into the file as it then stands;
//   • a save goes to a temporary file first, and the version it replaces is kept as
//     inventories.json.bak;
//   • a file that cannot be understood is never written over: it is set aside under a
//     name of its own and the backup is restored, or, if it cannot be moved, nothing is
//     saved at all;
//   • a save that fails is not forgotten. What it was to write stays here, State says
//     so, and the next save (or Retry) writes it with whatever has been answered since.
public sealed class InventoryStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();

    // What this window takes the answers to be: the file as last read, with everything
    // answered here since. False for _loaded when the file could not be read at all.
    private Dictionary<string, List<InventorySitting>> _all = [];
    private bool _loaded;

    // The people whose answers here have not reached the file yet (an empty list: their
    // answers are deleted here and still in the file).
    private readonly Dictionary<string, List<InventorySitting>> _unsaved = [];

    // Writes go one after another on a background thread, in the order asked for; each
    // task says whether its write reached the file. _queued counts those not yet done.
    private Task<bool> _queue = Task.FromResult(true);
    private int _queued;

    private string? _saveProblem;
    private string? _loadProblem;

    // How long a save waits for another Lore window to finish its own.
    internal TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(5);

    public InventoryStore()
        : this(AppFolder.Path)
    {
    }

    // A different folder, for tests.
    public InventoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "inventories.json");
        Load();
    }

    public string FilePath => _path;
    private string BackupPath => _path + ".bak";
    private string LockPath => _path + ".lock";

    // Raised, on whatever thread, when State, Problem or IsReadable may have changed.
    public event Action? StatusChanged;

    public InventorySaveState State
    {
        get
        {
            lock (_gate)
                return _unsaved.Count == 0 ? InventorySaveState.Saved
                    : _queued > 0 ? InventorySaveState.Pending : InventorySaveState.Failed;
        }
    }

    // False while the file is there but could not be read: nobody's answers are known,
    // and none should be asked for until it can be (Get tries again each time).
    public bool IsReadable { get { lock (_gate) return _loaded; } }

    // What is wrong, in words for the reader: why the last save failed, or why the file
    // could not be read. Null when nothing is.
    public string? Problem
    {
        get
        {
            lock (_gate)
                return _unsaved.Count > 0 && _queued == 0 ? _saveProblem : !_loaded ? _loadProblem : null;
        }
    }

    // Set when a damaged file was set aside (and the backup restored, if there was one).
    public string? RecoveryNote { get; private set; }

    // The people whose answers here are not in the file yet.
    public IReadOnlyList<string> Unsaved { get { lock (_gate) return [.. _unsaved.Keys]; } }

    // The sittings saved for a chart, oldest first; empty if there are none. A copy: the
    // caller changes it and hands it back to Save.
    public List<InventorySitting> Get(string chartId)
    {
        bool reloaded = false;
        List<InventorySitting> result;
        lock (_gate)
        {
            if (!_loaded) reloaded = TryReload();
            result = _all.TryGetValue(chartId, out var sittings) ? Copy(sittings) : [];
        }
        if (reloaded) StatusChanged?.Invoke();
        return result;
    }

    // Replaces what is saved for a chart; an empty list removes it altogether. What is
    // shown in this window changes at once; the task says whether the file did too.
    public Task<bool> SaveAsync(string chartId, IReadOnlyList<InventorySitting> sittings)
    {
        var copy = Copy(sittings);
        Task<bool> written;
        lock (_gate)
        {
            if (copy.Count == 0) _all.Remove(chartId); else _all[chartId] = copy;
            _unsaved[chartId] = copy;
            written = Enqueue();
        }
        StatusChanged?.Invoke();
        return written;
    }

    public void Save(string chartId, IReadOnlyList<InventorySitting> sittings) => _ = SaveAsync(chartId, sittings);

    public Task<bool> RemoveAsync(string chartId) => SaveAsync(chartId, []);

    public void Remove(string chartId) => _ = RemoveAsync(chartId);

    // Tries again to write whatever a failed save left behind.
    public Task<bool> RetryAsync()
    {
        Task<bool> written;
        lock (_gate) written = Enqueue();
        StatusChanged?.Invoke();
        return written;
    }

    // Waits for the writes asked for so far (the main window calls it on closing, and
    // the tests call it). False if any answers given here are still not in the file:
    // the wait ran out, or the last save failed.
    public bool Flush(TimeSpan? wait = null)
    {
        Task pending;
        lock (_gate) pending = _queue;
        try { pending.Wait(wait ?? TimeSpan.FromSeconds(10)); }
        catch (AggregateException) { }
        lock (_gate) return _unsaved.Count == 0;
    }

    // Call holding _gate.
    private Task<bool> Enqueue()
    {
        _queued++;
        return _queue = _queue.ContinueWith(_ => WriteUnsaved(), TaskScheduler.Default);
    }

    private static List<InventorySitting> Copy(IEnumerable<InventorySitting> sittings) =>
        sittings.Select(s => new InventorySitting { Started = s.Started, Completed = s.Completed, Answers = [.. s.Answers] }).ToList();

    // ── Reading ───────────────────────────────────────────────────────────────

    // How reading the file went: there is none; it held a usable set of answers; it was
    // read and what it holds is damaged; or it could not be opened at all, which says
    // nothing about what it holds and must never be taken for damage.
    private enum ReadState { Missing, Ok, Invalid, Unavailable }

    private static (ReadState State, Dictionary<string, List<InventorySitting>>? Answers) ReadFile(string path, int attempts)
    {
        for (int attempt = 1; ; attempt++)
        {
            if (!File.Exists(path)) return (ReadState.Missing, null);
            try
            {
                var read = JsonSerializer.Deserialize<Dictionary<string, List<InventorySitting>>>(File.ReadAllText(path), JsonOpts);
                // Well-formed JSON is not enough: "null" or {"a":[null]} parse happily.
                if (read is not null && read.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null &&
                                                      p.Value.TrueForAll(s => s?.Answers is not null && s.Started is not null)))
                    return (ReadState.Ok, read);
                Diagnostics.Log($"{path} is valid JSON but not a list of sittings by chart.");
                return (ReadState.Invalid, null);
            }
            catch (JsonException ex)
            {
                Diagnostics.Log($"Could not read {path}: {ex.Message}");
                return (ReadState.Invalid, null);
            }
            catch (FileNotFoundException)
            {
                return (ReadState.Missing, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file held by something else is usually free again within a moment.
                if (attempt >= attempts)
                {
                    Diagnostics.Log($"Could not open {path}: {ex.Message}");
                    return (ReadState.Unavailable, null);
                }
            }
            Thread.Sleep(150);
        }
    }

    private const string InUse =
        "The inventory answers file is in use by another program, so the answers cannot be read or saved just now. " +
        "Nothing has been changed.";

    // At start-up. Another window may be in the middle of a save; if it does not finish
    // in a moment the file is read anyway (reading harms nothing), but a damaged file
    // is only set aside by the holder of the lock.
    private void Load()
    {
        using var held = StoreFile.TryLock(LockPath, TimeSpan.FromSeconds(1));
        var (state, read) = ReadFile(_path, attempts: 2);
        switch (state)
        {
            case ReadState.Ok:
                _all = read!;
                _loaded = true;
                break;

            case ReadState.Missing:
                // No main file but a backup: a save was interrupted between its two steps.
                _all = ReadFile(BackupPath, attempts: 2) is (ReadState.Ok, { } backup) ? backup : [];
                _loaded = true;
                break;

            case ReadState.Unavailable:
                _loadProblem = InUse;
                break;

            default:
                if (held is null)
                {
                    _loadProblem = "The inventory answers file couldn't be read, and another Lore window is busy with it. " +
                                   "It has been left untouched; close the other window and restart Lore.";
                }
                else if (SetAside() is { } restored)
                {
                    _all = restored;
                    _loaded = true;
                }
                else
                {
                    _loadProblem = "The inventory answers file couldn't be read or moved. It has been left untouched, " +
                                   "and no answers will be saved until that is put right.";
                }
                break;
        }
    }

    // The file was unreadable when this window opened; has it become readable since?
    // Without the lock, so nothing is moved or restored here. Call holding _gate.
    private bool TryReload()
    {
        var (state, read) = ReadFile(_path, attempts: 1);
        if (state is not (ReadState.Ok or ReadState.Missing)) return false;
        _all = Overlay(read ?? [], _unsaved);
        _loaded = true;
        _loadProblem = null;
        return true;
    }

    // Moves a damaged file out of the way under a name nothing else has, so that it can
    // still be recovered by hand, and gives the answers to carry on from: the backup's,
    // put back as the file, or none. Null (and nothing changed) if it cannot be moved.
    // Call holding the lock file.
    private Dictionary<string, List<InventorySitting>>? SetAside()
    {
        string keptAs = Path.Combine(Path.GetDirectoryName(_path)!,
            $"inventories.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
        try
        {
            File.Move(_path, keptAs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The inventory answers file is unreadable and could not be moved: {ex.Message}");
            return null;
        }

        if (ReadFile(BackupPath, attempts: 2) is (ReadState.Ok, { } backup))
        {
            try
            {
                File.Copy(BackupPath, _path);
                RecoveryNote = "The inventory answers file couldn't be read, so the backup from " +
                               $"{File.GetLastWriteTime(BackupPath):d MMM yyyy} was restored. " +
                               $"The unreadable file was kept as {Path.GetFileName(keptAs)}.";
                Diagnostics.Log($"Inventory answers unreadable; moved to {keptAs} and the backup restored.");
                return backup;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.Log($"The inventory answers backup could not be restored: {ex.Message}");
            }
        }
        RecoveryNote = "The inventory answers file couldn't be read and there was no usable backup. " +
                       $"It was kept, untouched, as {Path.GetFileName(keptAs)} in {Path.GetDirectoryName(_path)}.";
        Diagnostics.Log($"Inventory answers unreadable; moved to {keptAs}. No usable backup.");
        return [];
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    private static Dictionary<string, List<InventorySitting>> Overlay(
        Dictionary<string, List<InventorySitting>> onto, Dictionary<string, List<InventorySitting>> changes)
    {
        foreach (var (chartId, sittings) in changes)
        {
            if (sittings.Count == 0) onto.Remove(chartId); else onto[chartId] = sittings;
        }
        return onto;
    }

    // Everything not yet in the file, written in one go. Runs on the queue.
    private bool WriteUnsaved()
    {
        Dictionary<string, List<InventorySitting>> pending;
        lock (_gate) pending = new(_unsaved);

        string? problem = null;
        if (pending.Count > 0)
        {
            try
            {
                problem = Commit(pending);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = $"The inventory answers could not be saved ({ex.Message}).";
            }
            if (problem is not null) Diagnostics.Log(problem);
        }

        lock (_gate)
        {
            _queued--;
            if (problem is null)
            {
                // Anything answered again since this write began is still to be written.
                foreach (var (chartId, sittings) in pending)
                    if (_unsaved.TryGetValue(chartId, out var now) && ReferenceEquals(now, sittings))
                        _unsaved.Remove(chartId);
            }
            _saveProblem = problem;
        }
        StatusChanged?.Invoke();
        return problem is null;
    }

    // The changes, put into the file as it stands now and written back whole, all while
    // holding the lock file. Null when it is done; otherwise why it was not, in which
    // case nothing has been written.
    private string? Commit(Dictionary<string, List<InventorySitting>> pending)
    {
        using var held = StoreFile.TryLock(LockPath, LockWait);
        if (held is null)
            return "Another Lore window is busy with the inventory answers file, so the latest answers are not saved yet.";

        Dictionary<string, List<InventorySitting>> onDisk;
        var (state, read) = ReadFile(_path, attempts: 4);
        switch (state)
        {
            case ReadState.Ok:
                onDisk = read!;
                break;
            case ReadState.Missing:
                onDisk = ReadFile(BackupPath, attempts: 2) is (ReadState.Ok, { } backup) ? backup : [];
                break;
            case ReadState.Unavailable:
                return InUse.Replace("cannot be read or saved just now", "are not saved yet");
            default:
                // Readable when this window opened and not now. Never written over: set
                // aside, or nothing is saved.
                if (SetAside() is not { } restored)
                    return "The inventory answers file can no longer be read and could not be moved out of the way, " +
                           "so nothing is being saved over it. The answers given here are kept until it is put right.";
                onDisk = restored;
                break;
        }

        Overlay(onDisk, pending);

        string temp = StoreFile.TempPath(_path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, onDisk, JsonOpts);
            }
            StoreFile.Replace(temp, _path, BackupPath);
        }
        finally
        {
            StoreFile.DeleteTemp(temp);
        }

        // The file as it now is, with whatever has been answered here meanwhile: this
        // also brings in what other windows have saved.
        lock (_gate)
        {
            _all = Overlay(onDisk, _unsaved);
            _loaded = true;
            _loadProblem = null;
        }
        return null;
    }
}
