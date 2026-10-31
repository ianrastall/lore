using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Services;

namespace Lore;

// What the shell remembers: the accent, the window, the Charts pane and the size of the
// reading text. Kept in shell.json in Lore's folder, apart from what Lore.Core owns
// (settings.json for the calculations, ui.json for the chart and view last open).
public sealed class ShellPreferences : Jupiter.UI.IAccentPreferences
{
    private string _file = "";

    public bool UseCustomAccent { get; set; }
    public string? CustomAccent { get; set; }

    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public bool ChartsPaneVisible { get; set; } = true;
    public double ChartsPaneWidth { get; set; } = 320;

    public int ReadingSize { get; set; } = Views.ReadingText.DefaultStep;

    public static ShellPreferences Load()
    {
        string file = Path.Combine(AppFolder.Path, "shell.json");
        var preferences = new ShellPreferences();
        try
        {
            if (File.Exists(file))
                preferences = JsonSerializer.Deserialize(File.ReadAllText(file), ShellJson.Default.ShellPreferences) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not read {file}: {ex.Message}");
            preferences = new();
        }
        preferences._file = file;
        preferences.ChartsPaneWidth = Math.Clamp(preferences.ChartsPaneWidth, 240, 800);
        return preferences;
    }

    // Written beside the real file and swapped in, so a failed write leaves the old one whole.
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temporary = _file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, ShellJson.Default.ShellPreferences));
            File.Move(temporary, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"Could not save {_file}: {ex.Message}");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ShellPreferences))]
internal sealed partial class ShellJson : JsonSerializerContext;
