using Lore.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lore.Services;

// Persists the calculation settings (house system, node type) to
// %LOCALAPPDATA%\Lore\settings.json. A missing or unreadable file just means defaults.
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsService()
        : this(AppFolder.Path)
    {
    }

    // A different folder, for tests.
    public SettingsService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public ChartSettings Load()
    {
        try
        {
            if (File.Exists(_path) &&
                JsonSerializer.Deserialize<ChartSettings>(File.ReadAllText(_path), JsonOpts) is { } s &&
                Enum.IsDefined(s.Houses) && Enum.IsDefined(s.Node) && Enum.IsDefined(s.Lilith))
                return s with { Orbs = (s.Orbs ?? OrbSettings.Lore).Clamped() };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {_path}: {ex.Message}");
        }
        return ChartSettings.Default;
    }

    // Where the user left off: which chart was open and in which view. Kept apart from
    // the calculation settings (ui.json beside settings.json), since it changes with
    // every click and matters far less.
    // AnswersShapeReadings: whether a person's inventory answers colour the wording of
    // their Report, Daily and Forecast readings (see InventoryViewModel.ShapeReadings).
    public sealed record UiState(string? LastChartId = null, string LastView = "Chart", bool AnswersShapeReadings = true);

    private string UiPath => Path.Combine(Path.GetDirectoryName(_path)!, "ui.json");

    public UiState LoadUi()
    {
        try
        {
            if (File.Exists(UiPath) && JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiPath), JsonOpts) is { } ui)
                return ui with { LastView = ui.LastView ?? "Chart" };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {UiPath}: {ex.Message}");
        }
        return new UiState();
    }

    // Saved with every chart picked and every change of view, so the writing is done
    // behind the scenes (see Queue) rather than while the click is being answered.
    public void SaveUi(UiState ui) => Queue(() => Write(UiPath, JsonSerializer.Serialize(ui, JsonOpts)));

    // The small files that change as Lore is used (ui.json, home.json) are written one
    // after another on a background thread, in the order asked for. Flush waits for
    // whatever is still to be written; the window calls it as it closes.
    private readonly object _queueGate = new();
    private Task _queue = Task.CompletedTask;

    private void Queue(Action write)
    {
        lock (_queueGate)
            _queue = _queue.ContinueWith(_ => write(), TaskScheduler.Default);
    }

    public void Flush()
    {
        Task pending;
        lock (_queueGate) pending = _queue;
        pending.Wait(TimeSpan.FromSeconds(2));
    }

    // Written beside the real file and then swapped in, so a crash mid-write cannot
    // leave half a file behind. Never throws: a setting that could not be saved is
    // logged and Lore carries on with it for this session.
    private static void Write(string path, string json)
    {
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {path}: {ex.Message}");
            try { File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    // Where the reader is, for sunrise, sunset and the planetary hours (home.json beside
    // settings.json). It is not a calculation setting: no chart depends on it. Null
    // until a city has been chosen, and null again once it is cleared.
    private string HomePath => Path.Combine(Path.GetDirectoryName(_path)!, "home.json");

    public HomePlace? LoadHome()
    {
        try
        {
            if (File.Exists(HomePath) && JsonSerializer.Deserialize<HomePlace>(File.ReadAllText(HomePath), JsonOpts) is { } home &&
                !string.IsNullOrWhiteSpace(home.Name) && home.Latitude is >= -90 and <= 90 && home.Longitude is >= -180 and <= 180)
                return home;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {HomePath}: {ex.Message}");
        }
        return null;
    }

    public void SaveHome(HomePlace? home) => Queue(() =>
    {
        if (home is not null)
        {
            Write(HomePath, JsonSerializer.Serialize(home, JsonOpts));
            return;
        }
        try
        {
            File.Delete(HomePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not remove {HomePath}: {ex.Message}");
        }
    });

    // Rare (a setting changed in the menu) and wanted on disk before anything else
    // happens, so this one is written at once.
    public void Save(ChartSettings settings) => Write(_path, JsonSerializer.Serialize(settings, JsonOpts));
}
