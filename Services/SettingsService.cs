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
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore"))
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
    public sealed record UiState(string? LastChartId = null, string LastView = "Chart");

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

    public void SaveUi(UiState ui)
    {
        try
        {
            File.WriteAllText(UiPath, JsonSerializer.Serialize(ui, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {UiPath}: {ex.Message}");
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

    public void SaveHome(HomePlace? home)
    {
        try
        {
            if (home is null) File.Delete(HomePath);
            else File.WriteAllText(HomePath, JsonSerializer.Serialize(home, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {HomePath}: {ex.Message}");
        }
    }

    public void Save(ChartSettings settings)
    {
        try
        {
            // Written beside the real file and then swapped in, so a crash mid-write
            // cannot leave half a settings file behind.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOpts));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {_path}: {ex.Message}");
        }
    }
}
