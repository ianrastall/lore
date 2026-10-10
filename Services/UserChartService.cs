using Lore.Models;
using System.Globalization;
using System.Text.Json;

namespace Lore.Services;

// Persists user-entered charts (reusing the Celebrity model) to
// %LOCALAPPDATA%\Lore\mycharts.json so they survive across sessions.
//
// This is the one file holding the user's own (often family) data, so it is
// written defensively:
//   • a save goes to a temporary file first and only then replaces the real file,
//     so a crash or power cut mid-save leaves the previous version intact;
//   • each save keeps the version it replaces as mycharts.json.bak;
//   • a file that cannot be read is never overwritten: at start-up it is set aside
//     under a dated name and the backup is tried instead (LoadProblem says what
//     happened), and later on a save is refused rather than made over it;
//   • a save reads the file, changes it and writes it back while holding
//     mycharts.json.lock, so two Lore windows saving at the same moment take turns.
public sealed class UserChartService
{
    public const string MyChartsCategory = "My Charts";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private List<Celebrity> _charts = [];
    private bool _saveBlocked;

    // One save at a time within this window; the lock file does the same between windows.
    private readonly SemaphoreSlim _saving = new(1, 1);

    // How long a save waits for another Lore window to finish its own.
    internal TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(5);

    // The most an imported file may hold. A real My Charts file is a few hundred bytes a
    // chart; anything near these sizes is some other file picked by mistake.
    public const long MaxImportBytes = 10 * 1024 * 1024;
    public const int MaxImportCharts = 10_000;

    public UserChartService()
        : this(AppFolder.Path)
    {
    }

