using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Loads the bundled city list and provides prefix search for the Add-Chart dialog's
// autocomplete, which autofills latitude/longitude/UTC offset from the chosen city.
public sealed class CityService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private List<City> _cities = [];

    public async Task LoadAsync(string jsonPath)
    {
        if (_cities.Count > 0) return;
        if (!File.Exists(jsonPath)) return;

        await using var stream = File.OpenRead(jsonPath);
        _cities = await JsonSerializer.DeserializeAsync<List<City>>(stream, JsonOpts) ?? [];
    }

    public IReadOnlyList<City> Search(string? query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        string q = query.Trim();
        return _cities
            .Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Admin.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Country.Contains(q, StringComparison.OrdinalIgnoreCase))
            // Rank: names that start with the query first, then by population so the
            // major city (Paris, France) beats the small one (Paris, Texas).
            .OrderBy(c => c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(c => c.Population)
            .ThenBy(c => c.Name)
            .Take(max)
            .ToList();
    }
}
