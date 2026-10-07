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
        // Every word typed must be found, but each may be in the name, the region or the
        // country: "Paris France" and "Springfield Illinois" both find their city.
        string[] words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _cities
            .Where(c => words.All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                       c.Admin.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                                       c.Country.Contains(w, StringComparison.OrdinalIgnoreCase)))
            // Rank: names that start with the query (or its first word) first, then by
            // population so the major city (Paris, France) beats the small one (Paris, Texas).
            .OrderBy(c => c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0
                        : c.Name.StartsWith(words[0], StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenByDescending(c => c.Population)
            .ThenBy(c => c.Name)
            .Take(max)
            .ToList();
    }
}