    // A different folder, for tests.
    public UserChartService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "mycharts.json");
    }

    public IReadOnlyList<Celebrity> Charts => _charts;

    public string FilePath => _path;
    private string BackupPath => _path + ".bak";
    private string LockPath => _path + ".lock";

    // Set when loading needed a recovery step, for the status bar; null otherwise.
    public string? LoadProblem { get; private set; }

    public async Task LoadAsync()
    {
        LoadProblem = null;

        // Another window may be in the middle of a save. If it does not finish in time
        // the file is read anyway: reading harms nothing, and a save takes the lock itself.
        await using var held = await TryLockAsync();

        // No main file but a backup: a save was interrupted between its two steps.
        if (!File.Exists(_path))
        {
            _charts = File.Exists(BackupPath) ? await TryReadAsync(BackupPath) ?? [] : [];
            return;
        }

        var (state, charts) = await ReadAsync(_path);
        if (state == ReadState.Ok)
        {
            _charts = charts!;
            return;
        }

        // The file is there but could not be opened: another program has it (a backup
        // or sync tool, a virus scanner) or access was refused. That says nothing about
        // what is in it, so nothing is moved or restored; the next save reads it afresh.
        if (state == ReadState.Unavailable)
        {
            _charts = [];
            LoadProblem = "Your saved charts file is in use by another program, so your charts are not shown. " +
                          "Nothing has been changed; restart Lore in a moment to see them.";
            return;
        }

        // Its contents are damaged. Setting it aside and restoring the backup changes
        // files, which only the holder of the lock may do: without it another window may
        // be half-way through a save of its own.
        if (held is null)
        {
            _charts = [];
            LoadProblem = "Your saved charts file couldn't be read, and another Lore window is busy with it. " +
                          "It has been left untouched; close the other window and restart Lore.";
            return;
        }

        // Keep it (it may still be recoverable by hand) and never write
        // over it: move it aside, then fall back to the last good version.
        string keptAs = Path.Combine(Path.GetDirectoryName(_path)!,
            $"mycharts.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try
        {
            File.Move(_path, keptAs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Couldn't even move it (locked?). Leave everything exactly as it is and
            // refuse to save this session, rather than risk writing over it.
            _saveBlocked = true;
            _charts = [];
            LoadProblem = $"Your saved charts file couldn't be read or moved ({ex.Message}). " +
                          "It has been left untouched, and new charts won't be saved until Lore is restarted.";
            Diagnostics.Log($"My Charts file unreadable and could not be moved: {ex}");
            return;
        }
        Diagnostics.Log($"My Charts file unreadable; moved to {keptAs}");

        if (File.Exists(BackupPath) && await TryReadAsync(BackupPath) is { } backup)
        {
            _charts = backup;
            File.Copy(BackupPath, _path);
            LoadProblem = $"Your saved charts file couldn't be read, so the backup from " +
                          $"{File.GetLastWriteTime(BackupPath):d MMM yyyy} was restored. " +
                          $"The unreadable file was kept as {Path.GetFileName(keptAs)}.";
        }
        else
        {
            _charts = [];
            LoadProblem = $"Your saved charts file couldn't be read and there was no usable backup. " +
                          $"It was kept, untouched, as {Path.GetFileName(keptAs)} in {Path.GetDirectoryName(_path)}.";
        }
    }

    // Each change is made to a copy of the saved list, the copy is saved, and only then
    // does it become the list in memory. If the save fails, memory and disk both stay as they were — so a
    // failed delete can't quietly take effect the next time something else is saved.
    public Task AddAsync(Celebrity chart) => CommitAsync(list => list.Add(chart));

    // Replaces the saved chart with the same Id (an edit); adds it if none matches.
    public Task UpdateAsync(Celebrity chart) => CommitAsync(list =>
    {
        int i = list.FindIndex(c => c.Id == chart.Id);
        if (i >= 0) list[i] = chart; else list.Add(chart);
    });

    public Task RemoveAsync(Celebrity chart) => CommitAsync(list => list.RemoveAll(c => c.Id == chart.Id));

    // ── Moving charts to another PC ───────────────────────────────────────────

    // What an import did: charts new to this PC, charts that replaced one with the same
    // Id, and entries left out because they were not somebody's own chart.
    public sealed record ImportResult(int Added, int Replaced, int Skipped);

    // The saved charts as a file of their own, in the same form as mycharts.json, and how
    // many there are. Read from the file under the lock, as a save does, not taken from
    // memory: another Lore window may have saved charts since this one loaded. Throws if
    // the file cannot be read, rather than writing out an older copy as if it were whole.
    public async Task<(byte[] Bytes, int Count)> ExportAsync()
    {
        await _saving.WaitAsync();
        try
        {
            await using var held = await TryLockAsync()
                ?? throw new IOException("Another Lore window is busy saving charts; try again in a moment.");

            var charts = _charts;
            if (File.Exists(_path))
            {
                var (state, onDisk) = await ReadAsync(_path);
                charts = state switch
                {
                    ReadState.Ok => onDisk!,
                    ReadState.Unavailable => throw new IOException("The saved charts file is in use by another program; try again in a moment."),
                    _ => throw new InvalidDataException("The saved charts file can no longer be read. Restart Lore and it will restore the backup."),
                };
            }
            return (JsonSerializer.SerializeToUtf8Bytes(charts, JsonOpts), charts.Count);
        }
        finally
        {
            _saving.Release();
        }
    }

    // Merges a file written by ExportAsync (or a copy of mycharts.json) into the saved
    // charts. It is read with the same checks as the real file and saved the same way,
    // so a bad file changes nothing and the previous version is kept as the backup.
    // A chart with the same Id as one already here replaces it.
    public async Task<ImportResult> ImportAsync(string path)
    {
        if (new FileInfo(path).Length > MaxImportBytes)
            throw new InvalidDataException("That file is too large to be a list of Lore charts.");
        if (await TryReadAsync(path) is not { } incoming)
            throw new InvalidDataException("That file is not a list of Lore charts.");
        if (incoming.Count > MaxImportCharts)
            throw new InvalidDataException($"That file holds more than {MaxImportCharts:N0} charts, more than Lore brings in at once.");

        // Only people's own charts: anything else (a copy of the figure library, say)
        // would turn up twice in the list.
        var mine = incoming.Where(c => c.Category == MyChartsCategory).ToList();
        int added = 0, replaced = 0;
        if (mine.Count > 0)
            await CommitAsync(list =>
            {
                // Looked up by Id once, not searched for afresh with every chart.
                var at = new Dictionary<string, int>(list.Count);
                for (int i = 0; i < list.Count; i++) at[list[i].Id] = i;
                foreach (var chart in mine)
                {
                    if (at.TryGetValue(chart.Id, out int i)) { list[i] = chart; replaced++; }
                    else { at[chart.Id] = list.Count; list.Add(chart); added++; }
                }
            });
        return new ImportResult(added, replaced, incoming.Count - mine.Count);
    }

    private async Task CommitAsync(Action<List<Celebrity>> change)
    {
        await _saving.WaitAsync();
        try
        {
            if (_saveBlocked)
                throw new IOException("Saving is paused because the existing charts file couldn't be read; restart Lore.");

            await using var held = await TryLockAsync()
                ?? throw new IOException("Another Lore window is busy saving charts. Nothing was changed; try again in a moment.");

            // Start from what is on disk rather than from memory: a second Lore window may
            // have saved since this one loaded, and its charts must not be written over.
            List<Celebrity> candidate;
            if (File.Exists(_path))
            {
                // A file that was readable at start-up and is not now must not be saved
                // over, nor pushed onto the backup, which may be the last good copy.
                var (state, onDisk) = await ReadAsync(_path);
                if (state == ReadState.Unavailable)
                    throw new IOException(
                        "The saved charts file is in use by another program, so nothing was saved. Try again in a moment.");
                if (state == ReadState.Invalid)
                    throw new InvalidDataException(
                        "The saved charts file can no longer be read, so nothing was saved; the file and its backup " +
                        "are as they were. Restart Lore and it will set the file aside and restore the backup.");
                candidate = new List<Celebrity>(onDisk!);
            }
            else
            {
                candidate = new List<Celebrity>(_charts);
            }

            change(candidate);
            await SaveAsync(candidate);
            _charts = candidate;
        }
        finally
        {
            _saving.Release();
        }
    }

    // Holds mycharts.json.lock open, shared with nobody, until disposed. Null if another
    // window kept it for longer than LockWait (or the folder cannot be written to).
    private async Task<FileStream?> TryLockAsync()
    {
        var giveUp = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= giveUp)
                {
                    Diagnostics.Log($"Could not take {LockPath}: {ex.Message}");
                    return null;
                }
            }
            await Task.Delay(50);
        }
    }

    // How reading a chart file went: it held a usable list; it was read and what it holds
    // is damaged; or it could not be opened or read at all, which says nothing about what
    // it holds and must never be taken for damage.
    private enum ReadState { Ok, Invalid, Unavailable }

    // The list, or null whichever way it failed: for a file nothing depends on telling apart.
    private static async Task<List<Celebrity>?> TryReadAsync(string path) => (await ReadAsync(path)).Charts;

    private static async Task<(ReadState State, List<Celebrity>? Charts)> ReadAsync(string path)
    {
        // A file held by something else is usually free again within a moment.
        const int Attempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var charts = await JsonSerializer.DeserializeAsync<List<Celebrity>>(stream, JsonOpts);

                // Well-formed JSON is not enough: "null", "[null]" or "[{}]" parse happily and
                // would break the app later. Treat those as damaged too, so the caller sets
                // the file aside and falls back to the backup.
                if (charts is null || !charts.TrueForAll(IsUsable))
                {
                    Diagnostics.Log($"{path} is valid JSON but not a usable chart list.");
                    return (ReadState.Invalid, null);
                }
                return (ReadState.Ok, charts);
            }
            catch (JsonException ex)
            {
                Diagnostics.Log($"Could not read {path}: {ex.Message}");
                return (ReadState.Invalid, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    Diagnostics.Log($"Could not open {path}: {ex.Message}");
                    return (ReadState.Unavailable, null);
                }
            }
            await Task.Delay(150);
        }
    }

    // A chart Lore can show and calculate: it has an id, a name, a readable date and
    // (if given) time, coordinates on the globe, and a moment of birth that can be
    // worked out from them. The one test for every way a chart comes in: the file at
    // start-up, an imported file, and the re-read before each save.
    internal static bool IsUsable(Celebrity? c)
    {
        if (c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name)) return false;
        if (!double.IsFinite(c.Latitude) || Math.Abs(c.Latitude) > 90) return false;
        if (!double.IsFinite(c.Longitude) || Math.Abs(c.Longitude) > 180) return false;
        // No clock has ever been as much as eighteen hours from Greenwich.
        if (!double.IsFinite(c.UtcOffsetHours) || Math.Abs(c.UtcOffsetHours) > 18) return false;

        if (!DateOnly.TryParseExact(c.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date.Year < 2 || date.Year > 9998)
            return false;

        // A time, if there is one, must be readable; and a chart that says its time is
        // known must have one, or it would be drawn for noon with angles nobody recorded.
        bool hasTime = !string.IsNullOrEmpty(c.BirthTime);
        if (hasTime && !TimeOnly.TryParseExact(c.BirthTime, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            return false;
        if (c.BirthTimeKnown && !hasTime) return false;

        try
        {
            BirthTimeResolver.ToUtc(c);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    // Call holding the lock.
    private async Task SaveAsync(List<Celebrity> charts)
    {
        // A name of its own, so nothing else can be holding or half-writing it.
        string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Write the new version completely (and to disk) before touching the old one.
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, charts, JsonOpts);
            }

            if (!File.Exists(_path))
            {
                File.Move(temp, _path);
                return;
            }

            try
            {
                // One step on NTFS: the new file takes the name, the old one becomes .bak.
                File.Replace(temp, _path, BackupPath);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                File.Copy(_path, BackupPath, overwrite: true);
                File.Move(temp, _path, overwrite: true);
            }
        }
        finally
        {
            // Still there only if the save failed part-way.
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
