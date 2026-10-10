using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Keeps the answers people have given to the personality inventory, by the Id of the
// saved chart they belong to, in %LOCALAPPDATA%\Lore\inventories.json. A file of its own
// on purpose: the answers are more private than a birth date, and kept apart from
// mycharts.json they are never part of a My Charts export. Nothing here leaves the PC.
//
// Answers are saved as they are given, so a sitting can be left and taken up again.
// Each write is of one person's sittings only, read into the file as it then stands, so
// two Lore windows open on different people do not undo each other.
public sealed class InventoryStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, List<InventorySitting>> _all;

    // Writes go one after another on a background thread, in the order asked for; Flush
    // waits for them (the main window calls it on closing, and the tests call it).
    private Task _queue = Task.CompletedTask;

    public InventoryStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore"))
    {
    }

    // A different folder, for tests.
    public InventoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "inventories.json");
        _all = Read() ?? [];
    }

    public string FilePath => _path;

    // The sittings saved for a chart, oldest first; empty if there are none. A copy: the
    // caller changes it and hands it back to Save.
    public List<InventorySitting> Get(string chartId)
    {
        lock (_gate)
        {
            return _all.TryGetValue(chartId, out var sittings)
                ? sittings.Select(s => new InventorySitting { Started = s.Started, Completed = s.Completed, Answers = [.. s.Answers] }).ToList()
                : [];
        }
    }

    // Replaces what is saved for a chart; an empty list removes it altogether.
    public void Save(string chartId, IReadOnlyList<InventorySitting> sittings)
    {
        var copy = sittings.Select(s => new InventorySitting { Started = s.Started, Completed = s.Completed, Answers = [.. s.Answers] }).ToList();
        lock (_gate)
        {
            if (copy.Count == 0) _all.Remove(chartId); else _all[chartId] = copy;
            _queue = _queue.ContinueWith(_ => Write(chartId, copy), TaskScheduler.Default);
        }
    }

    public void Remove(string chartId) => Save(chartId, []);

    public void Flush()
    {
        Task pending;
        lock (_gate) pending = _queue;
        try { pending.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }

    // Null if there is no file. A file that cannot be understood is set aside under a
    // dated name, never written over, and the store starts empty.
    private Dictionary<string, List<InventorySitting>>? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, List<InventorySitting>>>(File.ReadAllText(_path), JsonOpts);
            if (read is not null && read.Values.All(v => v is not null && v.TrueForAll(s => s?.Answers is not null)))
                return read;
            throw new JsonException("not a list of sittings by chart");
        }
        catch (JsonException ex)
        {
            string keptAs = Path.Combine(Path.GetDirectoryName(_path)!, $"inventories.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(_path, keptAs); }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException) { }
            Diagnostics.Log($"The inventory answers file could not be understood ({ex.Message}); kept as {keptAs}.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use by something else: leave it be. What is saved from here on is
            // merged into it when it can next be read.
            Diagnostics.Log($"The inventory answers file could not be opened: {ex.Message}");
            return null;
        }
    }

    // One chart's sittings, put into the file as it stands now and written back whole,
    // to a temporary file first so that a crash mid-write leaves the old one intact.
    private void Write(string chartId, List<InventorySitting> sittings)
    {
        try
        {
            Dictionary<string, List<InventorySitting>> onDisk = [];
            if (File.Exists(_path))
            {
                try
                {
                    onDisk = JsonSerializer.Deserialize<Dictionary<string, List<InventorySitting>>>(File.ReadAllText(_path), JsonOpts) ?? [];
                }
                catch (JsonException)
                {
                    lock (_gate) onDisk = new(_all);
                }
            }
            if (sittings.Count == 0) onDisk.Remove(chartId); else onDisk[chartId] = sittings;

            string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(onDisk, JsonOpts));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"The inventory answers could not be saved: {ex.Message}");
        }
    }
}
