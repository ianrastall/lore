using Lore.Models;
using System.Text.Json;

namespace Lore.Services;

// Loads the bundled hospital list and provides prefix search for the Add-Chart
// dialog's autocomplete, which autofills latitude/longitude from the chosen
// hospital. The time zone is then resolved from those coordinates, so no tz data
// is needed here (mirrors CityService, minus population ranking).
public sealed class HospitalService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private List<Hospital> _hospitals = [];

    public async Task LoadAsync(string jsonPath)
    {
        if (_hospitals.Count > 0) return;
        if (!File.Exists(jsonPath)) return;

        await using var stream = File.OpenRead(jsonPath);
        _hospitals = await JsonSerializer.DeserializeAsync<List<Hospital>>(stream, JsonOpts) ?? [];
    }

    public IReadOnlyList<Hospital> Search(string? query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        string q = query.Trim();
        // With a couple of hundred thousand entries, one letter matches most of the
        // list and its suggestions are arbitrary anyway; wait for a second letter so
        // each keystroke stays quick.
        if (q.Length < 2) return [];
        return _hospitals
            .Where(h => h.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        h.City.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        h.Country.Contains(q, StringComparison.OrdinalIgnoreCase))
            // Rank: names that start with the query first (no population to fall back
            // on as cities do), then alphabetically for a stable list.
            .OrderBy(h => h.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(h => h.Name)
            .Take(max)
            .ToList();
    }
}
