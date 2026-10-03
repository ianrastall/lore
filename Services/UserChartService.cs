using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Persists user-entered charts (reusing the Celebrity model) to
// %LOCALAPPDATA%\Lore\mycharts.json so they survive across sessions.
//
// This is the one file holding the user's own (often family) data, so it is
// written defensively:
//   • a save goes to mycharts.json.tmp first and only then replaces the real file,
//     so a crash or power cut mid-save leaves the previous version intact;
//   • each save keeps the version it replaces as mycharts.json.bak;
//   • a file that cannot be read is never overwritten: it is set aside under a
//     dated name and the backup is tried instead, and LoadProblem says what happened.
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

    public UserChartService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore"))
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
    private string TempPath => _path + ".tmp";

    // Set when loading needed a recovery step, for the status bar; null otherwise.
    public string? LoadProblem { get; private set; }

    public async Task LoadAsync()
    {
        LoadProblem = null;

        // No main file but a backup: a save was interrupted between its two steps.
        if (!File.Exists(_path))
        {
            _charts = File.Exists(BackupPath) ? await TryReadAsync(BackupPath) ?? [] : [];
            return;
        }

        if (await TryReadAsync(_path) is { } charts)
        {
            _charts = charts;
            return;
        }

        // Unreadable. Keep it (it may still be recoverable by hand) and never write
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

    public async Task AddAsync(Celebrity chart)
    {
        _charts.Add(chart);
        await SaveAsync();
    }

    // Replaces the saved chart with the same Id (an edit); adds it if none matches.
    public async Task UpdateAsync(Celebrity chart)
    {
        int i = _charts.FindIndex(c => c.Id == chart.Id);
        if (i >= 0) _charts[i] = chart; else _charts.Add(chart);
        await SaveAsync();
    }

    public async Task RemoveAsync(Celebrity chart)
    {
        _charts.RemoveAll(c => c.Id == chart.Id);
        await SaveAsync();
    }

    private static async Task<List<Celebrity>?> TryReadAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<Celebrity>>(stream, JsonOpts) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {path}: {ex.Message}");
            return null;
        }
    }

    private async Task SaveAsync()
    {
        if (_saveBlocked)
            throw new IOException("Saving is paused because the existing charts file couldn't be read; restart Lore.");

        // Write the new version completely (and to disk) before touching the old one.
        await using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write,
                         FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _charts, JsonOpts);
        }

        if (!File.Exists(_path))
        {
            File.Move(TempPath, _path);
            return;
        }

        try
        {
            // One step on NTFS: the new file takes the name, the old one becomes .bak.
            File.Replace(TempPath, _path, BackupPath);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            File.Copy(_path, BackupPath, overwrite: true);
            File.Move(TempPath, _path, overwrite: true);
        }
    }
}
