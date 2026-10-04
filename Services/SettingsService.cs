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
                Enum.IsDefined(s.Houses) && Enum.IsDefined(s.Node))
                return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {_path}: {ex.Message}");
        }
        return ChartSettings.Default;
    }

    public void Save(ChartSettings settings)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {_path}: {ex.Message}");
        }
    }
}
