using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Persists user-entered charts (reusing the Celebrity model) to
// %LOCALAPPDATA%\Lore\mycharts.json so they survive across sessions.
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

    public UserChartService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "mycharts.json");
    }

    public IReadOnlyList<Celebrity> Charts => _charts;

    public async Task LoadAsync()
    {
        if (!File.Exists(_path)) { _charts = []; return; }
        try
        {
            await using var stream = File.OpenRead(_path);
            _charts = await JsonSerializer.DeserializeAsync<List<Celebrity>>(stream, JsonOpts) ?? [];
        }
        catch
        {
            _charts = []; // corrupt file shouldn't block the app
        }
    }

    public async Task AddAsync(Celebrity chart)
    {
        _charts.Add(chart);
        await SaveAsync();
    }

    public async Task RemoveAsync(Celebrity chart)
    {
        _charts.RemoveAll(c => c.Id == chart.Id);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, _charts, JsonOpts);
    }
}
